using FluentAssertions;
using LibraryLending.Application;
using LibraryLending.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibraryLending.Tests.Integration;

public sealed class LendingIntegrationTests : LibraryTestBase
{
    [Fact]
    public async Task Borrow_persists_due_date_and_return_restores_availability()
    {
        var book = await NewBook();
        var borrowed = await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, EligibleMemberId));
        borrowed.IsSuccess.Should().BeTrue(borrowed.Error);
        var loan = (await Use(s => s.GetLoansAsync(Librarian))).Single(x => x.Id == borrowed.Value);
        loan.DueDate.Should().Be(new DateOnly(2026, 11, 28));
        (await Use(s => s.GetBookAsync(book.Book.Id)))!.Copies.Single().Status.Should().Be(CopyStatus.Borrowed);

        var returned = await Use(s => s.ReturnAsync(Librarian, borrowed.Value));
        returned.IsSuccess.Should().BeTrue(returned.Error);
        (await Use(s => s.GetBookAsync(book.Book.Id)))!.Copies.Single().Status.Should().Be(CopyStatus.Available);
        (await Use(s => s.GetLoansAsync(Librarian))).Single(x => x.Id == borrowed.Value).ReturnedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Repeated_return_is_rejected_and_does_not_change_recorded_return_time()
    {
        var book = await NewBook();
        var loan = await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, EligibleMemberId));
        (await Use(s => s.ReturnAsync(Librarian, loan.Value))).IsSuccess.Should().BeTrue();
        Factory.Clock.Advance(TimeSpan.FromDays(1));
        (await Use(s => s.ReturnAsync(Librarian, loan.Value))).IsSuccess.Should().BeFalse();
        var persisted = await Database(db => db.Loans.AsNoTracking().SingleAsync(x => x.Id == loan.Value));
        persisted.ReturnedAtUtc.Should().Be(new DateTime(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Return_uses_original_fine_rate_even_after_configuration_changes()
    {
        var book = await NewBook();
        var member = await NewMember();
        Factory.Options.DailyFineMinor = 125;
        var borrowed = await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, member));
        borrowed.IsSuccess.Should().BeTrue();
        Factory.Options.DailyFineMinor = 900;
        Factory.Clock.Advance(TimeSpan.FromDays(17));
        var overdue = (await Use(s => s.GetLoansAsync(Librarian, true))).Single(x => x.Id == borrowed.Value);
        overdue.DaysOverdue.Should().Be(3);
        overdue.FineMinor.Should().Be(375);
        (await Use(s => s.ReturnAsync(Librarian, borrowed.Value))).IsSuccess.Should().BeTrue();
        Factory.Clock.Advance(TimeSpan.FromDays(4));
        (await Use(s => s.GetLoansAsync(Librarian))).Single(x => x.Id == borrowed.Value).FineMinor.Should().Be(375);
    }

    [Fact]
    public async Task Overdue_member_cannot_borrow_an_available_copy()
    {
        var book = await NewBook(2);
        var member = await NewMember();
        (await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, member))).IsSuccess.Should().BeTrue();
        Factory.Clock.Advance(TimeSpan.FromDays(15));
        var result = await Use(s => s.BorrowAsync(Librarian, book.Copies[1].Id, member));
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("overdue");
        (await Use(s => s.GetBookAsync(book.Book.Id)))!.Copies.Count(x => x.Status == CopyStatus.Available).Should().Be(1);
    }

    [Fact]
    public async Task Suspended_member_cannot_borrow()
    {
        var book = await NewBook();
        var id = await NewMember();
        (await Use(s => s.SaveMemberAsync(Librarian, id, new MemberInput("Suspended Member", "suspended@example.test", "", MembershipStatus.Suspended)))).IsSuccess.Should().BeTrue();
        (await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, id))).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Members_cannot_perform_librarian_commands()
    {
        var book = await NewBook();
        (await Use(s => s.BorrowAsync(MemberActor, book.Copies[0].Id, EligibleMemberId))).IsSuccess.Should().BeFalse();
        (await Use(s => s.SaveMemberAsync(MemberActor, null, new MemberInput("Blocked", "blocked@example.test", "", MembershipStatus.Active)))).IsSuccess.Should().BeFalse();
        (await Use(s => s.AddCopyAsync(MemberActor, book.Book.Id, "UNAUTHORIZED"))).IsSuccess.Should().BeFalse();
        (await Use(s => s.MarkLostAsync(MemberActor, book.Copies[0].Id))).IsSuccess.Should().BeFalse();
        (await Use(s => s.GetMembersAsync(MemberActor))).Should().BeEmpty();
    }

    [Fact]
    public async Task Member_queries_return_only_own_loans_and_reservations()
    {
        var book = await NewBook(2);
        var other = await NewMember();
        (await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, EligibleMemberId))).IsSuccess.Should().BeTrue();
        (await Use(s => s.BorrowAsync(Librarian, book.Copies[1].Id, other))).IsSuccess.Should().BeTrue();
        var own = await Use(s => s.GetLoansAsync(MemberActor));
        own.Should().NotBeEmpty().And.OnlyContain(x => x.MemberId == EligibleMemberId);
        (await Use(s => s.ReserveAsync(MemberActor, book.Book.Id, other))).IsSuccess.Should().BeFalse();
        var reservation = await Use(s => s.ReserveAsync(MemberActor, book.Book.Id, EligibleMemberId));
        reservation.IsSuccess.Should().BeTrue(reservation.Error);
        (await Use(s => s.GetReservationsAsync(MemberActor))).Should().OnlyContain(x => x.MemberId == EligibleMemberId);
        var anonymous = new Actor("", false, null);
        (await Use(s => s.GetLoansAsync(anonymous))).Should().BeEmpty();
        (await Use(s => s.GetReservationsAsync(anonymous))).Should().BeEmpty();
    }

    [Fact]
    public async Task Two_simultaneous_borrowers_cannot_take_the_same_copy()
    {
        var book = await NewBook();
        var a = await NewMember();
        var b = await NewMember();
        var attempts = await Task.WhenAll(
            Task.Run(() => Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, a))),
            Task.Run(() => Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, b))));
        attempts.Count(x => x.IsSuccess).Should().Be(1);
        (await Database(db => db.Loans.CountAsync(x => x.BookCopyId == book.Copies[0].Id && x.ReturnedAtUtc == null))).Should().Be(1);
    }

    [Fact]
    public async Task Two_simultaneous_checkouts_cannot_exceed_one_members_limit()
    {
        var book = await NewBook(2);
        var member = await NewMember(limit: 1);
        var attempts = await Task.WhenAll(book.Copies.Select(copy => Task.Run(() => Use(s => s.BorrowAsync(Librarian, copy.Id, member)))));
        attempts.Count(x => x.IsSuccess).Should().Be(1);
        (await Database(db => db.Loans.CountAsync(x => x.MemberId == member && x.ReturnedAtUtc == null))).Should().Be(1);
    }

    [Fact]
    public async Task Database_rejects_two_open_loans_even_if_application_validation_is_bypassed()
    {
        var book = await NewBook();
        var first = await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, EligibleMemberId));
        first.IsSuccess.Should().BeTrue();
        Func<Task> duplicate = () => Database(async db =>
        {
            db.Loans.Add(new Loan
            {
                BookCopyId = book.Copies[0].Id,
                MemberId = EligibleMemberId,
                BorrowedAtUtc = Factory.Clock.GetUtcNow().UtcDateTime,
                DueDate = new DateOnly(2026, 11, 28),
                DailyFineMinor = 100
            });
            return await db.SaveChangesAsync();
        });
        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Editing_book_with_same_author_preserves_join_and_physical_copies()
    {
        var book = await NewBook(2);
        var update = await Use(s => s.SaveBookAsync(Librarian, book.Book.Id,
            new BookInput("Updated integration title", book.Book.Authors, book.Book.Isbn, book.Book.Category,
                book.Book.Year, "Revised description", "", 2)));
        update.IsSuccess.Should().BeTrue(update.Error);
        var stored = (await Use(s => s.GetBookAsync(book.Book.Id)))!;
        stored.Book.Title.Should().Be("Updated integration title");
        stored.Book.Authors.Should().Be(book.Book.Authors);
        stored.Copies.Select(x => x.Id).Should().BeEquivalentTo(book.Copies.Select(x => x.Id));
    }

    [Fact]
    public async Task Search_and_pagination_keep_stable_non_overlapping_pages()
    {
        for (var i = 0; i < 5; i++) await NewBook();
        var first = await Use(s => s.SearchBooksAsync(new BookQuery(Search: "Integration Book", PageSize: 2)));
        var second = await Use(s => s.SearchBooksAsync(new BookQuery(Search: "Integration Book", Page: 2, PageSize: 2)));
        first.TotalCount.Should().Be(5);
        first.Items.Should().HaveCount(2);
        second.Items.Should().HaveCount(2);
        first.Items.Select(x => x.Id).Should().NotIntersectWith(second.Items.Select(x => x.Id));
        var normalized = await Use(s => s.SearchBooksAsync(new BookQuery(Page: -1, PageSize: 1000)));
        normalized.Page.Should().Be(1);
        normalized.PageSize.Should().Be(50);
    }
}
