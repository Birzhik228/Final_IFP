using LibraryLending.Domain;

namespace LibraryLending.Application;

public sealed record AccountCredentials(string Email, string TemporaryPassword)
{
    // Record-generated ToString would otherwise expose the password in accidental diagnostics.
    public override string ToString() => $"AccountCredentials {{ Email = {Email}, TemporaryPassword = [redacted] }}";
}

public interface IMemberAccountService
{
    Task<Result<AccountCredentials>> ProvisionAsync(Actor actor, int memberId, CancellationToken ct = default);
}
