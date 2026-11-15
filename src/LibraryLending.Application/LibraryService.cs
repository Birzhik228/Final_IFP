using System.ComponentModel.DataAnnotations;
using LibraryLending.Domain;
namespace LibraryLending.Application;

public sealed class LibraryService(ILibraryRepository repository, TimeProvider clock, LibraryOptions options) : ILibraryService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => options.Today(clock);
    private static Result<int> Fail(string message) => Result<int>.Failure(message);
    private static bool Owns(Actor actor, int memberId) => actor.IsLibrarian || actor.MemberId == memberId;
    public Task<PageResult<BookDto>> SearchBooksAsync(BookQuery q, CancellationToken ct = default) => repository.SearchBooksAsync(q with { Page = Math.Max(1, q.Page), PageSize = Math.Clamp(q.PageSize, 1, 50) }, ct);
    public Task<BookDetailDto?> GetBookAsync(int id, CancellationToken ct = default) => repository.GetBookAsync(id, ct);
    public Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken ct = default) => repository.GetCategoriesAsync(ct);
    public Task<IReadOnlyList<MemberDto>> GetMembersAsync(Actor actor, CancellationToken ct = default) => actor.IsLibrarian ? repository.GetMembersAsync(ct) : Task.FromResult<IReadOnlyList<MemberDto>>([]);
    public Task<IReadOnlyList<LoanDto>> GetLoansAsync(Actor actor, bool overdueOnly = false, string search = "", string sort = "due", CancellationToken ct = default) => repository.GetLoansAsync(actor.IsLibrarian ? null : actor.MemberId ?? -1, overdueOnly, search, sort, ct);
    public Task<IReadOnlyList<ReservationDto>> GetReservationsAsync(Actor actor, CancellationToken ct = default) => repository.GetReservationsAsync(actor.IsLibrarian ? null : actor.MemberId ?? -1, ct);
    public Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(Actor actor, CancellationToken ct = default) => repository.GetNotificationsAsync(actor.UserId, ct);
    public Task<DashboardDto> GetDashboardAsync(Actor actor, CancellationToken ct = default) => repository.GetDashboardAsync(actor.IsLibrarian ? null : actor.MemberId ?? -1, ct);
    public Task MarkNotificationsReadAsync(Actor actor, CancellationToken ct = default) => repository.MarkNotificationsReadAsync(actor.UserId, ct);

    public async Task<Result<int>> SaveBookAsync(Actor actor, int? id, BookInput input, CancellationToken ct = default)
    {
        if (!actor.IsLibrarian) return Fail("Only librarians can manage books.");
        input = input with
        {
            Title = input.Title?.Trim() ?? "", Authors = input.Authors?.Trim() ?? "",
            Isbn = input.Isbn?.Trim() ?? "", Category = input.Category?.Trim() ?? "",
            Description = input.Description?.Trim() ?? "", CoverUrl = input.CoverUrl?.Trim() ?? ""
        };
        var isbn = input.Isbn.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Authors) || string.IsNullOrWhiteSpace(input.Category)) return Fail("Title, author and category are required.");
        if (input.Title.Length > 200 || input.Authors.Length > 500 || input.Description.Length > 4000 || input.Category.Length > 80 || input.CoverUrl.Length > 2048) return Fail("One or more fields exceed the allowed length.");
        if (!(isbn.Length == 13 && isbn.All(char.IsDigit) || isbn.Length == 10 && isbn[..9].All(char.IsDigit) && (char.IsDigit(isbn[9]) || isbn[9] == 'X'))) return Fail("ISBN must contain 10 or 13 valid characters.");
        if (input.Year < 1 || input.Year > Today.Year + 1 || input.Copies < 1 || input.Copies > 50) return Fail("Enter a valid publication year and 1–50 initial copies.");
        if (!string.IsNullOrWhiteSpace(input.CoverUrl) && (!Uri.TryCreate(input.CoverUrl, UriKind.Absolute, out var url) || (url.Scheme != "https" && url.Scheme != "http"))) return Fail("Cover URL must use HTTP or HTTPS.");
        return await repository.ExecuteAsync<int>(async s =>
        {
            if (await s.IsbnExistsAsync(isbn, id, ct)) return Fail("That ISBN already belongs to a book.");
            var book = id.HasValue ? await s.BookAsync(id.Value, ct) : new Book();
            if (book is null) return Fail("Book was not found.");
            book.Title = input.Title.Trim(); book.Isbn = isbn; book.Category = await s.CategoryAsync(input.Category.Trim(), ct); book.Year = input.Year; book.Description = input.Description.Trim(); book.CoverUrl = input.CoverUrl.Trim();
            var authorNames = input.Authors.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (authorNames.Count == 0) return Fail("At least one author is required.");
            foreach (var old in book.BookAuthors.Where(a => !authorNames.Contains(a.Author.Name, StringComparer.OrdinalIgnoreCase)).ToList()) book.BookAuthors.Remove(old);
            var order = 0;
            foreach (var name in authorNames)
            {
                var existing = book.BookAuthors.FirstOrDefault(a => a.Author.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing is not null) existing.DisplayOrder = order++;
                else book.BookAuthors.Add(new BookAuthor { Book = book, Author = await s.AuthorAsync(name, ct), DisplayOrder = order++ });
            }
            if (!id.HasValue) { s.Add(book); for (var i = 0; i < input.Copies; i++) book.Copies.Add(new BookCopy { Book = book, Barcode = $"LIB-{Guid.NewGuid():N}"[..16].ToUpperInvariant(), Status = CopyStatus.Available }); }
            await s.SaveAsync(ct); return Result<int>.Success(book.Id);
        }, ct);
    }

    public async Task<Result<int>> AddCopyAsync(Actor actor, int bookId, string barcode, CancellationToken ct = default)
    {
        if (!actor.IsLibrarian) return Fail("Only librarians can add copies.");
        barcode = barcode?.Trim().ToUpperInvariant() ?? "";
        if (barcode.Length < 3 || barcode.Length > 64) return Fail("Barcode must have 3–64 characters.");
        return await repository.ExecuteAsync<int>(async s =>
        {
            var book = await s.BookAsync(bookId, ct); if (book is null) return Fail("Book was not found.");
            if (await s.BarcodeExistsAsync(barcode, ct)) return Fail("This barcode already exists.");
            var copy = new BookCopy { Book = book, Barcode = barcode, Status = CopyStatus.Available }; s.Add(copy); await s.SaveAsync(ct);
            await AllocateAsync(s, copy, ct); return Result<int>.Success(copy.Id);
        }, ct);
    }

    public async Task<Result<int>> SaveMemberAsync(Actor actor, int? id, MemberInput input, CancellationToken ct = default)
    {
        if (!actor.IsLibrarian) return Fail("Only librarians can manage members.");
        input = input with { Name = input.Name?.Trim() ?? "", Email = input.Email?.Trim() ?? "", Phone = input.Phone?.Trim() ?? "" };
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 150 || string.IsNullOrWhiteSpace(input.Email) || !new EmailAddressAttribute().IsValid(input.Email) || input.Email.Length > 254 || input.Phone.Length > 40 || input.BorrowingLimit < 1 || input.BorrowingLimit > 50 || !Enum.IsDefined(input.Status)) return Fail("Enter a name, valid email and borrowing limit between 1 and 50.");
        return await repository.ExecuteAsync<int>(async s =>
        {
            var email = input.Email.Trim().ToLowerInvariant(); if (await s.EmailExistsAsync(email, id, ct)) return Fail("A member already uses this email.");
            var member = id.HasValue ? await s.MemberAsync(id.Value, ct) : new Member { JoinedAtUtc = Now }; if (member is null) return Fail("Member was not found.");
            member.Name = input.Name.Trim(); member.Email = email; member.Phone = input.Phone.Trim(); member.Status = input.Status; member.BorrowingLimit = input.BorrowingLimit; member.Version = Guid.NewGuid().ToString();
            if (!id.HasValue) s.Add(member); await s.SaveAsync(ct);
            if (member.Status != MembershipStatus.Active) foreach (var r in (await s.ActiveReservationsAsync(null, ct)).Where(r => r.MemberId == member.Id)) { r.Status = ReservationStatus.Cancelled; r.ClosureReason = "Membership is inactive."; await s.SaveAsync(ct); if (r.AssignedCopyId is int cid) { var copy = await s.CopyAsync(cid, ct); if (copy is not null) { copy.Status = CopyStatus.Available; await AllocateAsync(s, copy, ct); } } }
            return Result<int>.Success(member.Id);
        }, ct);
    }

    public async Task<Result<int>> BorrowAsync(Actor actor, int copyId, int memberId, CancellationToken ct = default)
    {
        if (!actor.IsLibrarian) return Fail("A librarian must check out physical books.");
        return await repository.ExecuteAsync<int>(async s =>
        {
            var copy = await s.CopyAsync(copyId, ct); var member = await s.MemberAsync(memberId, ct);
            if (copy is null || member is null) return Fail("Copy or member was not found.");
            if (copy.Status != CopyStatus.Available) return Fail("This copy is no longer available.");
            if ((await s.ActiveReservationsAsync(copy.BookId, ct)).Any(r => r.Status == ReservationStatus.Waiting)) return Fail("This book has a reservation queue. Allocate its copies before checkout.");
            var eligible = await EligibleAsync(s, member, ct); if (!eligible.IsSuccess) return Fail(eligible.Error);
            return await CreateLoanAsync(s, copy, member, ct);
        }, ct);
    }

    public async Task<Result<int>> ReturnAsync(Actor actor, int loanId, CancellationToken ct = default)
    {
        if (!actor.IsLibrarian) return Fail("Only librarians can accept returns.");
        return await repository.ExecuteAsync<int>(async s =>
        {
            var loan = await s.LoanAsync(loanId, ct); if (loan is null) return Fail("Loan was not found.");
            if (loan.ReturnedAtUtc.HasValue) return Fail("This loan has already been returned.");
            loan.ReturnedAtUtc = Now; loan.FinalFineMinor = LendingRules.FineMinor(loan.DueDate, Today, loan.DailyFineMinor);
            var copy = await s.CopyAsync(loan.BookCopyId, ct); copy!.Status = CopyStatus.Available; copy.Version = Guid.NewGuid().ToString();
            var member = await s.MemberAsync(loan.MemberId, ct); member!.Version = Guid.NewGuid().ToString();
            await AllocateAsync(s, copy, ct); return Result<int>.Success(loan.Id);
        }, ct);
    }

    public async Task<Result<int>> ReserveAsync(Actor actor, int bookId, int memberId, CancellationToken ct = default)
    {
        if (!Owns(actor, memberId)) return Fail("You can only reserve books for your own account.");
        return await repository.ExecuteAsync<int>(async s =>
        {
            var member = await s.MemberAsync(memberId, ct); var book = await s.BookAsync(bookId, ct);
            if (member is null || book is null) return Fail("Book or member was not found.");
            if (member.Status != MembershipStatus.Active) return Fail("Only active members can reserve books.");
            if ((await s.ActiveReservationsAsync(bookId, ct)).Any(r => r.MemberId == memberId)) return Fail("This member already has an active reservation for this book.");
            if ((await s.AvailableCopiesAsync(bookId, ct)).Count > 0) return Fail("A copy is available. Ask a librarian to check it out.");
            var r = new Reservation { BookId = bookId, MemberId = memberId, Status = ReservationStatus.Waiting, CreatedAtUtc = Now }; s.Add(r); await s.SaveAsync(ct); return Result<int>.Success(r.Id);
        }, ct);
    }

    public async Task<Result<int>> PickupAsync(Actor actor, int reservationId, CancellationToken ct = default)
    {
        if (!actor.IsLibrarian) return Fail("A librarian must confirm pickup.");
        return await repository.ExecuteAsync<int>(async s =>
        {
            var r = await s.ReservationAsync(reservationId, ct);
            if (r is null || r.Status != ReservationStatus.Ready || r.AssignedCopyId is null) return Fail("This reservation is not ready for pickup.");
            if (r.PickupDeadlineUtc <= Now) return Fail("Pickup deadline has expired. Refresh after the next deadline check.");
            var member = await s.MemberAsync(r.MemberId, ct); var copy = await s.CopyAsync(r.AssignedCopyId.Value, ct);
            if (copy?.Status != CopyStatus.Reserved || member is null) return Fail("The reserved copy is no longer available.");
            var eligible = await EligibleAsync(s, member, ct); if (!eligible.IsSuccess) return Fail(eligible.Error);
            r.Status = ReservationStatus.Fulfilled; r.Version = Guid.NewGuid().ToString();
            var loan = await CreateLoanAsync(s, copy, member, ct); if (loan.IsSuccess) r.FulfilledLoanId = loan.Value; return loan;
        }, ct);
    }

    public async Task<Result<int>> CancelReservationAsync(Actor actor, int reservationId, CancellationToken ct = default)
    {
        return await repository.ExecuteAsync<int>(async s =>
        {
            var r = await s.ReservationAsync(reservationId, ct); if (r is null || !Owns(actor, r.MemberId)) return Fail("Reservation was not found or access was denied.");
            if (r.Status is not (ReservationStatus.Ready or ReservationStatus.Waiting)) return Fail("Only active reservations can be cancelled.");
            r.Status = ReservationStatus.Cancelled; r.ClosureReason = "Cancelled by user."; r.Version = Guid.NewGuid().ToString();
            await s.SaveAsync(ct);
            if (r.AssignedCopyId is int cid) { var copy = await s.CopyAsync(cid, ct); if (copy is not null) { copy.Status = CopyStatus.Available; await AllocateAsync(s, copy, ct); } }
            return Result<int>.Success(r.Id);
        }, ct);
    }

    public async Task<Result<int>> MarkLostAsync(Actor actor, int copyId, CancellationToken ct = default)
    {
        if (!actor.IsLibrarian) return Fail("Only librarians can mark a copy lost.");
        return await repository.ExecuteAsync<int>(async s =>
        {
            var copy = await s.CopyAsync(copyId, ct); if (copy is null) return Fail("Copy was not found.");
            if (copy.Status == CopyStatus.Reserved) return Fail("Cancel or reassign the active reservation before marking this copy lost.");
            copy.Status = CopyStatus.Lost; copy.Version = Guid.NewGuid().ToString(); return Result<int>.Success(copy.Id);
        }, ct);
    }

    public async Task ProcessDeadlinesAsync(CancellationToken ct = default)
    {
        var result = await repository.ExecuteAsync<int>(async s =>
        {
            var active = await s.ActiveReservationsAsync(null, ct);
            foreach (var r in active.Where(r => r.Status == ReservationStatus.Ready && r.PickupDeadlineUtc <= Now))
            {
                r.Status = ReservationStatus.Expired; r.ClosureReason = "Pickup deadline elapsed."; r.Version = Guid.NewGuid().ToString();
                await s.NotifyAsync(r.Member.IdentityUserId, "reservation-expired", $"Your pickup window for {r.Book.Title} has expired.", $"expired:{r.Id}", Now, ct);
                await s.SaveAsync(ct);
                if (r.AssignedCopyId is int cid) { var copy = await s.CopyAsync(cid, ct); if (copy is not null && copy.Status == CopyStatus.Reserved) { copy.Status = CopyStatus.Available; await AllocateAsync(s, copy, ct); } }
            }
            foreach (var bookId in active.Where(r => r.Status == ReservationStatus.Waiting).Select(r => r.BookId).Distinct()) foreach (var copy in await s.AvailableCopiesAsync(bookId, ct)) await AllocateAsync(s, copy, ct);
            foreach (var loan in await s.OpenLoansAsync(null, ct))
            {
                var late = LendingRules.DaysOverdue(loan.DueDate, Today); var soon = loan.DueDate.DayNumber - Today.DayNumber;
                if (late > 0 || soon is >= 0 and <= 2)
                {
                    var kind = late > 0 ? "overdue" : "due-soon";
                    var message = late > 0 ? $"{loan.BookCopy.Book.Title} is {late} day(s) overdue." : $"{loan.BookCopy.Book.Title} is due on {loan.DueDate:dd MMM yyyy}.";
                    await s.NotifyAsync(loan.Member.IdentityUserId, kind, message, $"{kind}:{loan.Id}:{Today:yyyy-MM-dd}", Now, ct);
                }
            }
            return Result<int>.Success(0);
        }, ct);
        // The worker must observe failed persistence and log/retry it instead of silently
        // claiming that expired holds and overdue notifications have been processed.
        if (!result.IsSuccess) throw new InvalidOperationException($"Deadline processing failed: {result.Error}");
    }

    private async Task<Result<bool>> EligibleAsync(ILibrarySession s, Member member, CancellationToken ct)
    {
        var loans = await s.OpenLoansAsync(member.Id, ct);
        return LendingRules.CanBorrow(member.Status, member.BorrowingLimit, loans.Count, loans.Any(l => l.DueDate < Today));
    }
    private async Task<Result<int>> CreateLoanAsync(ILibrarySession s, BookCopy copy, Member member, CancellationToken ct)
    {
        copy.Status = CopyStatus.Borrowed; copy.Version = Guid.NewGuid().ToString(); member.Version = Guid.NewGuid().ToString();
        var loan = new Loan { BookCopyId = copy.Id, MemberId = member.Id, BorrowedAtUtc = Now, DueDate = Today.AddDays(options.LoanDays), DailyFineMinor = options.DailyFineMinor }; s.Add(loan); await s.SaveAsync(ct); return Result<int>.Success(loan.Id);
    }
    private async Task AllocateAsync(ILibrarySession s, BookCopy copy, CancellationToken ct)
    {
        var queue = await s.ActiveReservationsAsync(copy.BookId, ct);
        foreach (var r in queue.Where(r => r.Status == ReservationStatus.Waiting && r.Member.Status != MembershipStatus.Active)) { r.Status = ReservationStatus.Cancelled; r.ClosureReason = "Membership is inactive."; }
        var next = LendingRules.NextInQueue(queue);
        copy.Version = Guid.NewGuid().ToString();
        if (next is null) { copy.Status = CopyStatus.Available; return; }
        next.Status = ReservationStatus.Ready; next.AssignedCopyId = copy.Id; next.ReadyAtUtc = Now; next.PickupDeadlineUtc = Now.AddHours(options.PickupHours); next.Version = Guid.NewGuid().ToString(); copy.Status = CopyStatus.Reserved;
        await s.NotifyAsync(next.Member.IdentityUserId, "reservation-ready", $"{next.Book.Title} is ready. Collect it by {next.PickupDeadlineUtc:dd MMM HH:mm} UTC.", $"ready:{next.Id}", Now, ct);
    }
}
