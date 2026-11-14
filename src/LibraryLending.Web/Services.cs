using System.Security.Claims;
using LibraryLending.Application;
using LibraryLending.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace LibraryLending.Web;

[Authorize]
public sealed class NotificationsHub : Hub { }
public sealed class SignalRNotificationPublisher(IHubContext<NotificationsHub> hub) : INotificationPublisher
{ public Task PublishAsync(string userId, CancellationToken ct = default) => hub.Clients.User(userId).SendAsync("LibraryChanged", cancellationToken: ct); }
public sealed class LibraryClaimsFactory(UserManager<IdentityUser> users, RoleManager<IdentityRole> roles, IOptions<IdentityOptions> options, LibraryDbContext db) : UserClaimsPrincipalFactory<IdentityUser, IdentityRole>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(IdentityUser user)
    { var identity = await base.GenerateClaimsAsync(user); var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(x => x.IdentityUserId == user.Id); if (member is not null) identity.AddClaim(new Claim("member_id", member.Id.ToString())); return identity; }
}
public sealed class DeadlineWorker(IServiceScopeFactory scopes, LibraryOptions options, ILogger<DeadlineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.WorkerIntervalSeconds)));
        do { try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<ILibraryService>().ProcessDeadlinesAsync(stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; } catch (Exception e) { logger.LogError(e, "Deadline sweep failed; next sweep will retry."); } }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
