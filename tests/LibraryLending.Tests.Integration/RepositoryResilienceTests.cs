using FluentAssertions;
using LibraryLending.Application;
using LibraryLending.Domain;
using LibraryLending.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LibraryLending.Tests.Integration;

public sealed class RepositoryResilienceTests : LibraryTestBase
{
    private EfLibraryRepository Repository(INotificationPublisher? publisher = null)
    {
        using var scope = Factory.Services.CreateScope();
        return new EfLibraryRepository(scope.ServiceProvider.GetRequiredService<IDbContextFactory<LibraryDbContext>>(),
            Factory.Clock, Factory.Options, publisher ?? Substitute.For<INotificationPublisher>(),
            NullLogger<EfLibraryRepository>.Instance, scope.ServiceProvider.GetRequiredService<LibraryWriteCoordinator>());
    }

    [Fact]
    public async Task Expected_failure_rolls_back_even_changes_saved_earlier_in_the_transaction()
    {
        var memberId = await NewMember();
        var original = await Database(db => db.Members.Where(x => x.Id == memberId).Select(x => x.Name).SingleAsync());
        var result = await Repository().ExecuteAsync<int>(async session =>
        {
            var member = await session.MemberAsync(memberId, default);
            member!.Name = "Must not survive rollback";
            await session.SaveAsync(default);
            return Result<int>.Failure("The last validation check failed.");
        }, default);

        result.IsSuccess.Should().BeFalse();
        (await Database(db => db.Members.Where(x => x.Id == memberId).Select(x => x.Name).SingleAsync())).Should().Be(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Busy_retry_restarts_the_whole_transaction_without_duplicating_mutations(bool wrapped)
    {
        var memberId = await NewMember();
        var attempts = 0;
        var result = await Repository().ExecuteAsync<int>(async session =>
        {
            var member = await session.MemberAsync(memberId, default);
            member!.BorrowingLimit++;
            await session.SaveAsync(default);
            attempts++;
            if (attempts < 3)
            {
                var busy = new SqliteException("Injected transient writer contention.", 5);
                if (wrapped) throw new DbUpdateException("Save failed.", busy);
                throw busy;
            }
            return Result<int>.Success(member.BorrowingLimit);
        }, default);

        attempts.Should().Be(3);
        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value.Should().Be(6);
        (await Database(db => db.Members.Where(x => x.Id == memberId).Select(x => x.BorrowingLimit).SingleAsync())).Should().Be(6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exhausted_busy_retries_return_clear_failure_and_leave_database_unchanged(bool wrapped)
    {
        var memberId = await NewMember();
        var attempts = 0;
        var result = await Repository().ExecuteAsync<int>(async session =>
        {
            var member = await session.MemberAsync(memberId, default);
            member!.BorrowingLimit++;
            await session.SaveAsync(default);
            attempts++;
            var busy = new SqliteException("Injected persistent writer contention.", 6);
            if (wrapped) throw new DbUpdateException("Save failed.", busy);
            throw busy;
        }, default);

        attempts.Should().Be(3);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("busy");
        (await Database(db => db.Members.Where(x => x.Id == memberId).Select(x => x.BorrowingLimit).SingleAsync())).Should().Be(5);
    }

    [Fact]
    public async Task Concurrency_retries_reread_state_and_stop_after_the_bound()
    {
        var attempts = 0;
        var result = await Repository().ExecuteAsync<int>(_ =>
        {
            attempts++;
            throw new DbUpdateConcurrencyException("Injected stale version.");
        }, default);
        attempts.Should().Be(3);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Another operation changed");
    }

    [Fact]
    public async Task Constraint_failure_is_reported_as_result_and_rolls_back_the_operation()
    {
        var book = await NewBook();
        var result = await Repository().ExecuteAsync<int>(async session =>
        {
            session.Add(new BookCopy { BookId = book.Book.Id, Barcode = book.Copies.Single().Barcode });
            await session.SaveAsync(default);
            return Result<int>.Success(1);
        }, default);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("conflicts");
        (await Database(db => db.Copies.CountAsync(x => x.BookId == book.Book.Id))).Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_before_commit_rolls_back_saved_changes()
    {
        var memberId = await NewMember();
        using var cancellation = new CancellationTokenSource();
        Func<Task> operation = () => Repository().ExecuteAsync<int>(async session =>
        {
            var member = await session.MemberAsync(memberId, cancellation.Token);
            member!.BorrowingLimit = 9;
            await session.SaveAsync(cancellation.Token);
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return Result<int>.Success(1);
        }, cancellation.Token);
        await operation.Should().ThrowAsync<OperationCanceledException>();
        (await Database(db => db.Members.Where(x => x.Id == memberId).Select(x => x.BorrowingLimit).SingleAsync())).Should().Be(5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_cancelled_push_does_not_reverse_or_misreport_a_committed_operation(bool cancelled)
    {
        var memberId = await NewMember();
        var publisher = Substitute.For<INotificationPublisher>();
        publisher.PublishAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => cancelled
            ? Task.FromCanceled(new CancellationToken(true))
            : Task.FromException(new IOException("Live transport unavailable.")));
        var key = "resilience:" + Guid.NewGuid();
        var result = await Repository(publisher).ExecuteAsync<int>(async session =>
        {
            var member = await session.MemberAsync(memberId, default);
            member!.BorrowingLimit = 8;
            await session.NotifyAsync(MemberActor.UserId, "test", "Durable message", key, Factory.Clock.GetUtcNow().UtcDateTime, default);
            return Result<int>.Success(memberId);
        }, default);

        result.IsSuccess.Should().BeTrue(result.Error);
        (await Database(db => db.Members.Where(x => x.Id == memberId).Select(x => x.BorrowingLimit).SingleAsync())).Should().Be(8);
        (await Database(db => db.Notifications.CountAsync(x => x.DedupeKey == key))).Should().Be(1);
        publisher.ReceivedCalls().Should().ContainSingle(call =>
            call.GetMethodInfo().Name == nameof(INotificationPublisher.PublishAsync)
            && (string)call.GetArguments()[0]! == MemberActor.UserId);
    }

    [Fact]
    public async Task Request_cancellation_after_commit_does_not_cancel_independent_notification_delivery()
    {
        using var request = new CancellationTokenSource();
        var publisher = Substitute.For<INotificationPublisher>();
        publisher.PublishAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            request.Cancel();
            call.ArgAt<CancellationToken>(1).IsCancellationRequested.Should().BeFalse();
            return Task.CompletedTask;
        });
        var key = "after-commit:" + Guid.NewGuid();
        var result = await Repository(publisher).ExecuteAsync<int>(async session =>
        {
            await session.NotifyAsync(MemberActor.UserId, "test", "Saved before cancellation", key, Factory.Clock.GetUtcNow().UtcDateTime, request.Token);
            return Result<int>.Success(1);
        }, request.Token);
        request.IsCancellationRequested.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        (await Database(db => db.Notifications.CountAsync(x => x.DedupeKey == key))).Should().Be(1);
    }

    [Fact]
    public async Task Cancelling_a_queued_writer_never_enters_its_transaction_or_blocks_the_next_writer()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Repository().ExecuteAsync<int>(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return Result<int>.Success(1);
        }, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var cancellation = new CancellationTokenSource();
            var cancelledWriterEntered = false;
            var queued = Repository().ExecuteAsync<int>(_ =>
            {
                cancelledWriterEntered = true;
                return Task.FromResult(Result<int>.Success(2));
            }, cancellation.Token);
            cancellation.Cancel();
            Func<Task> waitForQueued = () => queued;
            await waitForQueued.Should().ThrowAsync<OperationCanceledException>();
            cancelledWriterEntered.Should().BeFalse();
        }
        finally { release.TrySetResult(); }
        (await first).IsSuccess.Should().BeTrue();
        (await Repository().ExecuteAsync(_ => Task.FromResult(Result<int>.Success(3)), default)
            .WaitAsync(TimeSpan.FromSeconds(10))).Value.Should().Be(3);
    }

    [Fact]
    public async Task Unexpected_transaction_failure_releases_the_writer_gate()
    {
        Func<Task> failed = () => Repository().ExecuteAsync<int>(_ => throw new IOException("Injected operation failure."), default);
        await failed.Should().ThrowAsync<IOException>();
        (await Repository().ExecuteAsync(_ => Task.FromResult(Result<int>.Success(1)), default)
            .WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_slow_live_notification_does_not_hold_the_writer_gate()
    {
        var publishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublisher = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = Substitute.For<INotificationPublisher>();
        publisher.PublishAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            publishing.TrySetResult();
            return releasePublisher.Task;
        });
        var first = Repository(publisher).ExecuteAsync<int>(async session =>
        {
            await session.NotifyAsync(MemberActor.UserId, "test", "Delivery in progress", "gate:" + Guid.NewGuid(), Factory.Clock.GetUtcNow().UtcDateTime, default);
            return Result<int>.Success(1);
        }, default);
        await publishing.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var second = await Repository().ExecuteAsync(_ => Task.FromResult(Result<int>.Success(2)), default)
                .WaitAsync(TimeSpan.FromSeconds(10));
            second.IsSuccess.Should().BeTrue();
            first.IsCompleted.Should().BeFalse("the second write must finish while live delivery is still pending");
        }
        finally { releasePublisher.TrySetResult(); }
        (await first).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Member_scoped_reservation_projection_preserves_global_fifo_position_with_ties()
    {
        var book = await NewBook();
        var borrower = await NewMember();
        var firstMember = await NewMember();
        (await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, borrower))).IsSuccess.Should().BeTrue();
        var first = await Use(s => s.ReserveAsync(Librarian, book.Book.Id, firstMember));
        var second = await Use(s => s.ReserveAsync(MemberActor, book.Book.Id, EligibleMemberId));
        var mine = (await Use(s => s.GetReservationsAsync(MemberActor))).Single(x => x.Id == second.Value);
        mine.QueuePosition.Should().Be(2);
        (await Use(s => s.GetReservationsAsync(MemberActor))).Should().NotContain(x => x.MemberId == firstMember);
        (await Use(s => s.CancelReservationAsync(Librarian, first.Value))).IsSuccess.Should().BeTrue();
        (await Use(s => s.GetReservationsAsync(MemberActor))).Single(x => x.Id == second.Value).QueuePosition.Should().Be(1);
    }
}
