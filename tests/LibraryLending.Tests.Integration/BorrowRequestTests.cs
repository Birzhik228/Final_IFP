using FluentAssertions;
using LibraryLending.Application;
using LibraryLending.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibraryLending.Tests.Integration;

public sealed class BorrowRequestTests : LibraryTestBase
{
    private Task<Actor> OtherMemberActor() => Database(async db =>
    {
        var member = await db.Members.FirstAsync(x => x.IdentityUserId != null && x.Id != EligibleMemberId
            && x.Status == MembershipStatus.Active);
        return new Actor(member.IdentityUserId!, false, member.Id);
    });

    private async Task SetEligibility(string state)
    {
        if (state == "overdue")
        {
            var borrowedBook = await NewBook();
            var loan = await Use(s => s.BorrowAsync(Librarian, borrowedBook.Copies[0].Id, EligibleMemberId));
            loan.IsSuccess.Should().BeTrue(loan.Error);
            await Database(async db =>
            {
                (await db.Loans.SingleAsync(x => x.Id == loan.Value)).DueDate = Factory.Options.Today(Factory.Clock).AddDays(-1);
                return await db.SaveChangesAsync();
            });
        }
        else if (state == "limit")
        {
            var member = (await Use(s => s.GetMembersAsync(Librarian))).Single(x => x.Id == EligibleMemberId);
            (await Use(s => s.SaveMemberAsync(Librarian, member.Id,
                new MemberInput(member.Name, member.Email, member.Phone, member.Status, 1)))).IsSuccess.Should().BeTrue();
            var borrowedBook = await NewBook();
            (await Use(s => s.BorrowAsync(Librarian, borrowedBook.Copies[0].Id, EligibleMemberId))).IsSuccess.Should().BeTrue();
        }
        else
        {
            var member = (await Use(s => s.GetMembersAsync(Librarian))).Single(x => x.Id == EligibleMemberId);
            (await Use(s => s.SaveMemberAsync(Librarian, member.Id,
                new MemberInput(member.Name, member.Email, member.Phone, MembershipStatus.Suspended, member.BorrowingLimit)))).IsSuccess.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Request_does_not_hold_a_copy_and_acceptance_creates_a_notified_audited_loan()
    {
        var book = await NewBook();
        var request = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        request.IsSuccess.Should().BeTrue(request.Error);
        var pending = (await Use(s => s.GetBorrowRequestsAsync(MemberActor))).Single(x => x.Id == request.Value);
        pending.Status.Should().Be(BorrowRequestStatus.Pending);
        pending.LoanId.Should().BeNull();
        (await Use(s => s.GetBookAsync(book.Book.Id)))!.Copies.Single().Status.Should().Be(CopyStatus.Available);
        (await Database(db => db.Loans.CountAsync(x => x.BookCopyId == book.Copies[0].Id))).Should().Be(0);
        (await Use(s => s.GetNotificationsAsync(Librarian))).Should().Contain(x => x.Message.Contains(book.Book.Title));

        Factory.Clock.Advance(TimeSpan.FromHours(1));
        var accepted = await Use(s => s.AcceptBorrowRequestAsync(Librarian, request.Value));
        accepted.IsSuccess.Should().BeTrue(accepted.Error);
        var final = (await Use(s => s.GetBorrowRequestsAsync(MemberActor))).Single(x => x.Id == request.Value);
        final.Status.Should().Be(BorrowRequestStatus.Accepted);
        final.LoanId.Should().Be(accepted.Value);
        final.Barcode.Should().Be(book.Copies[0].Barcode);
        final.DueDate.Should().Be(Factory.Options.Today(Factory.Clock).AddDays(Factory.Options.LoanDays));
        final.ReviewedAtUtc.Should().Be(Factory.Clock.GetUtcNow().UtcDateTime);
        var stored = await Database(db => db.BorrowRequests.AsNoTracking().SingleAsync(x => x.Id == request.Value));
        stored.ReviewerUserId.Should().Be(Librarian.UserId);
        (await Use(s => s.GetLoansAsync(MemberActor))).Should().Contain(x => x.Id == accepted.Value && x.MemberId == EligibleMemberId);
        (await Use(s => s.GetBookAsync(book.Book.Id)))!.Copies.Single().Status.Should().Be(CopyStatus.Borrowed);
        (await Use(s => s.GetNotificationsAsync(MemberActor))).Should().Contain(x => x.Message.Contains(book.Book.Title));

        var notificationCount = await Database(db => db.Notifications.CountAsync(x => x.UserId == MemberActor.UserId));
        (await Use(s => s.AcceptBorrowRequestAsync(Librarian, request.Value))).IsSuccess.Should().BeFalse();
        (await Use(s => s.RejectBorrowRequestAsync(Librarian, request.Value, "Too late"))).IsSuccess.Should().BeFalse();
        (await Database(db => db.Notifications.CountAsync(x => x.UserId == MemberActor.UserId))).Should().Be(notificationCount);
        (await Database(db => db.Loans.CountAsync(x => x.BookCopyId == book.Copies[0].Id))).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_duplicate_submissions_create_one_pending_request()
    {
        var book = await NewBook();
        var results = await Task.WhenAll(
            Task.Run(() => Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id))),
            Task.Run(() => Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id))));
        results.Count(x => x.IsSuccess).Should().Be(1);
        (await Database(db => db.BorrowRequests.CountAsync(x => x.BookId == book.Book.Id && x.MemberId == EligibleMemberId))).Should().Be(1);
    }

    [Fact]
    public async Task Requests_are_scoped_to_the_authenticated_member_and_only_librarians_can_review()
    {
        var book = await NewBook(2);
        var other = await OtherMemberActor();
        var first = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        var second = await Use(s => s.RequestBorrowAsync(other, book.Book.Id));
        first.IsSuccess.Should().BeTrue(first.Error);
        second.IsSuccess.Should().BeTrue(second.Error);
        (await Use(s => s.GetBorrowRequestsAsync(MemberActor))).Should().ContainSingle(x => x.Id == first.Value)
            .And.OnlyContain(x => x.MemberId == MemberActor.MemberId);
        (await Use(s => s.GetBorrowRequestsAsync(other))).Should().ContainSingle(x => x.Id == second.Value)
            .And.OnlyContain(x => x.MemberId == other.MemberId);
        (await Use(s => s.GetBorrowRequestsAsync(Librarian))).Should().HaveCount(2);
        (await Use(s => s.AcceptBorrowRequestAsync(MemberActor, first.Value))).IsSuccess.Should().BeFalse();
        (await Use(s => s.RejectBorrowRequestAsync(other, first.Value, "Unauthorized"))).IsSuccess.Should().BeFalse();
        (await Use(s => s.CancelBorrowRequestAsync(other, first.Value))).IsSuccess.Should().BeFalse();

        var forged = MemberActor with { MemberId = other.MemberId };
        var anonymous = new Actor("", false, null);
        (await Use(s => s.GetBorrowRequestsAsync(forged))).Should().BeEmpty();
        (await Use(s => s.GetBorrowRequestsAsync(anonymous))).Should().BeEmpty();
        (await Use(s => s.RequestBorrowAsync(forged, book.Book.Id))).IsSuccess.Should().BeFalse();
        (await Use(s => s.RequestBorrowAsync(anonymous, book.Book.Id))).IsSuccess.Should().BeFalse();
        (await Use(s => s.RequestBorrowAsync(Librarian, book.Book.Id))).IsSuccess.Should().BeFalse();
        (await Database(db => db.BorrowRequests.CountAsync(x => x.Status == BorrowRequestStatus.Pending))).Should().Be(2);
    }

    [Fact]
    public async Task Rejection_records_reason_and_notifies_member_without_lending_or_holding_the_copy()
    {
        var book = await NewBook();
        var request = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        request.IsSuccess.Should().BeTrue();
        (await Use(s => s.RejectBorrowRequestAsync(Librarian, request.Value, new string('x', 501)))).IsSuccess.Should().BeFalse();
        (await Use(s => s.GetBorrowRequestsAsync(MemberActor))).Single().Status.Should().Be(BorrowRequestStatus.Pending);
        var rejected = await Use(s => s.RejectBorrowRequestAsync(Librarian, request.Value, "  Please visit the circulation desk.  "));
        rejected.IsSuccess.Should().BeTrue(rejected.Error);
        var row = (await Use(s => s.GetBorrowRequestsAsync(MemberActor))).Single();
        row.Status.Should().Be(BorrowRequestStatus.Rejected);
        row.ReviewNote.Should().Be("Please visit the circulation desk.");
        row.LoanId.Should().BeNull();
        row.ReviewedAtUtc.Should().NotBeNull();
        (await Use(s => s.GetNotificationsAsync(MemberActor))).Should().Contain(x => x.Message.Contains(book.Book.Title));
        (await Use(s => s.GetBookAsync(book.Book.Id)))!.Copies.Single().Status.Should().Be(CopyStatus.Available);
        (await Database(db => db.Loans.CountAsync(x => x.BookCopyId == book.Copies[0].Id))).Should().Be(0);
        (await Use(s => s.AcceptBorrowRequestAsync(Librarian, request.Value))).IsSuccess.Should().BeFalse();
        (await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Owner_can_cancel_pending_request_and_submit_again()
    {
        var book = await NewBook();
        var request = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        request.IsSuccess.Should().BeTrue();
        (await Use(s => s.CancelBorrowRequestAsync(MemberActor, request.Value))).IsSuccess.Should().BeTrue();
        var cancelled = (await Use(s => s.GetBorrowRequestsAsync(MemberActor))).Single();
        cancelled.Status.Should().Be(BorrowRequestStatus.Cancelled);
        cancelled.LoanId.Should().BeNull();
        (await Use(s => s.AcceptBorrowRequestAsync(Librarian, request.Value))).IsSuccess.Should().BeFalse();
        (await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id))).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("overdue")]
    [InlineData("limit")]
    public async Task Ineligible_members_cannot_submit_requests(string state)
    {
        var book = await NewBook();
        await SetEligibility(state);
        var result = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        result.IsSuccess.Should().BeFalse();
        (await Database(db => db.BorrowRequests.CountAsync())).Should().Be(0);
        (await Use(s => s.GetBookAsync(book.Book.Id)))!.Copies.Single().Status.Should().Be(CopyStatus.Available);
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("overdue")]
    [InlineData("limit")]
    public async Task Acceptance_rechecks_eligibility_without_closing_a_pending_request_on_failure(string state)
    {
        var book = await NewBook();
        var request = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        request.IsSuccess.Should().BeTrue();
        await SetEligibility(state);
        var accepted = await Use(s => s.AcceptBorrowRequestAsync(Librarian, request.Value));
        accepted.IsSuccess.Should().BeFalse();
        var row = (await Use(s => s.GetBorrowRequestsAsync(MemberActor))).Single();
        row.Status.Should().Be(BorrowRequestStatus.Pending);
        row.ReviewedAtUtc.Should().BeNull();
        row.LoanId.Should().BeNull();
        (await Database(db => db.Loans.CountAsync(x => x.BookCopyId == book.Copies[0].Id))).Should().Be(0);
    }

    [Fact]
    public async Task Unavailable_or_archived_titles_cannot_receive_a_request()
    {
        var book = await NewBook();
        var borrower = await NewMember();
        (await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, borrower))).IsSuccess.Should().BeTrue();
        (await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id))).IsSuccess.Should().BeFalse();
        var archived = await NewBook();
        await Database(async db => { (await db.Books.SingleAsync(x => x.Id == archived.Book.Id)).IsArchived = true; return await db.SaveChangesAsync(); });
        (await Use(s => s.RequestBorrowAsync(MemberActor, archived.Book.Id))).IsSuccess.Should().BeFalse();
        (await Use(s => s.RequestBorrowAsync(MemberActor, int.MaxValue))).IsSuccess.Should().BeFalse();
        (await Database(db => db.BorrowRequests.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task A_copy_taken_after_submission_leaves_request_pending_until_acceptance_can_succeed()
    {
        var book = await NewBook();
        var request = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        request.IsSuccess.Should().BeTrue();
        var otherMember = await NewMember();
        var otherLoan = await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, otherMember));
        otherLoan.IsSuccess.Should().BeTrue();
        (await Use(s => s.AcceptBorrowRequestAsync(Librarian, request.Value))).IsSuccess.Should().BeFalse();
        (await Use(s => s.GetBorrowRequestsAsync(MemberActor))).Single().Status.Should().Be(BorrowRequestStatus.Pending);
        (await Use(s => s.ReturnAsync(Librarian, otherLoan.Value))).IsSuccess.Should().BeTrue();
        (await Use(s => s.AcceptBorrowRequestAsync(Librarian, request.Value))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Pending_request_must_be_cancelled_before_joining_the_unavailable_titles_queue()
    {
        var book = await NewBook();
        var request = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        request.IsSuccess.Should().BeTrue();
        var otherMember = await NewMember();
        (await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, otherMember))).IsSuccess.Should().BeTrue();
        (await Use(s => s.ReserveAsync(MemberActor, book.Book.Id, EligibleMemberId))).IsSuccess.Should().BeFalse();
        (await Use(s => s.CancelBorrowRequestAsync(MemberActor, request.Value))).IsSuccess.Should().BeTrue();
        (await Use(s => s.ReserveAsync(MemberActor, book.Book.Id, EligibleMemberId))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Accepting_a_request_cannot_bypass_a_waiting_reservation_when_a_copy_is_available()
    {
        var book = await NewBook();
        var request = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        request.IsSuccess.Should().BeTrue();
        var waitingMember = await NewMember();
        // Model a pre-sweep state: a free copy exists while an older reservation awaits allocation.
        await Database(async db =>
        {
            db.Reservations.Add(new Reservation { BookId = book.Book.Id, MemberId = waitingMember,
                Status = ReservationStatus.Waiting, CreatedAtUtc = Factory.Clock.GetUtcNow().UtcDateTime.AddHours(-1) });
            return await db.SaveChangesAsync();
        });
        (await Use(s => s.AcceptBorrowRequestAsync(Librarian, request.Value))).IsSuccess.Should().BeFalse();
        (await Database(db => db.Loans.CountAsync(x => x.BookCopyId == book.Copies[0].Id))).Should().Be(0);
        (await Use(s => s.GetBorrowRequestsAsync(MemberActor))).Single().Status.Should().Be(BorrowRequestStatus.Pending);
        await Use(s => s.ProcessDeadlinesAsync());
        (await Use(s => s.GetReservationsAsync(Librarian))).Should().Contain(x => x.BookId == book.Book.Id && x.Status == ReservationStatus.Ready);
    }

    [Theory]
    [InlineData(ReservationStatus.Waiting)]
    [InlineData(ReservationStatus.Ready)]
    public async Task Active_reservation_prevents_a_parallel_borrow_request_for_the_same_title(ReservationStatus status)
    {
        var book = await NewBook(2);
        await Database(async db =>
        {
            db.Reservations.Add(new Reservation { BookId = book.Book.Id, MemberId = EligibleMemberId,
                Status = status, CreatedAtUtc = Factory.Clock.GetUtcNow().UtcDateTime,
                AssignedCopyId = status == ReservationStatus.Ready ? book.Copies[0].Id : null,
                PickupDeadlineUtc = status == ReservationStatus.Ready ? Factory.Clock.GetUtcNow().UtcDateTime.AddHours(72) : null });
            if (status == ReservationStatus.Ready)
                (await db.Copies.SingleAsync(x => x.Id == book.Copies[0].Id)).Status = CopyStatus.Reserved;
            return await db.SaveChangesAsync();
        });
        (await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id))).IsSuccess.Should().BeFalse();
        (await Database(db => db.BorrowRequests.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_reviews_of_one_request_create_exactly_one_loan()
    {
        var book = await NewBook(2);
        var request = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        request.IsSuccess.Should().BeTrue();
        var results = await Task.WhenAll(
            Task.Run(() => Use(s => s.AcceptBorrowRequestAsync(Librarian, request.Value))),
            Task.Run(() => Use(s => s.AcceptBorrowRequestAsync(Librarian, request.Value))));
        results.Count(x => x.IsSuccess).Should().Be(1);
        (await Database(db => db.Loans.CountAsync(x => x.BookCopy.BookId == book.Book.Id))).Should().Be(1);
        (await Use(s => s.GetBookAsync(book.Book.Id)))!.Book.AvailableCopies.Should().Be(1);
    }

    [Fact]
    public async Task Competing_requests_for_the_last_copy_only_issue_one_loan()
    {
        var book = await NewBook();
        var other = await OtherMemberActor();
        var first = await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id));
        var second = await Use(s => s.RequestBorrowAsync(other, book.Book.Id));
        first.IsSuccess.Should().BeTrue(first.Error);
        second.IsSuccess.Should().BeTrue(second.Error);
        var results = await Task.WhenAll(
            Task.Run(() => Use(s => s.AcceptBorrowRequestAsync(Librarian, first.Value))),
            Task.Run(() => Use(s => s.AcceptBorrowRequestAsync(Librarian, second.Value))));
        results.Count(x => x.IsSuccess).Should().Be(1);
        (await Database(db => db.Loans.CountAsync(x => x.BookCopyId == book.Copies[0].Id && x.ReturnedAtUtc == null))).Should().Be(1);
        var requests = await Use(s => s.GetBorrowRequestsAsync(Librarian));
        requests.Count(x => x.Status == BorrowRequestStatus.Accepted).Should().Be(1);
        requests.Count(x => x.Status == BorrowRequestStatus.Pending).Should().Be(1);
    }

    [Fact]
    public async Task Database_unique_index_rejects_duplicate_pending_requests_even_without_service_validation()
    {
        var book = await NewBook();
        (await Use(s => s.RequestBorrowAsync(MemberActor, book.Book.Id))).IsSuccess.Should().BeTrue();
        Func<Task> insertDuplicate = () => Database(async db =>
        {
            db.BorrowRequests.Add(new BorrowRequest { BookId = book.Book.Id, MemberId = EligibleMemberId,
                Status = BorrowRequestStatus.Pending, CreatedAtUtc = Factory.Clock.GetUtcNow().UtcDateTime });
            return await db.SaveChangesAsync();
        });
        await insertDuplicate.Should().ThrowAsync<DbUpdateException>();
    }
}
