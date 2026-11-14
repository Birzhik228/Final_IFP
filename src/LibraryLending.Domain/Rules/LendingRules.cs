namespace LibraryLending.Domain;

/// <summary>Pure rules: time, database state and configuration are supplied by callers.</summary>
public static class LendingRules
{
    public static int DaysOverdue(DateOnly due, DateOnly today) => Math.Max(0, today.DayNumber - due.DayNumber);

    public static long FineMinor(DateOnly due, DateOnly end, long dailyRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dailyRate);
        // Overflow is a configuration/programming error, not an expected user-facing failure.
        return checked(DaysOverdue(due, end) * dailyRate);
    }

    public static Result<bool> CanBorrow(MembershipStatus status, int limit, int activeLoans, bool hasOverdue)
    {
        if (status != MembershipStatus.Active)
            return Result<bool>.Failure("Only active members can borrow books.");
        if (limit < 1 || activeLoans < 0)
            return Result<bool>.Failure("The member's borrowing settings are invalid.");
        if (hasOverdue)
            return Result<bool>.Failure("Return all overdue loans before borrowing another book.");
        if (activeLoans >= limit)
            return Result<bool>.Failure($"The borrowing limit of {limit} active loans has been reached.");
        return Result<bool>.Success(true);
    }

    public static Reservation? NextInQueue(IEnumerable<Reservation> reservations) => reservations
        .Where(reservation => reservation.Status == ReservationStatus.Waiting)
        .OrderBy(reservation => reservation.CreatedAtUtc)
        .ThenBy(reservation => reservation.Id)
        .FirstOrDefault();

    public static bool IsActiveReservation(ReservationStatus status) =>
        status is ReservationStatus.Waiting or ReservationStatus.Ready;

    public static bool IsPickupExpired(Reservation reservation, DateTime utcNow) =>
        reservation.Status == ReservationStatus.Ready &&
        reservation.PickupDeadlineUtc is { } deadline && utcNow >= deadline;

    public static Result<bool> CanPickup(Reservation reservation, int memberId, DateTime utcNow)
    {
        if (reservation.MemberId != memberId)
            return Result<bool>.Failure("This reservation belongs to another member.");
        if (reservation.Status != ReservationStatus.Ready || reservation.AssignedCopyId is null || reservation.PickupDeadlineUtc is null)
            return Result<bool>.Failure("This reservation is not ready for pickup.");
        if (IsPickupExpired(reservation, utcNow))
            return Result<bool>.Failure("The pickup deadline has passed.");
        return Result<bool>.Success(true);
    }
}
