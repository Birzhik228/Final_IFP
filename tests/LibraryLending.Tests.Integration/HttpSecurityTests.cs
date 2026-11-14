using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibraryLending.Tests.Integration;

public sealed class HttpSecurityTests : LibraryTestBase
{
    private HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    [Fact]
    public async Task Health_endpoint_responds_without_authentication()
    {
        using var client = Client();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("healthy");
    }

    [Fact]
    public async Task Anonymous_user_cannot_export_member_overdue_data()
    {
        using var client = Client();
        var response = await client.GetAsync("/exports/overdue.csv");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/account/login");
    }

    [Fact]
    public async Task Login_requires_antiforgery_token()
    {
        using var client = Client();
        var response = await client.PostAsync("/account/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["email"] = "librarian@library.local", ["password"] = "LibraryDemo1!" }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Librarian_can_login_and_export_csv_with_real_identity_cookie()
    {
        using var client = Client();
        await Login(client, "librarian@library.local");
        var response = await client.GetAsync("/exports/overdue.csv");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("Days overdue").And.Contain("Alex Morgan");
    }

    [Fact]
    public async Task Member_cookie_does_not_grant_librarian_export_access()
    {
        using var client = Client();
        await Login(client, "member2@library.local");
        var response = await client.GetAsync("/exports/overdue.csv");
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/account/login");
    }

    [Fact]
    public async Task Login_rejects_external_return_url()
    {
        using var client = Client();
        var token = await Token(client);
        var response = await client.PostAsync("/account/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "librarian@library.local",
            ["password"] = "LibraryDemo1!",
            ["returnUrl"] = "https://example.com/steal",
            ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");
    }

    [Fact]
    public async Task Authenticated_member_can_change_password_using_current_password_and_antiforgery()
    {
        using var client = Client();
        await Login(client, "member2@library.local");
        var token = await Token(client, "/account/password");
        var response = await client.PostAsync("/account/change-password", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["currentPassword"] = "LibraryDemo1!",
            ["newPassword"] = "ChangedPassword2!",
            ["confirmPassword"] = "ChangedPassword2!",
            ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("success=");
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = (await users.FindByEmailAsync("member2@library.local"))!;
        (await users.CheckPasswordAsync(user, "ChangedPassword2!")).Should().BeTrue();
        (await users.CheckPasswordAsync(user, "LibraryDemo1!")).Should().BeFalse();
    }

    [Fact]
    public async Task Wrong_current_password_does_not_change_account_password()
    {
        using var client = Client();
        await Login(client, "member2@library.local");
        var token = await Token(client, "/account/password");
        var response = await client.PostAsync("/account/change-password", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["currentPassword"] = "WrongPassword1!",
            ["newPassword"] = "ChangedPassword2!",
            ["confirmPassword"] = "ChangedPassword2!",
            ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("error=");
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = (await users.FindByEmailAsync("member2@library.local"))!;
        (await users.CheckPasswordAsync(user, "LibraryDemo1!")).Should().BeTrue();
        (await users.CheckPasswordAsync(user, "ChangedPassword2!")).Should().BeFalse();
    }

    [Fact]
    public async Task Password_change_without_antiforgery_token_is_rejected()
    {
        using var client = Client();
        await Login(client, "member2@library.local");
        var response = await client.PostAsync("/account/change-password", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["currentPassword"] = "LibraryDemo1!",
            ["newPassword"] = "ChangedPassword2!",
            ["confirmPassword"] = "ChangedPassword2!"
        }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<string> Token(HttpClient client, string path = "/account/login")
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var input = Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase)
            .Cast<Match>().Select(x => x.Value).First(x => x.Contains("__RequestVerificationToken", StringComparison.Ordinal));
        return WebUtility.HtmlDecode(Regex.Match(input, "value=\"([^\"]+)\"").Groups[1].Value);
    }

    private static async Task Login(HttpClient client, string email)
    {
        var token = await Token(client);
        var response = await client.PostAsync("/account/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = email,
            ["password"] = "LibraryDemo1!",
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = token
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");
    }
}
