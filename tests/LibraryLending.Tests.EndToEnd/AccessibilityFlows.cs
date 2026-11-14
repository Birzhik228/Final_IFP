using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.Playwright.Xunit;
using Xunit;

namespace LibraryLending.Tests.EndToEnd;

[Collection("Library browser workflows")]
public sealed class AccessibilityFlows : PageTest
{
    private static readonly string BaseUrl = (Environment.GetEnvironmentVariable("LIBRARY_BASE_URL") ?? "http://localhost:5180").TrimEnd('/');

    public override BrowserNewContextOptions ContextOptions() => new() { ViewportSize = new() { Width = 1440, Height = 1000 }, Locale = "en-US" };

    [Theory]
    [InlineData("librarian@library.local", "light")]
    [InlineData("librarian@library.local", "dark")]
    [InlineData("member1@library.local", "light")]
    [InlineData("member1@library.local", "dark")]
    public async Task Key_pages_have_no_automated_WCAG_A_AA_violations(string email, string theme)
    {
        await Page.GotoAsync(BaseUrl + "/account/login");
        await SetTheme(theme);
        var reports = new List<JsonElement>();
        reports.Add(await Scan("login"));
        await SignIn(email);
        var routes = email.StartsWith("librarian", StringComparison.Ordinal)
            ? new[] { "/", "/books", "/books/1", "/loans", "/members", "/reservations", "/overdue", "/notifications", "/account/password" }
            : new[] { "/", "/books", "/books/1", "/loans", "/reservations", "/notifications", "/account/password" };
        foreach (var route in routes)
        {
            await Page.GotoAsync(BaseUrl + route);
            if (!route.StartsWith("/account", StringComparison.Ordinal)) await WaitForInteractive();
            reports.Add(await Scan(route));
        }
        await BrowserAccessibility.SaveAndAssertAsync(Browser, $"{email.Split('@')[0]}-{theme}", reports);
    }

    [Fact]
    public async Task Confirmation_dialog_traps_focus_and_escape_returns_to_trigger()
    {
        await Page.GotoAsync(BaseUrl + "/account/login");
        await SignIn("librarian@library.local");
        await Page.GotoAsync(BaseUrl + "/books/2");
        await WaitForInteractive();
        var trigger = Page.GetByRole(AriaRole.Button, new() { Name = "Mark lost", Exact = true }).First;
        await trigger.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Mark lost", Exact = true })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(trigger).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Mobile_navigation_is_keyboard_operable_and_closed_links_are_hidden()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync(BaseUrl + "/account/login");
        await SignIn("member1@library.local");
        await CheckMobileNavigation();
        // Account forms use static rendering and must not depend on Blazor event handlers.
        await Page.GotoAsync(BaseUrl + "/account/password");
        await CheckMobileNavigation();
    }

    private async Task CheckMobileNavigation()
    {
        var sidebar = Page.Locator(".sidebar");
        await Expect(sidebar).ToBeHiddenAsync();
        var toggle = Page.GetByRole(AriaRole.Button, new() { Name = "Toggle navigation", Exact = true });
        await toggle.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(sidebar).ToBeVisibleAsync();
        await Expect(sidebar.GetByRole(AriaRole.Link, new() { Name = "BookBridge home", Exact = true })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(toggle).ToBeFocusedAsync();
        await Expect(sidebar).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(768, 1024)]
    [InlineData(844, 390)]
    public async Task Tablet_and_landscape_layouts_keep_navigation_and_content_reachable(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(BaseUrl + "/account/login");
        await SignIn("librarian@library.local");
        foreach (var route in new[] { "/", "/books", "/members", "/overdue" })
        {
            await Page.GotoAsync(BaseUrl + route);
            await WaitForInteractive();
            Assert.True(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"), route);
        }
        var notifications = Page.Locator(".sidebar").GetByRole(AriaRole.Link, new() { Name = "Notifications", Exact = false });
        await notifications.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page).ToHaveURLAsync(BaseUrl + "/notifications");
    }

    private Task<JsonElement> Scan(string route) => BrowserAccessibility.ScanAsync(Page, Browser, route);
    private Task SetTheme(string theme) => Page.EvaluateAsync("theme => {localStorage.setItem('library-theme',theme);document.documentElement.dataset.theme=theme;}", theme);
    private async Task SignIn(string email)
    {
        await Page.GetByLabel("Email", new() { Exact = true }).FillAsync(email);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync("LibraryDemo1!");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync(BaseUrl + "/");
        await WaitForInteractive();
    }
    private Task WaitForInteractive() => Page.WaitForFunctionAsync("window.libraryReady === true", null, new() { Timeout = 15000 });
}

internal static class BrowserAccessibility
{
    internal static async Task<JsonElement> ScanAsync(IPage page, IBrowser browser, string route)
    {
        await page.AddScriptTagAsync(new() { Path = Path.Combine(AppContext.BaseDirectory, "Assets", "axe.min.js") });
        return await page.EvaluateAsync<JsonElement>("""
            async metadata => {
                const result = await axe.run(document, { runOnly: {type: 'tag', values: ['wcag2a','wcag2aa','wcag21aa']} });
                return { ...metadata, theme: document.documentElement.dataset.theme, timestamp: result.timestamp,
                    userAgent: navigator.userAgent, viewport: { width: innerWidth, height: innerHeight },
                    axeVersion: result.testEngine.version, violations: result.violations, incomplete: result.incomplete, passes: result.passes.length };
            }
            """, new { route, browser = browser.BrowserType.Name, browserVersion = browser.Version });
    }
    internal static async Task SaveAndAssertAsync(IBrowser browser, string scenario, IEnumerable<JsonElement> reports)
    {
        var directory = Environment.GetEnvironmentVariable("LIBRARY_A11Y_RESULTS") ?? Path.Combine("TestResults", "accessibility");
        Directory.CreateDirectory(directory);
        var filename = $"{browser.BrowserType.Name}-{scenario}.json";
        await File.WriteAllTextAsync(Path.Combine(directory, filename), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        var failures = reports.SelectMany(r => r.GetProperty("violations").EnumerateArray().Select(v => $"{r.GetProperty("route").GetString()}: {v.GetProperty("id").GetString()} — {v.GetProperty("help").GetString()}"));
        Assert.Empty(failures);
    }
}
