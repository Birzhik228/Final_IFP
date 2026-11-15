using LibraryLending.Application;
using LibraryLending.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LibraryLending.Infrastructure.Identity;

public sealed class ProfileService(IDbContextFactory<LibraryDbContext> contexts, TimeProvider clock) : IProfileService
{
    public async Task<AccountProfileDto?> GetOwnAsync(Actor actor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actor.UserId)) return null;

        await using var db = await contexts.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId, ct);
        if (user is null || user.LockoutEnabled && user.LockoutEnd > clock.GetUtcNow()) return null;

        // Re-resolve roles and the member link from persisted Identity data. A stale or
        // forged member ID/role on an Actor must never expose another member's details.
        var roles = await (from assignment in db.UserRoles
            join role in db.Roles on assignment.RoleId equals role.Id
            where assignment.UserId == user.Id
            select role.NormalizedName).ToArrayAsync(ct);
        if (roles.Contains("LIBRARIAN"))
        {
            var displayName = await db.UserClaims.AsNoTracking()
                .Where(x => x.UserId == user.Id && x.ClaimType == "display_name")
                .Select(x => x.ClaimValue).FirstOrDefaultAsync(ct);
            return new AccountProfileDto(string.IsNullOrWhiteSpace(displayName) ? "Library librarian" : displayName,
                user.Email ?? "", user.PhoneNumber ?? "", "Librarian", null, null, null, null, null);
        }

        if (!roles.Contains("MEMBER")) return null;
        var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(x => x.IdentityUserId == user.Id, ct);
        if (member is null) return null;
        var activeLoans = await db.Loans.CountAsync(x => x.MemberId == member.Id && x.ReturnedAtUtc == null, ct);
        return new AccountProfileDto(member.Name, user.Email ?? member.Email, member.Phone, "Member", member.Id,
            member.JoinedAtUtc, member.Status, member.BorrowingLimit, activeLoans);
    }
}
