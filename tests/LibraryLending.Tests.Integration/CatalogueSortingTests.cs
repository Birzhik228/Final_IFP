using FluentAssertions;
using LibraryLending.Application;
using Xunit;

namespace LibraryLending.Tests.Integration;

public sealed class CatalogueSortingTests : LibraryTestBase
{
    [Fact]
    public async Task Recently_added_order_is_applied_before_paging_and_preserves_filters()
    {
        var oldest = await NewBook();
        var middle = await NewBook();
        var newest = await NewBook();
        (await Use(s => s.BorrowAsync(Librarian, newest.Copies[0].Id, EligibleMemberId))).IsSuccess.Should().BeTrue();

        var recent = await Use(s => s.SearchBooksAsync(new BookQuery(Category: "Integration", PageSize: 1, Sort: "newest")));
        recent.TotalCount.Should().Be(3);
        recent.Items.Should().ContainSingle().Which.Id.Should().Be(newest.Book.Id);
        var second = await Use(s => s.SearchBooksAsync(new BookQuery(Category: "Integration", Page: 2, PageSize: 1, Sort: "newest")));
        second.Items.Should().ContainSingle().Which.Id.Should().Be(middle.Book.Id);
        var available = await Use(s => s.SearchBooksAsync(new BookQuery(Category: "Integration", AvailableOnly: true, Sort: "newest")));
        available.TotalCount.Should().Be(2);
        available.Items.Select(x => x.Id).Should().Equal(middle.Book.Id, oldest.Book.Id);
        var byTitle = await Use(s => s.SearchBooksAsync(new BookQuery(Category: "Integration")));
        byTitle.Items.Select(x => x.Id).Should().Equal(oldest.Book.Id, middle.Book.Id, newest.Book.Id);
    }
}
