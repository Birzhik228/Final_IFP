using FluentAssertions;
using LibraryLending.Application;
using LibraryLending.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LibraryLending.Tests.Integration;

public sealed class ReservationIntegrationTests : LibraryTestBase
{
    private async Task<(BookDetailDto Book, int LoanId, int First, int Second, int FirstMember, int SecondMember)> Queue()
    {
        var book = await NewBook();
        var borrower = await NewMember();
        var firstMember = await NewMember();
        var secondMember = await NewMember();
        var loan = await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, borrower));
        loan.IsSuccess.Should().BeTrue(loan.Error);
        var first = await Use(s => s.ReserveAsync(Librarian, book.Book.Id, firstMember));
        var second = await Use(s => s.ReserveAsync(Librarian, book.Book.Id, secondMember));
        first.IsSuccess.Should().BeTrue(first.Error);
        second.IsSuccess.Should().BeTrue(second.Error);
        return (book, loan.Value, first.Value, second.Value, firstMember, secondMember);
    }

    [Fact]
    public async Task Return_atomically_assigns_oldest_waiting_reservation()
    {
        var queue = await Queue();
        (await Use(s => s.ReturnAsync(Librarian, queue.LoanId))).IsSuccess.Should().BeTrue();
        var reservations = await Use(s => s.GetReservationsAsync(Librarian));
        var first = reservations.Single(x => x.Id == queue.First);
        first.Status.Should().Be(ReservationStatus.Ready);
        first.AssignedCopyId.Should().Be(queue.Book.Copies.Single().Id);
        first.PickupDeadlineUtc.Should().Be(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(72));
        reservations.Single(x => x.Id == queue.Second).Status.Should().Be(ReservationStatus.Waiting);
        (await Use(s => s.GetBookAsync(queue.Book.Book.Id)))!.Copies.Single().Status.Should().Be(CopyStatus.Reserved);
        (await Use(s => s.BorrowAsync(Librarian, queue.Book.Copies.Single().Id, queue.SecondMember))).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_waiting_and_ready_reservations_are_rejected()
    {
        var queue = await Queue();
        (await Use(s => s.ReserveAsync(Librarian, queue.Book.Book.Id, queue.FirstMember))).IsSuccess.Should().BeFalse();
        (await Use(s => s.ReturnAsync(Librarian, queue.LoanId))).IsSuccess.Should().BeTrue();
        (await Use(s => s.ReserveAsync(Librarian, queue.Book.Book.Id, queue.FirstMember))).IsSuccess.Should().BeFalse();
        (await Database(db => db.Reservations.CountAsync(x => x.BookId == queue.Book.Book.Id))).Should().Be(2);
    }

    [Fact]
    public async Task Available_copy_cannot_be_reserved()
    {
        var book = await NewBook();
        var result = await Use(s => s.ReserveAsync(MemberActor, book.Book.Id, EligibleMemberId));
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("available");
    }

    [Fact]
    public async Task Expiry_at_exact_deadline_passes_copy_to_next_member()
    {
        var queue = await Queue();
        (await Use(s => s.ReturnAsync(Librarian, queue.LoanId))).IsSuccess.Should().BeTrue();
        Factory.Clock.Advance(TimeSpan.FromHours(72));
        (await Use(s => s.PickupAsync(Librarian, queue.First))).IsSuccess.Should().BeFalse();
        await Use(s => s.ProcessDeadlinesAsync());
        var reservations = await Use(s => s.GetReservationsAsync(Librarian));
        reservations.Single(x => x.Id == queue.First).Status.Should().Be(ReservationStatus.Expired);
        var second = reservations.Single(x => x.Id == queue.Second);
        second.Status.Should().Be(ReservationStatus.Ready);
        second.PickupDeadlineUtc.Should().Be(Factory.Clock.GetUtcNow().UtcDateTime.AddHours(72));
        (await Use(s => s.GetBookAsync(queue.Book.Book.Id)))!.Copies.Single().Status.Should().Be(CopyStatus.Reserved);
    }

    [Fact]
    public async Task Cancelling_ready_reservation_immediately_assigns_next_member()
    {
        var queue = await Queue();
        (await Use(s => s.ReturnAsync(Librarian, queue.LoanId))).IsSuccess.Should().BeTrue();
        var owner = new Actor("test-member", false, queue.FirstMember);
        (await Use(s => s.CancelReservationAsync(owner, queue.First))).IsSuccess.Should().BeTrue();
        var reservations = await Use(s => s.GetReservationsAsync(Librarian));
        reservations.Single(x => x.Id == queue.First).Status.Should().Be(ReservationStatus.Cancelled);
        reservations.Single(x => x.Id == queue.Second).Status.Should().Be(ReservationStatus.Ready);
    }

    [Fact]
    public async Task Pickup_creates_loan_for_assigned_member_and_preserves_history()
    {
        var queue = await Queue();
        (await Use(s => s.ReturnAsync(Librarian, queue.LoanId))).IsSuccess.Should().BeTrue();
        (await Use(s => s.PickupAsync(MemberActor, queue.First))).IsSuccess.Should().BeFalse();
        var pickup = await Use(s => s.PickupAsync(Librarian, queue.First));
        pickup.IsSuccess.Should().BeTrue(pickup.Error);
        var loan = (await Use(s => s.GetLoansAsync(Librarian))).Single(x => x.Id == pickup.Value);
        loan.MemberId.Should().Be(queue.FirstMember);
        loan.CopyId.Should().Be(queue.Book.Copies[0].Id);
        var reservation = await Database(db => db.Reservations.SingleAsync(x => x.Id == queue.First));
        reservation.Status.Should().Be(ReservationStatus.Fulfilled);
        reservation.FulfilledLoanId.Should().Be(pickup.Value);
        reservation.AssignedCopyId.Should().Be(queue.Book.Copies[0].Id);
        (await Use(s => s.PickupAsync(Librarian, queue.First))).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Reaching_limit_does_not_lose_fifo_hold_but_blocks_pickup()
    {
        var queue = await Queue();
        var member = (await Use(s => s.GetMembersAsync(Librarian))).Single(x => x.Id == queue.FirstMember);
        (await Use(s => s.SaveMemberAsync(Librarian, member.Id, new MemberInput(member.Name, member.Email, member.Phone, member.Status, 1)))).IsSuccess.Should().BeTrue();
        var another = await NewBook();
        (await Use(s => s.BorrowAsync(Librarian, another.Copies[0].Id, member.Id))).IsSuccess.Should().BeTrue();
        (await Use(s => s.ReturnAsync(Librarian, queue.LoanId))).IsSuccess.Should().BeTrue();
        var pickup = await Use(s => s.PickupAsync(Librarian, queue.First));
        pickup.IsSuccess.Should().BeFalse();
        pickup.Error.Should().Contain("limit");
        (await Use(s => s.GetReservationsAsync(Librarian))).Single(x => x.Id == queue.First).Status.Should().Be(ReservationStatus.Ready);
    }

    [Fact]
    public async Task Suspending_a_member_cancels_their_hold_and_passes_copy_on()
    {
        var queue = await Queue();
        (await Use(s => s.ReturnAsync(Librarian, queue.LoanId))).IsSuccess.Should().BeTrue();
        var member = (await Use(s => s.GetMembersAsync(Librarian))).Single(x => x.Id == queue.FirstMember);
        var update = await Use(s => s.SaveMemberAsync(Librarian, member.Id,
            new MemberInput(member.Name, member.Email, member.Phone, MembershipStatus.Suspended, member.BorrowingLimit)));
        update.IsSuccess.Should().BeTrue(update.Error);
        var reservations = await Use(s => s.GetReservationsAsync(Librarian));
        reservations.Single(x => x.Id == queue.First).Status.Should().Be(ReservationStatus.Cancelled);
        reservations.Single(x => x.Id == queue.Second).Status.Should().Be(ReservationStatus.Ready);
    }

    [Fact]
    public async Task Another_member_cannot_cancel_someone_elses_reservation()
    {
        var queue = await Queue();
        var other = new Actor("other", false, queue.SecondMember);
        (await Use(s => s.CancelReservationAsync(other, queue.First))).IsSuccess.Should().BeFalse();
        (await Use(s => s.GetReservationsAsync(Librarian))).Single(x => x.Id == queue.First).Status.Should().Be(ReservationStatus.Waiting);
    }

    [Fact]
    public async Task Ready_notification_is_persisted_once_and_only_for_its_recipient()
    {
        var book = await NewBook();
        var borrower = await NewMember();
        var borrowed = await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, borrower));
        (await Use(s => s.ReserveAsync(MemberActor, book.Book.Id, EligibleMemberId))).IsSuccess.Should().BeTrue();
        (await Use(s => s.ReturnAsync(Librarian, borrowed.Value))).IsSuccess.Should().BeTrue();
        await Use(s => s.ProcessDeadlinesAsync());
        await Use(s => s.ProcessDeadlinesAsync());
        var messages = (await Use(s => s.GetNotificationsAsync(MemberActor))).Where(x => x.Kind == "reservation-ready" && x.Message.Contains(book.Book.Title));
        messages.Should().ContainSingle();
        (await Use(s => s.GetNotificationsAsync(Librarian))).Should().NotContain(x => x.Message.Contains(book.Book.Title));
        await Use(s => s.MarkNotificationsReadAsync(MemberActor));
        (await Use(s => s.GetNotificationsAsync(MemberActor))).Should().OnlyContain(x => x.IsRead);
    }

    [Fact]
    public async Task Repeated_deadline_sweeps_do_not_duplicate_daily_overdue_notifications()
    {
        var book = await NewBook();
        (await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, EligibleMemberId))).IsSuccess.Should().BeTrue();
        Factory.Clock.Advance(TimeSpan.FromDays(15));
        await Use(s => s.ProcessDeadlinesAsync());
        await Use(s => s.ProcessDeadlinesAsync());
        (await Use(s => s.GetNotificationsAsync(MemberActor))).Count(x => x.Kind == "overdue" && x.Message.Contains(book.Book.Title)).Should().Be(1);
    }

    [Fact]
    public async Task Database_rejects_duplicate_active_reservations()
    {
        var queue = await Queue();
        Func<Task> duplicate = () => Database(async db =>
        {
            db.Reservations.Add(new Reservation
            {
                BookId = queue.Book.Book.Id,
                MemberId = queue.FirstMember,
                CreatedAtUtc = Factory.Clock.GetUtcNow().UtcDateTime,
                Status = ReservationStatus.Waiting
            });
            return await db.SaveChangesAsync();
        });
        await duplicate.Should().ThrowAsync<DbUpdateException>();
    }
}
