using LibraryLending.Domain;

namespace LibraryLending.Application;

public sealed record AccountProfileDto(
    string Name,
    string Email,
    string Phone,
    string Role,
    int? MemberId,
    DateTime? JoinedAtUtc,
    MembershipStatus? Status,
    int? BorrowingLimit,
    int? ActiveLoans)
{
    public int? RemainingLoans => BorrowingLimit is int limit && ActiveLoans is int active
        ? Math.Max(0, limit - active)
        : null;
}

public interface IProfileService
{
    /// <summary>Read the profile linked to the authenticated actor's Identity user ID.</summary>
    Task<AccountProfileDto?> GetOwnAsync(Actor actor, CancellationToken ct = default);
}
