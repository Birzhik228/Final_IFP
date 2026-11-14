using FluentAssertions;
using LibraryLending.Application;
using LibraryLending.Domain;
using NSubstitute;
using Xunit;

namespace LibraryLending.Tests.Unit;

public sealed class ApplicationFailureTests
{
    private readonly ILibraryRepository repository = Substitute.For<ILibraryRepository>();
    private readonly Actor librarian = new("librarian", true, null);
    private LibraryService Service => new(repository, TimeProvider.System, new LibraryOptions());

    [Fact]
    public async Task Failed_deadline_transaction_is_observable_to_the_background_worker()
    {
        repository.ExecuteAsync<int>(Arg.Any<Func<ILibrarySession, Task<Result<int>>>>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure("The library is busy."));
        Func<Task> sweep = () => Service.ProcessDeadlinesAsync();
        await sweep.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Deadline processing failed*busy*");
    }

    public static IEnumerable<object[]> InvalidBooks()
    {
        var valid = new BookInput("Valid title", "Valid author", "9780000000002", "Fiction", 2024, "", "");
        yield return [valid with { Title = " " }];
        yield return [valid with { Isbn = null! }];
        yield return [valid with { Isbn = "not-an-isbn" }];
        yield return [valid with { Authors = null! }];
        yield return [valid with { Category = new string('c', 81) }];
        yield return [valid with { Year = 0 }];
        yield return [valid with { Copies = 51 }];
        yield return [valid with { CoverUrl = "javascript:alert(1)" }];
        yield return [valid with { Description = new string('d', 4001) }];
    }

    [Theory]
    [MemberData(nameof(InvalidBooks))]
    public async Task Malformed_book_input_fails_before_opening_a_write_transaction(BookInput input)
    {
        var result = await Service.SaveBookAsync(librarian, null, input);
        result.IsSuccess.Should().BeFalse();
        await repository.DidNotReceive().ExecuteAsync<int>(Arg.Any<Func<ILibrarySession, Task<Result<int>>>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "a@example.test", 5)]
    [InlineData("A member", null, 5)]
    [InlineData("A member", "invalid", 5)]
    [InlineData("A member", "a@example.test", 0)]
    [InlineData("A member", "a@example.test", 51)]
    public async Task Malformed_member_input_is_a_validation_result_not_an_exception(string? name, string? email, int limit)
    {
        var result = await Service.SaveMemberAsync(librarian, null, new MemberInput(name!, email!, null!, MembershipStatus.Active, limit));
        result.IsSuccess.Should().BeFalse();
        await repository.DidNotReceive().ExecuteAsync<int>(Arg.Any<Func<ILibrarySession, Task<Result<int>>>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AB")]
    public async Task Missing_or_short_barcode_is_rejected_before_persistence(string? barcode)
    {
        (await Service.AddCopyAsync(librarian, 1, barcode!)).IsSuccess.Should().BeFalse();
        await repository.DidNotReceive().ExecuteAsync<int>(Arg.Any<Func<ILibrarySession, Task<Result<int>>>>(), Arg.Any<CancellationToken>());
    }
}
