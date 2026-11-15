using System.Security.Cryptography;
using System.Text.Json;
using LibraryLending.Application;
using LibraryLending.Infrastructure.Persistence;
using LibraryLending.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 3 || args[0] != "initialize")
{
    Console.Error.WriteLine("Usage: LibraryLending.DemoTool initialize <absolute-new-database.db> <absolute-new-credentials.json>");
    Console.Error.WriteLine("Both destinations must be outside the repository. Existing files are never overwritten.");
    return 2;
}

try
{
    await InitializeAsync(args[1], args[2]);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Demo initialization failed: {exception.Message}");
    Console.Error.WriteLine("Do not start a server with an incomplete database. Use new destinations when retrying.");
    return 1;
}

static async Task InitializeAsync(string databaseArgument, string credentialsArgument)
{
    var repository = FindRepository();
    var databasePath = ValidateDestination(databaseArgument, ".db", repository);
    var credentialsPath = ValidateDestination(credentialsArgument, ".json", repository);
    foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        if (Path.Exists(databasePath + suffix))
            throw new IOException("The database destination already has a SQLite sidecar file.");

    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(credentialsPath)!);

    // CreateNew prevents accidentally replacing either a real database or existing credentials.
    using (new FileStream(databasePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
    await using var credentialsFile = new FileStream(credentialsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(databasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.SetUnixFileMode(credentialsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    var connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath,
        Mode = SqliteOpenMode.ReadWrite,
        Pooling = false
    }.ToString();
    var services = new ServiceCollection();
    // No console logger: account credentials are written only to the explicit credentials file.
    services.AddLogging();
    services.AddDbContext<LibraryDbContext>(options => options.UseSqlite(connectionString));
    services.AddIdentityCore<IdentityUser>(options =>
    {
        options.Password.RequiredLength = 10;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
    }).AddRoles<IdentityRole>().AddEntityFrameworkStores<LibraryDbContext>();
    await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await db.Database.MigrateAsync();

    // The seed and all password changes commit together. A failed rotation cannot leave
    // a usable sample database containing the publicly documented development password.
    await using var transaction = await db.Database.BeginTransactionAsync();
    await DemoSeeder.SeedAsync(db, users, roles, TimeProvider.System, new LibraryOptions());
    var expectedAccounts = new[] { (Email: "librarian@library.local", Role: "Librarian") }
        .Concat(DemoSeeder.LoginMemberNumbers.Select(number => (Email: $"member{number}@library.local", Role: "Member")))
        .ToArray();
    if (await db.Users.CountAsync() != expectedAccounts.Length)
        throw new InvalidOperationException("Unexpected seeded account count.");

    var accounts = new List<DemoAccount>();
    foreach (var expected in expectedAccounts)
    {
        var user = await users.FindByEmailAsync(expected.Email)
            ?? throw new InvalidOperationException("A seeded account is missing.");
        // The fixed prefix guarantees Identity's character requirements; the remaining
        // 192 bits are independently random for every account and every initialization.
        var password = "Bb9!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        var changed = await users.ChangePasswordAsync(user, DemoSeeder.Password, password);
        if (!changed.Succeeded)
            throw new InvalidOperationException("Password rotation failed: " + string.Join(", ", changed.Errors.Select(error => error.Code)));
        accounts.Add(new DemoAccount(expected.Email, expected.Role, password));
    }

    // Reload persisted hashes instead of checking only the objects changed above.
    db.ChangeTracker.Clear();
    foreach (var account in accounts)
    {
        var user = await users.FindByEmailAsync(account.Email)
            ?? throw new InvalidOperationException("A rotated account is missing.");
        if (!await users.CheckPasswordAsync(user, account.Password)
            || await users.CheckPasswordAsync(user, DemoSeeder.Password)
            || !await users.IsInRoleAsync(user, account.Role))
            throw new InvalidOperationException("A rotated account failed password or role verification.");
    }

    var counts = new DemoCounts(await db.Users.CountAsync(), await db.Members.CountAsync(),
        await db.Books.CountAsync(), await db.Copies.CountAsync(), await db.Loans.CountAsync(),
        await db.Reservations.CountAsync());
    if (counts != new DemoCounts(4, 10, 30, 60, 17, 2))
        throw new InvalidOperationException("The seeded sample data did not match the expected counts.");

    await JsonSerializer.SerializeAsync(credentialsFile,
        new DemoCredentials(DateTimeOffset.UtcNow, databasePath, accounts),
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    await credentialsFile.FlushAsync();
    await transaction.CommitAsync();

    Console.WriteLine("Initialized isolated BookBridge demo database.");
    Console.WriteLine($"Verified {counts.Users} accounts, {counts.Members} members, {counts.Books} books, {counts.Copies} copies, {counts.Loans} loans, {counts.Reservations} reservations.");
    Console.WriteLine($"Verified all {counts.Users} private passwords; the development password is rejected for every account.");
    Console.WriteLine("Credentials were saved to the requested local JSON file. Keep it private and outside Git.");
}

static string ValidateDestination(string argument, string extension, string repository)
{
    if (!Path.IsPathFullyQualified(argument))
        throw new ArgumentException("Destination paths must be absolute.");
    var path = Path.GetFullPath(argument);
    var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    if (!Path.GetExtension(path).Equals(extension, comparison))
        throw new ArgumentException($"Expected a {extension} destination.");
    if (path.Equals(repository, comparison) || path.StartsWith(repository + Path.DirectorySeparatorChar, comparison))
        throw new ArgumentException("Demo database and credentials must be outside the repository.");
    if (Path.Exists(path))
        throw new IOException("A destination already exists; refusing to overwrite it.");

    // Reject symlink/junction ancestors so a seemingly external path cannot resolve
    // back into the repository or another unexpected location.
    for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
        if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Destination directories must not contain symlinks or junctions.");
    return path;
}

static string FindRepository()
{
    foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "LibraryLending.sln")))
                return Path.TrimEndingDirectorySeparator(directory.FullName);
    throw new InvalidOperationException("Run this tool from the LibraryLending repository or its build output.");
}

internal sealed record DemoAccount(string Email, string Role, string Password);
internal sealed record DemoCredentials(DateTimeOffset CreatedAtUtc, string DatabasePath, IReadOnlyList<DemoAccount> Accounts);
internal sealed record DemoCounts(int Users, int Members, int Books, int Copies, int Loans, int Reservations);
