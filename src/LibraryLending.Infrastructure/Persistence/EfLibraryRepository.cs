using LibraryLending.Application;
using LibraryLending.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace LibraryLending.Infrastructure.Persistence;

public sealed class EfLibraryRepository(IDbContextFactory<LibraryDbContext> factory, TimeProvider clock, LibraryOptions options, INotificationPublisher publisher, ILogger<EfLibraryRepository> logger, LibraryWriteCoordinator writes) : ILibraryRepository
{
    public async Task<Result<T>> ExecuteAsync<T>(Func<ILibrarySession, Task<Result<T>>> operation, CancellationToken ct)
    {
        var notifiedUsers = new HashSet<string>();
        var result = await ExecuteTransactionAsync(operation, notifiedUsers, ct);
        // The write gate and transaction have been released. A disconnected browser must
        // not turn committed circulation into a reported failure: delivery is best-effort.
        using var deliveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        foreach (var userId in notifiedUsers)
            try { await publisher.PublishAsync(userId, deliveryTimeout.Token).WaitAsync(deliveryTimeout.Token); }
            catch (Exception e) { logger.LogWarning(e, "Live notification delivery failed; persisted notification remains available."); }
        return result;
    }

    private async Task<Result<T>> ExecuteTransactionAsync<T>(Func<ILibrarySession, Task<Result<T>>> operation, HashSet<string> notifiedUsers, CancellationToken ct)
    {
        using var lease = await writes.AcquireAsync(ct);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            try
            {
                await db.Database.OpenConnectionAsync(ct);
                // Acquire SQLite's write reservation before reading predicates (limits, availability, queue).
                await using var tx = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
                await db.Database.UseTransactionAsync(tx, ct);
                var session = new EfLibrarySession(db);
                var result = await operation(session);
                if (!result.IsSuccess) { await tx.RollbackAsync(ct); return result; }
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                notifiedUsers.UnionWith(session.NotifiedUsers);
                return result;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2) { await Task.Delay(30 * (attempt + 1), ct); }
            catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6 && attempt < 2) { await Task.Delay(30 * (attempt + 1), ct); }
            catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 5 or 6 } && attempt < 2) { await Task.Delay(30 * (attempt + 1), ct); }
            catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 }) { return Result<T>.Failure("The change conflicts with an existing record. Refresh and try again."); }
            catch (DbUpdateConcurrencyException) { return Result<T>.Failure("Another operation changed this record. Refresh and try again."); }
            catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6) { return Result<T>.Failure("The library is busy. Please try again shortly."); }
            catch (DbUpdateException e) when (e.InnerException is SqliteException { SqliteErrorCode: 5 or 6 }) { return Result<T>.Failure("The library is busy. Please try again shortly."); }
        }
        return Result<T>.Failure("Please retry this operation.");
    }
    private static IQueryable<Book> BookGraph(LibraryDbContext db) => db.Books.AsNoTracking().Include(x => x.Category).Include(x => x.BookAuthors).ThenInclude(x => x.Author).Include(x => x.Copies).AsSplitQuery();
    private static BookDto MapBook(Book x, int queue) => new(x.Id, x.Title, string.Join("; ", x.BookAuthors.OrderBy(a => a.DisplayOrder).Select(a => a.Author.Name)), x.Isbn, x.Category?.Name ?? "", x.Year, x.Description, x.CoverUrl, x.Copies.Count(c => c.Status == CopyStatus.Available), x.Copies.Count, queue);
    public async Task<PageResult<BookDto>> SearchBooksAsync(BookQuery q, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = BookGraph(db).Where(x => !x.IsArchived);
        if (!string.IsNullOrWhiteSpace(q.Search)) { var term = q.Search.Trim().ToLower(); query = query.Where(x => x.Title.ToLower().Contains(term) || x.Isbn.Contains(term) || x.BookAuthors.Any(a => a.Author.Name.ToLower().Contains(term))); }
        if (!string.IsNullOrWhiteSpace(q.Category)) query = query.Where(x => x.Category!.Name == q.Category);
        if (q.AvailableOnly) query = query.Where(x => x.Copies.Any(c => c.Status == CopyStatus.Available));
        var total = await query.CountAsync(ct); var books = await query.OrderBy(x => x.Title).ThenBy(x => x.Id).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToListAsync(ct);
        var ids = books.Select(x => x.Id).ToList(); var counts = await db.Reservations.Where(r => ids.Contains(r.BookId) && r.Status == ReservationStatus.Waiting).GroupBy(r => r.BookId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
        return new(books.Select(x => MapBook(x, counts.GetValueOrDefault(x.Id))).ToList(), total, q.Page, q.PageSize);
    }
    public async Task<BookDetailDto?> GetBookAsync(int id, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); var book = await BookGraph(db).SingleOrDefaultAsync(x => x.Id == id, ct); return book is null ? null : new(MapBook(book, await db.Reservations.CountAsync(x => x.BookId == id && x.Status == ReservationStatus.Waiting, ct)), book.Copies.OrderBy(x => x.Barcode).Select(x => new CopyDto(x.Id, x.Barcode, x.Status)).ToList()); }
    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct) { await using var db = await factory.CreateDbContextAsync(ct); return await db.Categories.OrderBy(x => x.Name).Select(x => x.Name).ToListAsync(ct); }
    public async Task<IReadOnlyList<MemberDto>> GetMembersAsync(CancellationToken ct) { await using var db = await factory.CreateDbContextAsync(ct); return await db.Members.AsNoTracking().OrderBy(x => x.Name).Select(x => new MemberDto(x.Id, x.Name, x.Email, x.Phone, x.Status, x.BorrowingLimit, db.Loans.Count(l => l.MemberId == x.Id && l.ReturnedAtUtc == null), x.IdentityUserId != null)).ToListAsync(ct); }
    public async Task<IReadOnlyList<LoanDto>> GetLoansAsync(int? memberId, bool overdueOnly, string search, string sort, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var today = options.Today(clock);
        var q = db.Loans.AsNoTracking().AsQueryable(); if (memberId.HasValue) q = q.Where(x => x.MemberId == memberId);
        if (overdueOnly) q = q.Where(x => x.ReturnedAtUtc == null && x.DueDate < today);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToLower(); q = q.Where(x => x.Member.Name.ToLower().Contains(term) || x.BookCopy.Book.Title.ToLower().Contains(term) || x.BookCopy.Barcode.ToLower().Contains(term)); }
        q = sort switch { "member" => q.OrderBy(x => x.Member.Name), "book" => q.OrderBy(x => x.BookCopy.Book.Title), _ => q.OrderBy(x => x.ReturnedAtUtc != null).ThenBy(x => x.DueDate) };
        var rows = await q.Select(x => new { x.Id, x.BookCopyId, x.MemberId, Member = x.Member.Name, Title = x.BookCopy.Book.Title, x.BookCopy.Barcode, x.BorrowedAtUtc, x.DueDate, x.ReturnedAtUtc, x.DailyFineMinor, x.FinalFineMinor }).ToListAsync(ct);
        var result = rows.Select(x => new LoanDto(x.Id, x.BookCopyId, x.MemberId, x.Member, x.Title, x.Barcode, x.BorrowedAtUtc, x.DueDate, x.ReturnedAtUtc, x.ReturnedAtUtc.HasValue ? 0 : LendingRules.DaysOverdue(x.DueDate, today), x.FinalFineMinor ?? LendingRules.FineMinor(x.DueDate, today, x.DailyFineMinor))).ToList();
        return sort == "fine" ? result.OrderByDescending(x => x.FineMinor).ToList() : sort == "days" ? result.OrderByDescending(x => x.DaysOverdue).ToList() : result;
    }
    public async Task<IReadOnlyList<ReservationDto>> GetReservationsAsync(int? memberId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Reservations.AsNoTracking().AsQueryable();
        if (memberId.HasValue) query = query.Where(x => x.MemberId == memberId.Value);
        // Count earlier waiting reservations inside SQL. Only the requested member's records
        // are materialized, while their queue position still includes other waiting members.
        return await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Select(x => new ReservationDto(x.Id, x.BookId, x.Book.Title, x.MemberId, x.Member.Name,
                x.Status, x.AssignedCopyId, x.AssignedCopy == null ? null : x.AssignedCopy.Barcode,
                x.CreatedAtUtc, x.PickupDeadlineUtc,
                x.Status == ReservationStatus.Waiting
                    ? 1 + db.Reservations.Count(earlier => earlier.BookId == x.BookId
                        && earlier.Status == ReservationStatus.Waiting
                        && (earlier.CreatedAtUtc < x.CreatedAtUtc
                            || (earlier.CreatedAtUtc == x.CreatedAtUtc && earlier.Id < x.Id)))
                    : 0))
            .ToListAsync(ct);
    }
    public async Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(string userId, CancellationToken ct) { await using var db = await factory.CreateDbContextAsync(ct); return await db.Notifications.AsNoTracking().Where(x => x.UserId == userId).OrderByDescending(x => x.CreatedAtUtc).Take(100).Select(x => new NotificationDto(x.Id, x.Message, x.Kind, x.CreatedAtUtc, x.ReadAtUtc != null)).ToListAsync(ct); }
    public async Task MarkNotificationsReadAsync(string userId, CancellationToken ct) { await using var db = await factory.CreateDbContextAsync(ct); var now = clock.GetUtcNow().UtcDateTime; await db.Notifications.Where(x => x.UserId == userId && x.ReadAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReadAtUtc, now), ct); }
    public async Task<DashboardDto> GetDashboardAsync(int? memberId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var today = options.Today(clock); var loans = db.Loans.Where(x => !memberId.HasValue || x.MemberId == memberId);
        var popular = await loans.GroupBy(x => x.BookCopy.Book.Title).OrderByDescending(g => g.Count()).Take(5).Select(g => new PopularBookDto(g.Key, g.Count())).ToListAsync(ct);
        return new(await db.Books.CountAsync(ct), await db.Copies.CountAsync(x => x.Status == CopyStatus.Available, ct), await loans.CountAsync(x => x.ReturnedAtUtc == null, ct), await loans.CountAsync(x => x.ReturnedAtUtc == null && x.DueDate < today, ct), await db.Reservations.CountAsync(x => (!memberId.HasValue || x.MemberId == memberId) && x.Status == ReservationStatus.Waiting, ct), memberId.HasValue ? 1 : await db.Members.CountAsync(ct), popular);
    }
}

