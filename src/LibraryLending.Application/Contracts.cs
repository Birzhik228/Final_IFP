using LibraryLending.Domain;
namespace LibraryLending.Application;

public sealed record Actor(string UserId, bool IsLibrarian, int? MemberId);
public sealed record BookQuery(string Search = "", string Category = "", bool AvailableOnly = false, int Page = 1, int PageSize = 12, string Sort = "title");
public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{ public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize)); }
public sealed record BookDto(int Id, string Title, string Authors, string Isbn, string Category, int Year, string Description, string CoverUrl, int AvailableCopies, int TotalCopies, int QueueLength);
public sealed record CopyDto(int Id, string Barcode, CopyStatus Status);
public sealed record BookDetailDto(BookDto Book, IReadOnlyList<CopyDto> Copies);
public sealed record MemberDto(int Id, string Name, string Email, string Phone, MembershipStatus Status, int BorrowingLimit, int ActiveLoans, bool HasAccount = false);
public sealed record LoanDto(int Id, int CopyId, int MemberId, string MemberName, string BookTitle, string Barcode, DateTime BorrowedAtUtc, DateOnly DueDate, DateTime? ReturnedAtUtc, int DaysOverdue, long FineMinor);
public sealed record ReservationDto(int Id, int BookId, string BookTitle, int MemberId, string MemberName, ReservationStatus Status, int? AssignedCopyId, string? Barcode, DateTime CreatedAtUtc, DateTime? PickupDeadlineUtc, int QueuePosition);
public sealed record BorrowRequestDto(int Id, int BookId, string BookTitle, int MemberId, string MemberName, BorrowRequestStatus Status, DateTime CreatedAtUtc, DateTime? ReviewedAtUtc, string ReviewNote, int? LoanId, string? Barcode, DateOnly? DueDate);
public sealed record NotificationDto(int Id, string Message, string Kind, DateTime CreatedAtUtc, bool IsRead);
public sealed record PopularBookDto(string Title, int BorrowCount);
public sealed record DashboardDto(int Books, int AvailableCopies, int ActiveLoans, int OverdueLoans, int WaitingReservations, int Members, IReadOnlyList<PopularBookDto> PopularBooks);
public sealed record BookInput(string Title, string Authors, string Isbn, string Category, int Year, string Description, string CoverUrl, int Copies = 1);
public sealed record MemberInput(string Name, string Email, string Phone, MembershipStatus Status, int BorrowingLimit = 5);

public interface ILibraryService
{
    Task<PageResult<BookDto>> SearchBooksAsync(BookQuery query, CancellationToken ct = default);
    Task<BookDetailDto?> GetBookAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MemberDto>> GetMembersAsync(Actor actor, CancellationToken ct = default);
    Task<IReadOnlyList<LoanDto>> GetLoansAsync(Actor actor, bool overdueOnly = false, string search = "", string sort = "due", CancellationToken ct = default);
    Task<IReadOnlyList<ReservationDto>> GetReservationsAsync(Actor actor, CancellationToken ct = default);
    Task<IReadOnlyList<BorrowRequestDto>> GetBorrowRequestsAsync(Actor actor, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(Actor actor, CancellationToken ct = default);
    Task<DashboardDto> GetDashboardAsync(Actor actor, CancellationToken ct = default);
    Task<Result<int>> SaveBookAsync(Actor actor, int? id, BookInput input, CancellationToken ct = default);
    Task<Result<int>> AddCopyAsync(Actor actor, int bookId, string barcode, CancellationToken ct = default);
    Task<Result<int>> SaveMemberAsync(Actor actor, int? id, MemberInput input, CancellationToken ct = default);
    Task<Result<int>> BorrowAsync(Actor actor, int copyId, int memberId, CancellationToken ct = default);
    Task<Result<int>> RequestBorrowAsync(Actor actor, int bookId, CancellationToken ct = default);
    Task<Result<int>> AcceptBorrowRequestAsync(Actor actor, int requestId, CancellationToken ct = default);
    Task<Result<int>> RejectBorrowRequestAsync(Actor actor, int requestId, string reason = "", CancellationToken ct = default);
    Task<Result<int>> CancelBorrowRequestAsync(Actor actor, int requestId, CancellationToken ct = default);
    Task<Result<int>> ReturnAsync(Actor actor, int loanId, CancellationToken ct = default);
    Task<Result<int>> ReserveAsync(Actor actor, int bookId, int memberId, CancellationToken ct = default);
    Task<Result<int>> PickupAsync(Actor actor, int reservationId, CancellationToken ct = default);
    Task<Result<int>> CancelReservationAsync(Actor actor, int reservationId, CancellationToken ct = default);
    Task<Result<int>> MarkLostAsync(Actor actor, int copyId, CancellationToken ct = default);
    Task MarkNotificationsReadAsync(Actor actor, CancellationToken ct = default);
    Task ProcessDeadlinesAsync(CancellationToken ct = default);
}

public interface INotificationPublisher { Task PublishAsync(string userId, CancellationToken ct = default); }
public sealed class LibraryOptions
{
    public int LoanDays { get; set; } = 14;
    public int PickupHours { get; set; } = 72;
    public long DailyFineMinor { get; set; } = 100;
    public string CurrencyLabel { get; set; } = "CU";
    public string TimeZoneId { get; set; } = "UTC";
    public int WorkerIntervalSeconds { get; set; } = 60;
    public DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)));
}
