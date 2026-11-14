using LibraryLending.Application;
using LibraryLending.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LibraryLending.Tests.Integration;

public sealed class TestClock : TimeProvider
{
    private long ticks = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero).Ticks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
}

public sealed class LibraryFactory : WebApplicationFactory<Program>
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "LibraryLending.Tests", Guid.NewGuid().ToString("N"));
    public TestClock Clock { get; } = new();
    public LibraryOptions Options { get; } = new() { WorkerIntervalSeconds = 3600 };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        var connectionString = $"Data Source={Path.Combine(directory, "library.db")};Default Timeout=3;Pooling=False";
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Library", connectionString);
        builder.UseSetting("Demo:Seed", "true");
        builder.UseSetting("BackgroundJobs:Enabled", "false");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Library"] = connectionString,
            ["Demo:Seed"] = "true",
            ["BackgroundJobs:Enabled"] = "false",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning"
        }));
        builder.ConfigureTestServices(services =>
        {
            // Real SQLite and real migrations remain in use, but each test owns its database and clock.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<LibraryOptions>();
            services.AddSingleton(Options);
            services.RemoveAll<DbContextOptions<LibraryDbContext>>();
            services.RemoveAll<IDbContextFactory<LibraryDbContext>>();
            services.AddDbContextFactory<LibraryDbContext>(options => options.UseSqlite(connectionString));
            foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService) &&
                x.ImplementationType is not null && typeof(BackgroundService).IsAssignableFrom(x.ImplementationType)).ToArray())
                services.Remove(descriptor);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        // Only the unique directory owned by this factory is removed, never a caller-supplied path.
        var ownedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LibraryLending.Tests")) + Path.DirectorySeparatorChar;
        var ownedDirectory = Path.GetFullPath(directory);
        if (ownedDirectory.StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(ownedDirectory))
            Directory.Delete(ownedDirectory, recursive: true);
    }
}

public abstract class LibraryTestBase : IAsyncLifetime
{
    protected LibraryFactory Factory { get; } = new();
    protected Actor Librarian { get; private set; } = null!;
    protected Actor MemberActor { get; private set; } = null!;
    protected int EligibleMemberId { get; private set; }
    private int nextBook = 1000;
    private int nextMember;

    public async Task InitializeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var librarian = (await users.GetUsersInRoleAsync("Librarian")).First();
        Librarian = new Actor(librarian.Id, true, null);
        var service = scope.ServiceProvider.GetRequiredService<ILibraryService>();
        var members = await service.GetMembersAsync(Librarian);
        var overdueIds = (await service.GetLoansAsync(Librarian, true)).Select(x => x.MemberId).ToHashSet();
        EligibleMemberId = members.First(x => x.Status == Domain.MembershipStatus.Active && x.ActiveLoans < x.BorrowingLimit && !overdueIds.Contains(x.Id)).Id;
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LibraryDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var eligible = await db.Members.SingleAsync(x => x.Id == EligibleMemberId);
        MemberActor = new Actor(eligible.IdentityUserId!, false, eligible.Id);
    }

    public Task DisposeAsync() => Factory.DisposeAsync().AsTask();

    protected async Task<T> Use<T>(Func<ILibraryService, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILibraryService>());
    }

    protected async Task Use(Func<ILibraryService, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ILibraryService>());
    }

    protected async Task<T> Database<T>(Func<LibraryDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LibraryDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        return await action(db);
    }

    protected async Task<BookDetailDto> NewBook(int copies = 1)
    {
        var number = Interlocked.Increment(ref nextBook);
        var result = await Use(s => s.SaveBookAsync(Librarian, null,
            new BookInput($"Integration Book {number}", "Test Author", $"978000000{number:D4}", "Integration", 2024, "An isolated test edition.", "", copies)));
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
        return (await Use(s => s.GetBookAsync(result.Value)))!;
    }

    protected async Task<int> NewMember(int limit = 5)
    {
        var number = Interlocked.Increment(ref nextMember);
        var result = await Use(s => s.SaveMemberAsync(Librarian, null,
            new MemberInput($"Integration Member {number}", $"integration{number}@example.test", "", Domain.MembershipStatus.Active, limit)));
        if (!result.IsSuccess) throw new InvalidOperationException(result.Error);
        return result.Value;
    }
}
