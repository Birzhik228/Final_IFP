using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using LibraryLending.Infrastructure.Persistence;
using LibraryLending.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibraryLending.Tests.Integration;

public sealed class ProductionFactory : WebApplicationFactory<Program>
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "BookBridge.Production.Tests", Guid.NewGuid().ToString("N"));
    public bool Bootstrap { get; init; } = true;
    public string Password { get; init; } = "InitialAdminPassword9!";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(DirectoryPath);
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Library", $"Data Source={Path.Combine(DirectoryPath, "library.db")};Pooling=False");
        builder.UseSetting("DataProtection:KeyPath", Path.Combine(DirectoryPath, "keys"));
        builder.UseSetting("Database:ApplyMigrations", "true");
        builder.UseSetting("Demo:Seed", "true"); // Production must ignore an accidentally enabled demo flag.
        builder.UseSetting("Bootstrap:Enabled", Bootstrap.ToString());
        builder.UseSetting("Bootstrap:Email", "librarian@example.test");
        builder.UseSetting("Bootstrap:Password", Password);
        builder.UseSetting("BackgroundJobs:Enabled", "false");
    }
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        var ownedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "BookBridge.Production.Tests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(DirectoryPath).StartsWith(ownedRoot, StringComparison.OrdinalIgnoreCase)) Directory.Delete(DirectoryPath, true);
    }
}

public sealed class ProductionReadinessTests
{
    [Fact]
    public async Task Production_bootstraps_only_the_requested_admin_and_never_seeds_demo_records()
    {
        await using var factory = new ProductionFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        (await db.Books.CountAsync()).Should().Be(0);
        (await db.Users.CountAsync()).Should().Be(1);
        (await db.Users.AnyAsync(user => user.Email == "librarian@library.local")).Should().BeFalse();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var admin = (await users.FindByEmailAsync("librarian@example.test"))!;
        (await users.IsInRoleAsync(admin, "Librarian")).Should().BeTrue();
        (await users.CheckPasswordAsync(admin, factory.Password)).Should().BeTrue();
    }

    [Fact]
    public async Task Production_http_redirects_to_https_and_does_not_trust_arbitrary_forwarded_headers()
    {
        await using var factory = new ProductionFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("http://example.test"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var response = await client.GetAsync("/account/login");
        response.StatusCode.Should().Be(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location!.Scheme.Should().Be("https");
    }

    [Fact]
    public async Task Production_cookie_is_secure_and_key_ring_is_persisted()
    {
        await using var factory = new ProductionFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://example.test"), AllowAutoRedirect = false, HandleCookies = true });
        var login = await client.GetAsync("/account/login");
        login.Headers.GetValues("Strict-Transport-Security").Should().NotBeEmpty();
        login.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        var html = await login.Content.ReadAsStringAsync();
        var input = Regex.Matches(html, "<input\\b[^>]*>").Select(match => match.Value).Single(value => value.Contains("__RequestVerificationToken"));
        var token = WebUtility.HtmlDecode(Regex.Match(input, "value=\"([^\"]+)\"").Groups[1].Value);
        var response = await client.PostAsync("/account/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["email"] = "librarian@example.test", ["password"] = factory.Password, ["__RequestVerificationToken"] = token }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var authCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(".AspNetCore.Identity.Application="));
        authCookie.ToLowerInvariant().Should().Contain("secure").And.Contain("httponly").And.Contain("samesite=lax");
        Directory.GetFiles(Path.Combine(factory.DirectoryPath, "keys"), "*.xml").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Production_rejects_public_demo_bootstrap_password()
    {
        await using var factory = new ProductionFactory { Password = "LibraryDemo1!" };
        var act = () => factory.CreateClient();
        act.Should().Throw<InvalidOperationException>().WithMessage("*public demo password*");
    }

    [Fact]
    public async Task Production_can_initialize_roles_without_enabling_an_admin_bootstrap()
    {
        await using var factory = new ProductionFactory { Bootstrap = false };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
        (await db.Users.CountAsync()).Should().Be(0);
        (await db.Roles.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Readiness_checks_the_database_instead_of_only_the_web_process()
    {
        await using var factory = new ProductionFactory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://example.test") });
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        using (var scope = factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<LibraryDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE BookAuthor; DROP TABLE Books;");
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Bootstrap_does_not_reset_an_existing_administrator_password()
    {
        await using var factory = new ProductionFactory();
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = (await users.FindByEmailAsync("librarian@example.test"))!;
        await users.ChangePasswordAsync(user, factory.Password, "ReplacementPassword8!");
        await IdentityBootstrapper.InitializeAsync(scope.ServiceProvider, scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
        (await users.CheckPasswordAsync(user, "ReplacementPassword8!")).Should().BeTrue();
    }
}

public sealed class OperationalRegressionTests : LibraryTestBase
{
    [Fact]
    public async Task Online_backup_and_restore_preserve_committed_wal_loans_and_queue_links()
    {
        var book = await NewBook();
        using var scope = Factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LibraryDbContext>>();
        await using var original = await dbFactory.CreateDbContextAsync();
        // Keep this connection open before checkout so the writer closing its connection
        // cannot checkpoint/delete the WAL as the last connection to the database.
        await original.Database.OpenConnectionAsync();
        await using (var journalMode = original.Database.GetDbConnection().CreateCommand())
        {
            journalMode.CommandText = "PRAGMA journal_mode";
            (await journalMode.ExecuteScalarAsync())!.ToString().Should().Be("wal");
        }
        var loan = await Use(service => service.BorrowAsync(Librarian, book.Copies[0].Id, EligibleMemberId));
        loan.IsSuccess.Should().BeTrue();
        var source = original.Database.GetDbConnection().DataSource;
        var wal = source + "-wal";
        File.Exists(wal).Should().BeTrue("the backup must exercise committed WAL data rather than a checkpointed main file");
        new FileInfo(wal).Length.Should().BeGreaterThan(32, "the WAL must contain frames beyond its header");
        var directory = Path.Combine(Path.GetTempPath(), "BookBridge.Backup.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var backup = Path.Combine(directory, "snapshot.db");
            var restored = Path.Combine(directory, "restored.db");
            await SqliteBackup.CreateAsync(source, backup);
            await SqliteBackup.CreateAsync(backup, restored);
            await using var restoredDb = new LibraryDbContext(new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite($"Data Source={restored};Pooling=False").Options);
            (await restoredDb.Loans.AnyAsync(value => value.Id == loan.Value && value.ReturnedAtUtc == null)).Should().BeTrue();
            (await restoredDb.Reservations.CountAsync()).Should().Be(await original.Reservations.CountAsync());
            var duplicate = () => SqliteBackup.CreateAsync(source, backup);
            await duplicate.Should().ThrowAsync<IOException>();
            await SqliteBackup.VerifyAsync(backup);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Interactive_session_validation_rejects_changed_password_stamp_and_revoked_roles()
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var principalFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser>>();
        var user = (await users.FindByEmailAsync("librarian@library.local"))!;
        var principal = await principalFactory.CreateAsync(user);
        var validator = scope.ServiceProvider.GetRequiredService<IdentitySessionValidator>();
        (await validator.IsValidAsync(principal)).Should().BeTrue();
        await users.UpdateSecurityStampAsync(user);
        (await validator.IsValidAsync(principal)).Should().BeFalse();
        principal = await principalFactory.CreateAsync(user);
        await users.RemoveFromRoleAsync(user, "Librarian");
        (await validator.IsValidAsync(principal)).Should().BeFalse();
    }
}
