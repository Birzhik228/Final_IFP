using System.ComponentModel.DataAnnotations;
using LibraryLending.Infrastructure.Persistence;
using LibraryLending.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LibraryLending.Web;

public static class IdentityBootstrapper
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var name in new[] { "Librarian", "Member" })
        {
            if (await roles.RoleExistsAsync(name)) continue;
            var created = await roles.CreateAsync(new IdentityRole(name));
            if (!created.Succeeded) throw new InvalidOperationException("Unable to initialize library roles.");
        }

        if (!configuration.GetValue("Bootstrap:Enabled", false)) return;
        var email = configuration["Bootstrap:Email"]?.Trim().ToLowerInvariant();
        var password = configuration["Bootstrap:Password"];
        if (string.IsNullOrEmpty(email) || !new EmailAddressAttribute().IsValid(email) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Bootstrap requires a valid email and a password supplied through deployment secrets.");
        if (password == DemoSeeder.Password)
            throw new InvalidOperationException("The public demo password cannot be used for administrator bootstrap.");

        var users = services.GetRequiredService<UserManager<IdentityUser>>();
        var existing = await users.GetUsersInRoleAsync("Librarian");
        if (existing.Any())
        {
            // Bootstrap is idempotent and never resets or elevates an existing account.
            if (existing.Any(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))) return;
            throw new InvalidOperationException("A librarian already exists. Disable administrator bootstrap.");
        }
        if (await users.FindByEmailAsync(email) is not null)
            throw new InvalidOperationException("Bootstrap cannot promote an existing member account.");

        var db = services.GetRequiredService<LibraryDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("Bootstrap password does not satisfy the configured password policy.");
        result = await users.AddToRoleAsync(user, "Librarian");
        if (!result.Succeeded) throw new InvalidOperationException("Unable to assign the initial librarian role.");
        await transaction.CommitAsync();
    }
}

public sealed class DatabaseReadinessCheck(IDbContextFactory<LibraryDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            if (!await db.Database.CanConnectAsync(cancellationToken)) return HealthCheckResult.Unhealthy("Database unavailable.");
            if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any()) return HealthCheckResult.Unhealthy("Schema migration required.");
            await db.Books.AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Database unavailable.");
        }
    }
}
