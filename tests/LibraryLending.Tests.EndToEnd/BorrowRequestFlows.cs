using Microsoft.Playwright;
using Xunit;

namespace LibraryLending.Tests.EndToEnd;

public sealed partial class CirculationFlows
{
    [Fact]
    public async Task Borrow_request_is_reviewed_live_and_acceptance_creates_the_members_loan()
    {
        await SignInAsync(Page, "librarian@library.local");
        var title = await CreateBookAsync(Page);
        var bookUrl = Page.Url;
        await NavigateReadyAsync(Page, "/requests");

        await using var memberContext = await Browser.NewContextAsync(ContextOptions());
        var member = await memberContext.NewPageAsync();
        await SignInAsync(member, "member10@library.local");
        await NavigateReadyAsync(member, bookUrl);
        await member.GetByRole(AriaRole.Button, new() { Name = "Request to borrow", Exact = true }).ClickAsync();
        await Expect(member.GetByRole(AriaRole.Main)).ToContainTextAsync("Request pending");
        await Expect(member.GetByRole(AriaRole.Button, new() { Name = "Request to borrow", Exact = true })).ToBeDisabledAsync();
        // The librarian was already on this page: no reload should be necessary to see the submission.
        var staffRow = Page.GetByTestId("borrow-request-row").Filter(new() { HasText = title });
        await Expect(staffRow).ToContainTextAsync("Riley James", new() { Timeout = 15000 });

        await NavigateReadyAsync(member, "/requests");
        var memberRow = member.GetByTestId("borrow-request-row").Filter(new() { HasText = title });
        await Expect(memberRow).ToContainTextAsync("Pending");
        await Expect(member.GetByRole(AriaRole.Button, new() { Name = "Accept & issue book", Exact = true })).ToHaveCountAsync(0);
        await staffRow.GetByRole(AriaRole.Button, new() { Name = "Accept & issue book", Exact = true }).ClickAsync();
        var confirm = Page.GetByRole(AriaRole.Dialog, new() { Name = "Accept borrow request?", Exact = true });
        await Expect(confirm).ToContainTextAsync("creates a loan now");
        await confirm.GetByRole(AriaRole.Button, new() { Name = "Accept & issue book", Exact = true }).ClickAsync();
        await Expect(confirm).ToHaveCountAsync(0);
        // A live decision removes the member's pending row, then appears in their history.
        await Expect(memberRow).ToHaveCountAsync(0, new() { Timeout = 15000 });
        await member.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        await Expect(memberRow).ToContainTextAsync("Accepted");
        await Expect(memberRow).ToContainTextAsync("Due");
        await memberRow.GetByRole(AriaRole.Link, new() { Name = "View loan", Exact = true }).ClickAsync();
        await Expect(member.GetByTestId("loan-row").Filter(new() { HasText = title })).ToContainTextAsync("On loan");
        await NavigateReadyAsync(member, "/notifications");
        await Expect(member.GetByRole(AriaRole.Main)).ToContainTextAsync(title);

        // Return this test's loan so repeated browser-matrix runs do not exhaust Riley's allowance.
        await SearchLoansAsync(Page, title);
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Return {title}", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Confirm return", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("loan-row").Filter(new() { HasText = title })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Borrow_request_rejection_is_private_and_members_can_cancel_a_later_request()
    {
        await SignInAsync(Page, "librarian@library.local");
        var title = await CreateBookAsync(Page);
        var bookUrl = Page.Url;
        await using var memberContext = await Browser.NewContextAsync(ContextOptions());
        var member = await memberContext.NewPageAsync();
        await SignInAsync(member, "member10@library.local");
        await NavigateReadyAsync(member, bookUrl);
        await member.GetByRole(AriaRole.Button, new() { Name = "Request to borrow", Exact = true }).ClickAsync();
        await Expect(member.GetByRole(AriaRole.Main)).ToContainTextAsync("Request pending");
        await NavigateReadyAsync(member, "/requests");
        var memberRow = member.GetByTestId("borrow-request-row").Filter(new() { HasText = title });
        await Expect(memberRow).ToContainTextAsync("Pending");

        await using var otherContext = await Browser.NewContextAsync(ContextOptions());
        var other = await otherContext.NewPageAsync();
        await SignInAsync(other, "member7@library.local");
        await NavigateReadyAsync(other, "/requests");
        await Expect(other.GetByTestId("borrow-request-row").Filter(new() { HasText = title })).ToHaveCountAsync(0);

        await NavigateReadyAsync(Page, "/requests");
        var staffRow = Page.GetByTestId("borrow-request-row").Filter(new() { HasText = title });
        await staffRow.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).ClickAsync();
        var dialog = Page.GetByRole(AriaRole.Dialog, new() { Name = "Reject borrow request?", Exact = true });
        const string reason = "Please visit the desk before requesting this edition.";
        await dialog.GetByLabel("Reason (optional)", new() { Exact = true }).FillAsync(reason);
        var light = await BrowserAccessibility.ScanAsync(Page, Browser, "/requests (reject dialog light)");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Reject request", Exact = true }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(memberRow).ToHaveCountAsync(0, new() { Timeout = 15000 });
        await member.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        await Expect(memberRow).ToContainTextAsync("Rejected");
        await Expect(memberRow).ToContainTextAsync(reason);
        await member.SetViewportSizeAsync(390, 844);
        await member.GetByRole(AriaRole.Button, new() { Name = "Toggle light or dark theme", Exact = true }).ClickAsync();
        await Expect(member.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        Assert.True(await member.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"));
        var dark = await BrowserAccessibility.ScanAsync(member, Browser, "/requests (member history phone dark)");
        await BrowserAccessibility.SaveAndAssertAsync(Browser, "borrow-request-review", new[] { light, dark });
        await other.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        await Expect(other.GetByRole(AriaRole.Main)).Not.ToContainTextAsync(reason);
        await Expect(other.GetByTestId("borrow-request-row").Filter(new() { HasText = title })).ToHaveCountAsync(0);

        await NavigateReadyAsync(member, bookUrl);
        await member.GetByRole(AriaRole.Button, new() { Name = "Request to borrow", Exact = true }).ClickAsync();
        await Expect(member.GetByRole(AriaRole.Main)).ToContainTextAsync("Request pending");
        await NavigateReadyAsync(member, "/requests");
        await memberRow.GetByRole(AriaRole.Button, new() { Name = "Cancel request", Exact = true }).ClickAsync();
        await member.GetByRole(AriaRole.Dialog, new() { Name = "Cancel borrow request?", Exact = true })
            .GetByRole(AriaRole.Button, new() { Name = "Confirm cancellation", Exact = true }).ClickAsync();
        await Expect(memberRow).ToHaveCountAsync(0);
        await member.GetByRole(AriaRole.Button, new() { Name = "History", Exact = true }).ClickAsync();
        await Expect(memberRow.Filter(new() { HasText = "Cancelled" })).ToHaveCountAsync(1);
        await NavigateReadyAsync(Page, bookUrl);
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Borrow copy ") })).ToHaveCountAsync(1);
    }
}
