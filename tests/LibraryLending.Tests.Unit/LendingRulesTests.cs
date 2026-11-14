using FluentAssertions;
using LibraryLending.Domain;
using Xunit;

namespace LibraryLending.Tests.Unit;

public sealed class LendingRulesTests
{
    [Theory]
    [InlineData(-20, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(14, 14)]
    [InlineData(365, 365)]
    public void Overdue_days_start_on_the_day_after_the_due_date(int offset, int expected)
    {
        var due = new DateOnly(2026, 11, 14);
        LendingRules.DaysOverdue(due, due.AddDays(offset)).Should().Be(expected);
    }

    [Fact]
    public void Overdue_days_include_leap_day()
    {
        LendingRules.DaysOverdue(new DateOnly(2024, 2, 28), new DateOnly(2024, 3, 1)).Should().Be(2);
    }

    [Theory]
    [InlineData(-1, 100, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(1, 100, 100)]
    [InlineData(7, 125, 875)]
    [InlineData(30, 0, 0)]
    public void Fine_uses_whole_calendar_days_and_integer_minor_units(int offset, long rate, long expected)
    {
        var due = new DateOnly(2026, 12, 28);
        LendingRules.FineMinor(due, due.AddDays(offset), rate).Should().Be(expected);
    }

    [Fact]
    public void Invalid_negative_rate_is_rejected()
    {
        var calculate = () => LendingRules.FineMinor(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), -1);
        calculate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Fine_does_not_silently_overflow()
    {
        var calculate = () => LendingRules.FineMinor(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3), long.MaxValue);
        calculate.Should().Throw<OverflowException>();
    }

    [Theory]
    [InlineData(MembershipStatus.Active, 5, 0, false, true)]
    [InlineData(MembershipStatus.Active, 5, 4, false, true)]
    [InlineData(MembershipStatus.Active, 1, 0, false, true)]
    [InlineData(MembershipStatus.Active, 5, 5, false, false)]
    [InlineData(MembershipStatus.Active, 5, 6, false, false)]
    [InlineData(MembershipStatus.Active, 5, 1, true, false)]
    [InlineData(MembershipStatus.Suspended, 5, 0, false, false)]
    [InlineData(MembershipStatus.Expired, 5, 0, false, false)]
    [InlineData(MembershipStatus.Active, 0, 0, false, false)]
    [InlineData(MembershipStatus.Active, -1, 0, false, false)]
    [InlineData(MembershipStatus.Active, 5, -1, false, false)]
    public void Borrowing_enforces_membership_overdue_and_limit_rules(MembershipStatus status, int limit, int active, bool overdue, bool expected)
    {
        var result = LendingRules.CanBorrow(status, limit, active, overdue);
        result.IsSuccess.Should().Be(expected);
        if (expected) result.Value.Should().BeTrue();
        else result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Queue_chooses_oldest_waiting_reservation_without_mutating_input()
    {
        var time = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var queue = new[]
        {
            new Reservation { Id = 4, CreatedAtUtc = time.AddHours(1) },
            new Reservation { Id = 2, CreatedAtUtc = time.AddHours(-1), Status = ReservationStatus.Ready },
            new Reservation { Id = 3, CreatedAtUtc = time },
            new Reservation { Id = 1, CreatedAtUtc = time.AddHours(-2), Status = ReservationStatus.Cancelled }
        };
        LendingRules.NextInQueue(queue)!.Id.Should().Be(3);
        queue.Select(x => x.Id).Should().Equal(4, 2, 3, 1);
        queue[2].Status.Should().Be(ReservationStatus.Waiting);
    }

    [Fact]
    public void Queue_uses_id_to_break_equal_timestamp_ties()
    {
        var time = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        var queue = new[] { new Reservation { Id = 10, CreatedAtUtc = time }, new Reservation { Id = 9, CreatedAtUtc = time } };
        LendingRules.NextInQueue(queue)!.Id.Should().Be(9);
    }

    [Fact]
    public void Empty_queue_has_no_candidate() => LendingRules.NextInQueue(Array.Empty<Reservation>()).Should().BeNull();

    [Theory]
    [InlineData(ReservationStatus.Ready)]
    [InlineData(ReservationStatus.Fulfilled)]
    [InlineData(ReservationStatus.Expired)]
    [InlineData(ReservationStatus.Cancelled)]
    public void Queue_ignores_every_non_waiting_status(ReservationStatus status) =>
        LendingRules.NextInQueue(new[] { new Reservation { Id = 1, Status = status } }).Should().BeNull();

    [Theory]
    [InlineData(ReservationStatus.Waiting, true)]
    [InlineData(ReservationStatus.Ready, true)]
    [InlineData(ReservationStatus.Fulfilled, false)]
    [InlineData(ReservationStatus.Expired, false)]
    [InlineData(ReservationStatus.Cancelled, false)]
    public void Duplicate_reservation_checks_include_waiting_and_ready(ReservationStatus status, bool expected) =>
        LendingRules.IsActiveReservation(status).Should().Be(expected);

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Pickup_expires_at_the_deadline_not_after_it(int secondsAfterDeadline, bool expected)
    {
        var reservation = ReadyReservation();
        LendingRules.IsPickupExpired(reservation, reservation.PickupDeadlineUtc!.Value.AddSeconds(secondsAfterDeadline)).Should().Be(expected);
    }

    [Fact]
    public void Fulfilled_reservation_is_not_expired_by_a_later_sweep()
    {
        var reservation = ReadyReservation();
        reservation.Status = ReservationStatus.Fulfilled;
        LendingRules.IsPickupExpired(reservation, reservation.PickupDeadlineUtc!.Value.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Waiting_reservation_has_no_pickup_deadline() =>
        LendingRules.IsPickupExpired(new Reservation(), DateTime.UtcNow).Should().BeFalse();

    [Fact]
    public void Rightful_member_can_collect_ready_unexpired_copy()
    {
        var reservation = ReadyReservation();
        LendingRules.CanPickup(reservation, 7, reservation.PickupDeadlineUtc!.Value.AddMinutes(-1)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Other_member_cannot_collect_reserved_copy()
    {
        var reservation = ReadyReservation();
        LendingRules.CanPickup(reservation, 8, reservation.PickupDeadlineUtc!.Value.AddMinutes(-1)).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(ReservationStatus.Waiting)]
    [InlineData(ReservationStatus.Fulfilled)]
    [InlineData(ReservationStatus.Expired)]
    [InlineData(ReservationStatus.Cancelled)]
    public void Pickup_requires_ready_status(ReservationStatus status)
    {
        var reservation = ReadyReservation();
        reservation.Status = status;
        LendingRules.CanPickup(reservation, 7, reservation.PickupDeadlineUtc!.Value.AddMinutes(-1)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Pickup_rejects_missing_copy()
    {
        var reservation = ReadyReservation();
        reservation.AssignedCopyId = null;
        LendingRules.CanPickup(reservation, 7, reservation.PickupDeadlineUtc!.Value.AddMinutes(-1)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Pickup_rejects_missing_deadline()
    {
        var reservation = ReadyReservation();
        reservation.PickupDeadlineUtc = null;
        LendingRules.CanPickup(reservation, 7, new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Pickup_rejects_exact_expiry_boundary()
    {
        var reservation = ReadyReservation();
        LendingRules.CanPickup(reservation, 7, reservation.PickupDeadlineUtc!.Value).IsSuccess.Should().BeFalse();
    }

    private static Reservation ReadyReservation() => new()
    {
        Id = 1,
        MemberId = 7,
        AssignedCopyId = 12,
        Status = ReservationStatus.Ready,
        ReadyAtUtc = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc),
        PickupDeadlineUtc = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc)
    };
}
