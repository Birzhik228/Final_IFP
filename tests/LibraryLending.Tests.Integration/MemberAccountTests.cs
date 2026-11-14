using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using LibraryLending.Application;
using LibraryLending.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibraryLending.Tests.Integration;

public sealed class MemberAccountTests : LibraryTestBase
{
    private async Task<Result<AccountCredentials>> Provision(Actor actor, int memberId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMemberAccountService>().ProvisionAsync(actor, memberId);
    }

    [Fact]
    public async Task Member_cannot_create_accounts()
    {
        var memberId = await NewMember();
        var result = await Provision(MemberActor, memberId);
        result.IsSuccess.Should().BeFalse();
        (await Database(db => db.Members.SingleAsync(x => x.Id == memberId))).IdentityUserId.Should().BeNull();
    }

    [Fact]
    public async Task Provisioned_member_can_sign_in_with_generated_credentials_and_has_member_role()
    {
        var memberId = await NewMember();
        var result = await Provision(Librarian, memberId);
        result.IsSuccess.Should().BeTrue(result.Error);
        var credentials = result.Value!;
        credentials.TemporaryPassword.Length.Should().BeGreaterThanOrEqualTo(32);
        credentials.ToString().Should().NotContain(credentials.TemporaryPassword);
        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByEmailAsync(credentials.Email);
            user.Should().NotBeNull();
            (await users.IsInRoleAsync(user!, "Member")).Should().BeTrue();
            (await users.IsInRoleAsync(user!, "Librarian")).Should().BeFalse();
            (await Database(db => db.Members.SingleAsync(x => x.Id == memberId))).IdentityUserId.Should().Be(user!.Id);
        }

        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var page = await client.GetStringAsync("/account/login");
        var input = Regex.Matches(page, "<input\\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>()
            .Select(x => x.Value).First(x => x.Contains("__RequestVerificationToken", StringComparison.Ordinal));
        var token = WebUtility.HtmlDecode(Regex.Match(input, "value=\"([^\"]+)\"").Groups[1].Value);
        var login = await client.PostAsync("/account/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = credentials.Email,
            ["password"] = credentials.TemporaryPassword,
            ["returnUrl"] = "/",
            ["__RequestVerificationToken"] = token
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.ToString().Should().Be("/");
        (await client.GetAsync("/exports/overdue.csv")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Existing_account_cannot_be_provisioned_twice()
    {
        var memberId = await NewMember();
        var first = await Provision(Librarian, memberId);
        first.IsSuccess.Should().BeTrue(first.Error);
        var second = await Provision(Librarian, memberId);
        second.IsSuccess.Should().BeFalse();
        (await Database(db => db.Users.CountAsync(x => x.Email == first.Value!.Email))).Should().Be(1);
    }

    [Theory]
    [InlineData(MembershipStatus.Suspended)]
    [InlineData(MembershipStatus.Expired)]
    public async Task Inactive_member_cannot_receive_account(MembershipStatus status)
    {
        var memberId = await NewMember();
        var member = (await Use(s => s.GetMembersAsync(Librarian))).Single(x => x.Id == memberId);
        (await Use(s => s.SaveMemberAsync(Librarian, memberId, new MemberInput(member.Name, member.Email, member.Phone, status)))).IsSuccess.Should().BeTrue();
        (await Provision(Librarian, memberId)).IsSuccess.Should().BeFalse();
        (await Database(db => db.Users.AnyAsync(x => x.Email == member.Email))).Should().BeFalse();
    }

    [Fact]
    public async Task Existing_identity_email_does_not_create_partial_account_or_member_link()
    {
        var memberId = await NewMember();
        var member = (await Use(s => s.GetMembersAsync(Librarian))).Single(x => x.Id == memberId);
        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            (await users.CreateAsync(new IdentityUser { UserName = member.Email, Email = member.Email }, "ExistingIdentity1!")).Succeeded.Should().BeTrue();
        }
        (await Provision(Librarian, memberId)).IsSuccess.Should().BeFalse();
        (await Database(db => db.Members.SingleAsync(x => x.Id == memberId))).IdentityUserId.Should().BeNull();
        (await Database(db => db.Users.CountAsync(x => x.Email == member.Email))).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_provisioning_creates_exactly_one_login()
    {
        var memberId = await NewMember();
        var results = await Task.WhenAll(Task.Run(() => Provision(Librarian, memberId)), Task.Run(() => Provision(Librarian, memberId)));
        results.Count(x => x.IsSuccess).Should().Be(1);
        var email = results.Single(x => x.IsSuccess).Value!.Email;
        (await Database(db => db.Users.CountAsync(x => x.Email == email))).Should().Be(1);
    }
}
