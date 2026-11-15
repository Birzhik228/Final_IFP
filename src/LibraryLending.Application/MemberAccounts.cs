using LibraryLending.Domain;

namespace LibraryLending.Application;

public static class MemberAccountPolicy
{
    public const int MaximumAccounts = 3;
}

public sealed record MemberAccountCapacity(int Used, int Maximum)
{
    public bool IsFull => Used >= Maximum;
}

public sealed record AccountCredentials(string Email, string TemporaryPassword)
{
    // Record-generated ToString would otherwise expose the password in accidental diagnostics.
    public override string ToString() => $"AccountCredentials {{ Email = {Email}, TemporaryPassword = [redacted] }}";
}

public interface IMemberAccountService
{
    Task<Result<MemberAccountCapacity>> GetCapacityAsync(Actor actor, CancellationToken ct = default);
    Task<Result<AccountCredentials>> ProvisionAsync(Actor actor, int memberId, CancellationToken ct = default);
}
