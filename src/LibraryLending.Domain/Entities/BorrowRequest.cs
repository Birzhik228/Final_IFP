namespace LibraryLending.Domain;

/// <summary>A member's request to issue an available title. Pending requests do not hold copies.</summary>
public sealed class BorrowRequest
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int MemberId { get; set; }
    public BorrowRequestStatus Status { get; set; } = BorrowRequestStatus.Pending;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewerUserId { get; set; }
    public string ReviewNote { get; set; } = string.Empty;
    public int? LoanId { get; set; }
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public Book Book { get; set; } = null!;
    public Member Member { get; set; } = null!;
    public Loan? Loan { get; set; }
}
