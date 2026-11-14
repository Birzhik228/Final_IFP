namespace LibraryLending.Domain;

public sealed class Loan
{
    public int Id { get; set; }
    public int BookCopyId { get; set; }
    public int MemberId { get; set; }
    public DateTime BorrowedAtUtc { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    // Snapshot the rate at checkout so a later policy change cannot alter an existing loan.
    public long DailyFineMinor { get; set; }
    public long? FinalFineMinor { get; set; }
    public BookCopy BookCopy { get; set; } = null!;
    public Member Member { get; set; } = null!;
}
