namespace LibraryLending.Domain;

public sealed class Member
{
    public int Id { get; set; }
    public string? IdentityUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;
    public int BorrowingLimit { get; set; } = 5;
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
}
