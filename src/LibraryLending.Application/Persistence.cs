using LibraryLending.Domain;
namespace LibraryLending.Application;

// One session represents one atomic use case; persistence details stay behind this boundary.
public interface ILibrarySession
{
    Task<Book?> BookAsync(int id, CancellationToken ct);
    Task<BookCopy?> CopyAsync(int id, CancellationToken ct);
    Task<Member?> MemberAsync(int id, CancellationToken ct);
    Task<Loan?> LoanAsync(int id, CancellationToken ct);
    Task<Reservation?> ReservationAsync(int id, CancellationToken ct);
    Task<BorrowRequest?> BorrowRequestAsync(int id, CancellationToken ct);
    Task<bool> HasPendingBorrowRequestAsync(int bookId, int memberId, CancellationToken ct);
    Task<List<string>> LibrarianUserIdsAsync(CancellationToken ct);
    Task<List<Loan>> OpenLoansAsync(int? memberId, CancellationToken ct);
    Task<List<Reservation>> ActiveReservationsAsync(int? bookId, CancellationToken ct);
    Task<List<BookCopy>> AvailableCopiesAsync(int bookId, CancellationToken ct);
    Task<Category> CategoryAsync(string name, CancellationToken ct);
    Task<Author> AuthorAsync(string name, CancellationToken ct);
    Task<bool> IsbnExistsAsync(string isbn, int? exceptId, CancellationToken ct);
    Task<bool> EmailExistsAsync(string email, int? exceptId, CancellationToken ct);
    Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct);
    Task NotifyAsync(string? userId, string kind, string message, string dedupeKey, DateTime now, CancellationToken ct);
    void Add<T>(T entity) where T : class;
    Task SaveAsync(CancellationToken ct);
}

public interface ILibraryRepository
{
    Task<Result<T>> ExecuteAsync<T>(Func<ILibrarySession, Task<Result<T>>> operation, CancellationToken ct);
    Task<PageResult<BookDto>> SearchBooksAsync(BookQuery query, CancellationToken ct);
    Task<BookDetailDto?> GetBookAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct);
    Task<IReadOnlyList<MemberDto>> GetMembersAsync(CancellationToken ct);
    Task<IReadOnlyList<LoanDto>> GetLoansAsync(int? memberId, bool overdueOnly, string search, string sort, CancellationToken ct);
    Task<IReadOnlyList<ReservationDto>> GetReservationsAsync(int? memberId, CancellationToken ct);
    Task<IReadOnlyList<BorrowRequestDto>> GetBorrowRequestsAsync(int? memberId, string? userId, CancellationToken ct);
    Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(string userId, CancellationToken ct);
    Task<DashboardDto> GetDashboardAsync(int? memberId, CancellationToken ct);
    Task MarkNotificationsReadAsync(string userId, CancellationToken ct);
}
