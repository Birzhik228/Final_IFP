using FluentAssertions;
using LibraryLending.Application;
using LibraryLending.Domain;
using LibraryLending.Infrastructure.Identity;
using LibraryLending.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibraryLending.Tests.Integration;

public sealed class MemberProfileTests : LibraryTestBase
{
    private async Task<AccountProfileDto?> Profile(Actor actor)
    {
        using var scope = Factory.Services.CreateScope();
        var service = new ProfileService(scope.ServiceProvider.GetRequiredService<IDbContextFactory<LibraryDbContext>>(), Factory.Clock);
        return await service.GetOwnAsync(actor);
    }

    [Fact]
    public async Task Profile_resolves_own_identity_link_and_ignores_forged_member_id_and_librarian_flag()
    {
        var own = await Database(db => db.Members.SingleAsync(x => x.IdentityUserId == MemberActor.UserId));
        var other = await Database(db => db.Members.FirstAsync(x => x.Id != own.Id));
        var book = await NewBook();
        (await Use(s => s.BorrowAsync(Librarian, book.Copies[0].Id, own.Id))).IsSuccess.Should().BeTrue();

        var profile = await Profile(new Actor(MemberActor.UserId, true, other.Id));

        profile.Should().NotBeNull();
        profile!.MemberId.Should().Be(own.Id);
        profile.Name.Should().Be(own.Name).And.NotBe(other.Name);
        profile.Email.Should().Be(own.Email).And.NotBe(other.Email);
        profile.Phone.Should().Be(own.Phone);
        profile.Role.Should().Be("Member");
        profile.Status.Should().Be(own.Status);
        profile.JoinedAtUtc.Should().Be(own.JoinedAtUtc);
        profile.ActiveLoans.Should().Be(await Database(db => db.Loans.CountAsync(x => x.MemberId == own.Id && x.ReturnedAtUtc == null)));
        profile.RemainingLoans.Should().Be(own.BorrowingLimit - profile.ActiveLoans);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown-identity")]
    public async Task Anonymous_or_missing_identity_cannot_read_a_member_profile(string userId)
    {
        (await Profile(new Actor(userId, true, EligibleMemberId))).Should().BeNull();
    }

    [Fact]
    public async Task Removed_role_or_active_identity_lockout_denies_profile_access()
    {
        await Database(async db =>
        {
            var user = await db.Users.SingleAsync(x => x.Id == MemberActor.UserId);
            user.LockoutEnabled = true;
            user.LockoutEnd = Factory.Clock.GetUtcNow().AddHours(1);
            return await db.SaveChangesAsync();
        });
        (await Profile(MemberActor)).Should().BeNull();

        Factory.Clock.Advance(TimeSpan.FromHours(2));
        (await Profile(MemberActor)).Should().NotBeNull();

        await Database(db => db.UserRoles.Where(x => x.UserId == MemberActor.UserId).ExecuteDeleteAsync());
        (await Profile(MemberActor)).Should().BeNull();
    }

    [Fact]
    public async Task Librarian_profile_has_staff_details_without_inventing_member_fields()
    {
        var profile = await Profile(new Actor(Librarian.UserId, false, EligibleMemberId));
        profile.Should().NotBeNull();
        profile!.Role.Should().Be("Librarian");
        profile.Name.Should().Be("Library librarian");
        profile.Email.Should().Be("librarian@library.local");
        profile.MemberId.Should().BeNull();
        profile.JoinedAtUtc.Should().BeNull();
        profile.BorrowingLimit.Should().BeNull();
        profile.ActiveLoans.Should().BeNull();
        profile.RemainingLoans.Should().BeNull();

        await Database(async db =>
        {
            db.UserClaims.Add(new IdentityUserClaim<string> { UserId = Librarian.UserId, ClaimType = "display_name", ClaimValue = "Demo Librarian" });
            return await db.SaveChangesAsync();
        });
        (await Profile(Librarian))!.Name.Should().Be("Demo Librarian");
    }

    [Fact]
    public async Task Unknown_joining_date_stays_unknown_and_suspended_member_can_read_their_status()
    {
        await Database(async db =>
        {
            var member = await db.Members.SingleAsync(x => x.Id == EligibleMemberId);
            member.JoinedAtUtc = null;
            member.Status = MembershipStatus.Suspended;
            return await db.SaveChangesAsync();
        });
        var profile = await Profile(MemberActor);
        profile.Should().NotBeNull();
        profile!.JoinedAtUtc.Should().BeNull();
        profile.Status.Should().Be(MembershipStatus.Suspended);
    }

    [Fact]
    public async Task Creating_member_records_current_joining_date_and_editing_preserves_it()
    {
        var expectedDate = Factory.Clock.GetUtcNow().UtcDateTime;
        var memberId = await NewMember();
        var member = await Database(db => db.Members.SingleAsync(x => x.Id == memberId));
        member.JoinedAtUtc.Should().Be(expectedDate);

        Factory.Clock.Advance(TimeSpan.FromDays(30));
        (await Use(s => s.SaveMemberAsync(Librarian, memberId,
            new MemberInput("Updated member name", member.Email, member.Phone, member.Status, member.BorrowingLimit)))).IsSuccess.Should().BeTrue();
        (await Database(db => db.Members.SingleAsync(x => x.Id == memberId))).JoinedAtUtc.Should().Be(expectedDate);
    }
}

public sealed class ProfileMigrationTests
{
    [Fact]
    public async Task Joining_date_migration_preserves_existing_member_without_inventing_a_date()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new LibraryDbContext(new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261114140755_InitialCreate");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO Members (Id, Name, Email, Phone, Status, BorrowingLimit, Version)
            VALUES (1, 'Historical Member', 'historical@example.test', '', 'Active', 5, 'original-version')
            """);

        await migrator.MigrateAsync();

        var member = await db.Members.SingleAsync();
        member.Name.Should().Be("Historical Member");
        member.BorrowingLimit.Should().Be(5);
        member.Version.Should().Be("original-version");
        member.JoinedAtUtc.Should().BeNull();
    }
}
