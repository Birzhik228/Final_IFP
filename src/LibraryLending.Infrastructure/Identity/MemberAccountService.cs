using System.Security.Cryptography;
using LibraryLending.Application;
using LibraryLending.Domain;
using LibraryLending.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LibraryLending.Infrastructure.Identity;

/// <summary>Resolve in a fresh DI scope per operation so Identity and the member link share one context.</summary>
public sealed class MemberAccountService(UserManager<IdentityUser> users, LibraryDbContext db) : IMemberAccountService
{
    public async Task<Result<AccountCredentials>> ProvisionAsync(Actor actor, int memberId, CancellationToken ct = default)
    {
        if (!actor.IsLibrarian)
            return Result<AccountCredentials>.Failure("Only librarians can create member login accounts.");

        try
        {
            // SQLite's immediate write transaction protects the read/check/create sequence, including
            // competing requests for the same member. Identity uses this same scoped DbContext.
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var member = await db.Members.SingleOrDefaultAsync(x => x.Id == memberId, ct);
            if (member is null)
                return Result<AccountCredentials>.Failure("Member was not found.");
            if (member.Status != MembershipStatus.Active)
                return Result<AccountCredentials>.Failure("Only active members can receive a login account.");
            if (!string.IsNullOrEmpty(member.IdentityUserId))
                return Result<AccountCredentials>.Failure("This member already has a login account.");

            var email = member.Email.Trim().ToLowerInvariant();
            if (await users.FindByEmailAsync(email) is not null || await users.FindByNameAsync(email) is not null)
                return Result<AccountCredentials>.Failure("An account already uses this email. Ask the librarian to check the member record.");
            if (!await db.Roles.AnyAsync(x => x.NormalizedName == "MEMBER", ct))
                return Result<AccountCredentials>.Failure("The Member role has not been configured.");

            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + "Aa1!";
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return Result<AccountCredentials>.Failure(string.Join(" ", created.Errors.Select(x => x.Description)));
            }

            ct.ThrowIfCancellationRequested();
            var assigned = await users.AddToRoleAsync(user, "Member");
            if (!assigned.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                return Result<AccountCredentials>.Failure("The member role could not be assigned. No account was created.");
            }

            member.IdentityUserId = user.Id;
            member.Version = Guid.NewGuid().ToString("N");
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result<AccountCredentials>.Success(new AccountCredentials(email, password));
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Result<AccountCredentials>.Failure("The member changed during account creation. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            db.ChangeTracker.Clear();
            return Result<AccountCredentials>.Failure("An account or member link already exists. Refresh and try again.");
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            db.ChangeTracker.Clear();
            return Result<AccountCredentials>.Failure("The library is busy. Please try account creation again shortly.");
        }
    }
}
