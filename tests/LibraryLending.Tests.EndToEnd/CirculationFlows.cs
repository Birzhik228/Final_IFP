using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using Xunit;

namespace LibraryLending.Tests.EndToEnd;

[CollectionDefinition("Library browser workflows", DisableParallelization = true)]
public sealed class BrowserCollection { }

[Collection("Library browser workflows")]
public sealed class CirculationFlows : PageTest
{
    private static readonly string BaseUrl = (Environment.GetEnvironmentVariable("LIBRARY_BASE_URL") ?? "http://localhost:5180").TrimEnd('/');
    private const string Password = "LibraryDemo1!";

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        ViewportSize = new() { Width = 1440, Height = 1000 },
        Locale = "en-US",
        ColorScheme = ColorScheme.Light
    };

    [Fact]
    public async Task Librarian_can_borrow_and_return_a_physical_copy()
    {
        await SignInAsync(Page, "librarian@library.local");
        var title = await CreateBookAsync(Page);
        await BorrowToJamieAsync(Page);

        await SearchLoansAsync(Page, title);
        var row = Page.GetByTestId("loan-row").Filter(new() { HasText = title });
        await Expect(row).ToContainTextAsync("Jamie Chen");
        await Expect(row).ToContainTextAsync("On loan");
        await row.GetByRole(AriaRole.Button, new() { Name = $"Return {title}", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm return", Exact = true }).ClickAsync();
        await Expect(row).ToHaveCountAsync(0);

        await Page.GetByLabel("Loan status", new() { Exact = true }).SelectOptionAsync("returned");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("loan-row").Filter(new() { HasText = title })).ToContainTextAsync("Returned");
    }

    [Fact]
    public async Task Member_reservation_becomes_ready_after_librarian_returns_the_copy()
    {
        await SignInAsync(Page, "librarian@library.local");
        var title = await CreateBookAsync(Page);
        var bookUrl = Page.Url;
        await BorrowToJamieAsync(Page);

        await using var memberContext = await Browser.NewContextAsync(ContextOptions());
        var memberPage = await memberContext.NewPageAsync();
        await SignInAsync(memberPage, "member10@library.local");
        await NavigateReadyAsync(memberPage, bookUrl);
        await memberPage.GetByRole(AriaRole.Button, new() { Name = "Join reservation queue", Exact = true }).ClickAsync();
        await Expect(memberPage.GetByRole(AriaRole.Main)).ToContainTextAsync("1 waiting");
        await NavigateReadyAsync(memberPage, "/reservations");
        var memberRow = memberPage.GetByTestId("reservation-row").Filter(new() { HasText = title });
        await Expect(memberRow).ToContainTextAsync("Waiting");

        await SearchLoansAsync(Page, title);
        await Page.GetByRole(AriaRole.Button, new() { Name = $"Return {title}", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Confirm return", Exact = true }).ClickAsync();

        // This assertion uses the second live session: it exercises SignalR-driven refresh.
        await Expect(memberRow).ToContainTextAsync("Ready", new() { Timeout = 15000 });
        await NavigateReadyAsync(memberPage, "/notifications");
        await Expect(memberPage.GetByRole(AriaRole.Main)).ToContainTextAsync(title);
        var unreadReport = await BrowserAccessibility.ScanAsync(memberPage, Browser, "/notifications (unread)");
        await memberPage.GetByRole(AriaRole.Button, new() { Name = "Mark all as read", Exact = true }).ClickAsync();
        await Expect(memberPage.Locator(".unread-dot")).ToHaveCountAsync(0);
        var readReport = await BrowserAccessibility.ScanAsync(memberPage, Browser, "/notifications (read)");
        await BrowserAccessibility.SaveAndAssertAsync(Browser, "notification-read-states", new[] { unreadReport, readReport });

        // The held copy belongs to Riley, and a normal borrow action must not be offered.
        await NavigateReadyAsync(Page, bookUrl);
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Borrow copy ") })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("Reserved");
    }

    [Fact]
    public async Task Overdue_report_filters_seeded_loans_and_downloads_csv()
    {
        await SignInAsync(Page, "librarian@library.local");
        await NavigateReadyAsync(Page, "/overdue");
        await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("Clean Code");
        var input = Page.Locator("input[type=search]");
        await input.FillAsync("Alex Morgan");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Apply filters", Exact = true }).ClickAsync();
        var rows = Page.GetByTestId("loan-row");
        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows.First).ToContainTextAsync("Alex Morgan");
        var download = await Page.RunAndWaitForDownloadAsync(async () =>
            await Page.GetByRole(AriaRole.Link, new() { NameRegex = new Regex("CSV", RegexOptions.IgnoreCase) }).ClickAsync());
        Assert.EndsWith(".csv", download.SuggestedFilename, StringComparison.OrdinalIgnoreCase);
        var path = await download.PathAsync();
        Assert.NotNull(path);
        var content = await File.ReadAllTextAsync(path!);
        Assert.Contains("Alex Morgan", content);
        Assert.Contains("Clean Code", content);
    }

    [Fact]
    public async Task Member_catalogue_is_usable_at_phone_width()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await SignInAsync(Page, "member10@library.local");
        await NavigateReadyAsync(Page, "/books");
        await Expect(Page.GetByLabel("Search books", new() { Exact = true })).ToBeVisibleAsync();
        var categories = Page.GetByRole(AriaRole.Group, new() { Name = "Book categories", Exact = true });
        await categories.GetByRole(AriaRole.Button, new() { Name = "Science", Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("Category", new() { Exact = true })).ToHaveValueAsync("Science");
        await Expect(Page.GetByTestId("book-card")).ToHaveCountAsync(4);
        await Page.GetByLabel("Sort by", new() { Exact = true }).SelectOptionAsync("newest");
        await Expect(Page.GetByTestId("book-card").First).ToContainTextAsync("Silent Spring");
        await Page.GetByLabel("Sort by", new() { Exact = true }).SelectOptionAsync("title");
        await Expect(Page.GetByTestId("book-card").First).ToContainTextAsync("A Brief History of Time");
        await categories.GetByRole(AriaRole.Button, new() { Name = "All books", Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("Category", new() { Exact = true })).ToHaveValueAsync("");

        await Page.GetByLabel("Search books", new() { Exact = true }).FillAsync("Clean Code");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("book-card")).ToHaveCountAsync(1);
        await Page.GetByLabel("Available now", new() { Exact = true }).CheckAsync();
        await Expect(Page.GetByTestId("book-card")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "No books found", Exact = true })).ToBeVisibleAsync();
        await Page.GetByLabel("Available now", new() { Exact = true }).UncheckAsync();
        await Expect(Page.GetByTestId("book-card")).ToHaveCountAsync(1);
        await Page.GetByLabel("Search books", new() { Exact = true }).FillAsync("Pride and Prejudice");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("book-card")).ToHaveCountAsync(1);
        await Expect(Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Add book") })).ToHaveCountAsync(0);
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"));
        await ScreenshotAsync(Page, "catalogue-phone-light.png");
        var light = await BrowserAccessibility.ScanAsync(Page, Browser, "/books (member phone light)");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Toggle light or dark theme", Exact = true }).ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        await ScreenshotAsync(Page, "catalogue-phone-dark.png");
        var dark = await BrowserAccessibility.ScanAsync(Page, Browser, "/books (member phone dark)");
        await BrowserAccessibility.SaveAndAssertAsync(Browser, "member-catalogue-phone", new[] { light, dark });
    }

    [Fact]
    public async Task Librarian_dashboard_and_catalogue_render_in_both_themes()
    {
        await SignInAsync(Page, "librarian@library.local");
        await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("Most borrowed");
        await ScreenshotAsync(Page, "dashboard-desktop-light.png");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Toggle light or dark theme", Exact = true }).ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        await ScreenshotAsync(Page, "dashboard-desktop-dark.png");
        await NavigateReadyAsync(Page, "/books");
        await Expect(Page.GetByTestId("book-card").First).ToBeVisibleAsync();
        await ScreenshotAsync(Page, "catalogue-desktop-dark.png");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Toggle light or dark theme", Exact = true }).ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "light");
        await ScreenshotAsync(Page, "catalogue-desktop-light.png");
    }

    [Fact]
    public async Task Librarian_cannot_create_a_fourth_member_sign_in_account()
    {
        await SignInAsync(Page, "librarian@library.local");
        var unique = Guid.NewGuid().ToString("N")[..10];
        var name = "Browser member " + unique;
        var email = $"browser-{unique}@library.local";
        await NavigateReadyAsync(Page, "/members");
        await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("3 of 3 member sign-in accounts");
        await Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Add member") }).ClickAsync();
        await Page.GetByLabel("Full name", new() { Exact = true }).FillAsync(name);
        await Page.GetByLabel("Email", new() { Exact = true }).FillAsync(email);
        await Page.GetByLabel("Phone", new() { Exact = true }).FillAsync("5550101");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save member", Exact = true }).ClickAsync();
        await Expect(Page.GetByLabel("Full name", new() { Exact = true })).ToHaveCountAsync(0);
        await Page.GetByLabel("Search members", new() { Exact = true }).FillAsync(name);
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await row.GetByRole(AriaRole.Button, new() { Name = "Create sign-in account", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("The library allows a maximum of 3 member login accounts. No account was created.");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Member sign-in account created", Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByLabel("Temporary password", new() { Exact = true })).ToHaveCountAsync(0);
        await NavigateReadyAsync(Page, "/members");
        await Page.GetByLabel("Search members", new() { Exact = true }).FillAsync(name);
        await Expect(Page.GetByRole(AriaRole.Row).Filter(new() { HasText = name })
            .GetByRole(AriaRole.Button, new() { Name = "Create sign-in account", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("3 of 3 member sign-in accounts");
    }

    [Fact]
    public async Task Member_can_change_their_password_and_sign_in_with_it()
    {
        const string email = "member7@library.local";
        var newPassword = "Changed-" + Guid.NewGuid().ToString("N")[..10] + "A1!";
        var changed = false;
        try
        {
            await SignInAsync(Page, email);
            await Page.GotoAsync(BaseUrl + "/account/password");
            await FillPasswordChangeAsync(Page, Password, newPassword);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Change password", Exact = true }).ClickAsync();
            changed = true;
            await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("Your password has been changed");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out", Exact = true }).ClickAsync();
            await SignInAsync(Page, email, newPassword);
            await NavigateReadyAsync(Page, "/profile");
            await Expect(Page.GetByRole(AriaRole.Main)).ToContainTextAsync("Robin Ellis");
        }
        finally
        {
            // Every browser suite shares the seeded account. Restore it even if a later assertion fails.
            if (changed)
            {
                await using var cleanupContext = await Browser.NewContextAsync(ContextOptions());
                var cleanupPage = await cleanupContext.NewPageAsync();
                await SignInAsync(cleanupPage, email, newPassword);
                await cleanupPage.GotoAsync(BaseUrl + "/account/password");
                await FillPasswordChangeAsync(cleanupPage, newPassword, Password);
                await cleanupPage.GetByRole(AriaRole.Button, new() { Name = "Change password", Exact = true }).ClickAsync();
                await Expect(cleanupPage.GetByRole(AriaRole.Main)).ToContainTextAsync("Your password has been changed");
            }
        }
    }

    [Fact]
    public async Task Member_profile_shows_only_their_own_details_at_desktop_and_phone_width()
    {
        await SignInAsync(Page, "member10@library.local");
        await Page.Locator(".primary-nav a[href='/profile']").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Your profile.", Exact = true })).ToBeVisibleAsync();
        var main = Page.GetByRole(AriaRole.Main);
        await Expect(main).ToContainTextAsync("Riley James");
        await Expect(main).ToContainTextAsync("member10@library.local");
        await Expect(main).ToContainTextAsync("Member since");
        await Expect(main).ToContainTextAsync("Membership status");
        await Expect(main).ToContainTextAsync("Borrowing limit");
        await Expect(main).ToContainTextAsync("Available to borrow");
        await Expect(main).Not.ToContainTextAsync("Jamie Chen");
        await Expect(main).Not.ToContainTextAsync("Robin Ellis");

        // A guessed query parameter cannot select another member's profile.
        await NavigateReadyAsync(Page, "/profile?memberId=2");
        await Expect(main).ToContainTextAsync("Riley James");
        await Expect(main).Not.ToContainTextAsync("Jamie Chen");
        await Page.SetViewportSizeAsync(390, 844);
        Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"));
        await ScreenshotAsync(Page, "profile-member-phone-light.png");
        var light = await BrowserAccessibility.ScanAsync(Page, Browser, "/profile (member phone light)");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Toggle light or dark theme", Exact = true }).ClickAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-theme", "dark");
        await ScreenshotAsync(Page, "profile-member-phone-dark.png");
        var dark = await BrowserAccessibility.ScanAsync(Page, Browser, "/profile (member phone dark)");
        await BrowserAccessibility.SaveAndAssertAsync(Browser, "member-profile-phone", new[] { light, dark });
    }

    private static async Task FillPasswordChangeAsync(IPage page, string currentPassword, string newPassword)
    {
        await page.GetByLabel("Current password", new() { Exact = true }).FillAsync(currentPassword);
        await page.GetByLabel("New password", new() { Exact = true }).FillAsync(newPassword);
        await page.GetByLabel("Confirm new password", new() { Exact = true }).FillAsync(newPassword);
    }

    private static async Task SignInAsync(IPage page, string email, string password = Password)
    {
        await page.GotoAsync($"{BaseUrl}/account/login");
        await page.GetByLabel("Email", new() { Exact = true }).FillAsync(email);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        try { await page.WaitForURLAsync(url => !url.Contains("/account/login", StringComparison.OrdinalIgnoreCase)); }
        catch
        {
            await ScreenshotAsync(page, "login-failure.png");
            throw new InvalidOperationException($"Sign-in did not leave the login page. URL: {page.Url}. Visible page: {await page.Locator("body").InnerTextAsync()}");
        }
        await WaitForInteractiveAsync(page);
    }

    private static async Task NavigateReadyAsync(IPage page, string path)
    {
        await page.GotoAsync(path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : BaseUrl + path);
        await WaitForInteractiveAsync(page);
    }

    private static Task WaitForInteractiveAsync(IPage page) =>
        page.WaitForFunctionAsync("window.libraryReady === true", null, new() { Timeout = 15000 });

    private async Task<string> CreateBookAsync(IPage page)
    {
        var title = "Browser test " + Guid.NewGuid().ToString("N")[..10];
        await NavigateReadyAsync(page, "/books");
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("Add book") }).ClickAsync();
        await page.GetByLabel("Title", new() { Exact = true }).FillAsync(title);
        await page.GetByLabel("Author(s)", new() { Exact = true }).FillAsync("Browser Test Author");
        await page.GetByLabel("ISBN", new() { Exact = true }).FillAsync(NewIsbn());
        await page.Locator("#book-category").FillAsync("Browser tests");
        await page.GetByLabel("Publication year", new() { Exact = true }).FillAsync("2024");
        await page.GetByLabel("Number of copies", new() { Exact = true }).FillAsync("1");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save book", Exact = true }).ClickAsync();
        await Expect(page.GetByLabel("Title", new() { Exact = true })).ToHaveCountAsync(0);
        await page.GetByLabel("Search books", new() { Exact = true }).FillAsync(title);
        await page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = title, Exact = true }).First.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = title, Exact = true })).ToBeVisibleAsync();
        // Optional cover URLs must remain optional when an existing book is edited.
        await page.GetByRole(AriaRole.Button, new() { Name = "Edit book", Exact = true }).ClickAsync();
        await page.GetByLabel("Description", new() { Exact = true }).FillAsync("Edited by the browser regression test.");
        await page.GetByLabel("Cover URL", new() { Exact = true }).FillAsync("");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save book", Exact = true }).ClickAsync();
        await Expect(page.GetByLabel("Title", new() { Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Main)).ToContainTextAsync("Edited by the browser regression test.");
        return title;
    }

    private async Task BorrowToJamieAsync(IPage page)
    {
        await page.GetByLabel("Member", new() { Exact = true }).SelectOptionAsync("2");
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Borrow copy ") }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Borrow copy ") })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Main)).ToContainTextAsync("Borrowed");
    }

    private async Task SearchLoansAsync(IPage page, string title)
    {
        await NavigateReadyAsync(page, "/loans");
        await page.GetByLabel("Search loans", new() { Exact = true }).FillAsync(title);
        await page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Expect(page.GetByTestId("loan-row").Filter(new() { HasText = title })).ToHaveCountAsync(1);
    }

    private static string NewIsbn()
    {
        var digits = $"978{RandomNumberGenerator.GetInt32(0, 1_000_000_000):D9}";
        var sum = digits.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return digits + ((10 - sum % 10) % 10);
    }

    private static async Task ScreenshotAsync(IPage page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("LIBRARY_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name), FullPage = true });
    }
}
