namespace LibraryLending.Domain;

public sealed class Reservation
{
    public int Id { get; set; }
    public int BookId { get; set; }
    public int MemberId { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Waiting;
    public int? AssignedCopyId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadyAtUtc { get; set; }
    public DateTime? PickupDeadlineUtc { get; set; }
    public int? FulfilledLoanId { get; set; }
    public string? ClosureReason { get; set; }
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public Book Book { get; set; } = null!;
    public Member Member { get; set; } = null!;
    public BookCopy? AssignedCopy { get; set; }
}