internal sealed class EfLibrarySession(LibraryDbContext db) : ILibrarySession
{
    public HashSet<string> NotifiedUsers { get; } = [];
    public Task<Book?> BookAsync(int id, CancellationToken ct) => db.Books.Include(x => x.BookAuthors).ThenInclude(x => x.Author).Include(x => x.Copies).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<BookCopy?> CopyAsync(int id, CancellationToken ct) => db.Copies.Include(x => x.Book).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Member?> MemberAsync(int id, CancellationToken ct) => db.Members.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Loan?> LoanAsync(int id, CancellationToken ct) => db.Loans.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<Reservation?> ReservationAsync(int id, CancellationToken ct) => db.Reservations.Include(x => x.Member).Include(x => x.Book).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<List<Loan>> OpenLoansAsync(int? memberId, CancellationToken ct) => db.Loans.Include(x => x.Member).Include(x => x.BookCopy).ThenInclude(x => x.Book).Where(x => x.ReturnedAtUtc == null && (!memberId.HasValue || x.MemberId == memberId)).ToListAsync(ct);
    public Task<List<Reservation>> ActiveReservationsAsync(int? bookId, CancellationToken ct) => db.Reservations.Include(x => x.Member).Include(x => x.Book).Where(x => (!bookId.HasValue || x.BookId == bookId) && (x.Status == ReservationStatus.Waiting || x.Status == ReservationStatus.Ready)).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
    public Task<List<BookCopy>> AvailableCopiesAsync(int bookId, CancellationToken ct) => db.Copies.Where(x => x.BookId == bookId && x.Status == CopyStatus.Available).ToListAsync(ct);
    public async Task<Category> CategoryAsync(string name, CancellationToken ct) { var item = await db.Categories.SingleOrDefaultAsync(x => x.Name == name, ct); if (item is not null) return item; item = new() { Name = name }; db.Add(item); return item; }
    public async Task<Author> AuthorAsync(string name, CancellationToken ct) { var item = db.Authors.Local.FirstOrDefault(x => x.Name == name) ?? await db.Authors.SingleOrDefaultAsync(x => x.Name == name, ct); if (item is not null) return item; item = new() { Name = name }; db.Add(item); return item; }
    public Task<bool> IsbnExistsAsync(string isbn, int? exceptId, CancellationToken ct) => db.Books.AnyAsync(x => x.Isbn == isbn && (!exceptId.HasValue || x.Id != exceptId), ct);
    public Task<bool> EmailExistsAsync(string email, int? exceptId, CancellationToken ct) => db.Members.AnyAsync(x => x.Email == email && (!exceptId.HasValue || x.Id != exceptId), ct);
    public Task<bool> BarcodeExistsAsync(string barcode, CancellationToken ct) => db.Copies.AnyAsync(x => x.Barcode == barcode, ct);
    public async Task NotifyAsync(string? userId, string kind, string message, string dedupeKey, DateTime now, CancellationToken ct) { if (string.IsNullOrEmpty(userId) || db.Notifications.Local.Any(x => x.DedupeKey == dedupeKey) || await db.Notifications.AnyAsync(x => x.DedupeKey == dedupeKey, ct)) return; db.Add(new Notification { UserId = userId, Kind = kind, Message = message, DedupeKey = dedupeKey, CreatedAtUtc = now }); NotifiedUsers.Add(userId); }
    public void Add<T>(T entity) where T : class => db.Add(entity);
    public async Task SaveAsync(CancellationToken ct) => await db.SaveChangesAsync(ct);
}
