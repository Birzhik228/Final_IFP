using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LibraryLending.Web;

public sealed class IdentitySessionValidator(UserManager<IdentityUser> users, IOptions<IdentityOptions> options)
{
    public async Task<bool> IsValidAsync(ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return false;
        if (users.SupportsUserSecurityStamp && principal.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType) != await users.GetSecurityStampAsync(user)) return false;
        var actualRoles = (await users.GetRolesAsync(user)).ToHashSet(StringComparer.Ordinal);
        var claimedRoles = principal.FindAll(options.Value.ClaimsIdentity.RoleClaimType).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);
        return actualRoles.SetEquals(claimedRoles);
    }
}

// Each revalidation uses a fresh Identity scope; a circuit never retains its DbContext.
public sealed class RevalidatingIdentityStateProvider(ILoggerFactory loggerFactory, IServiceScopeFactory scopes)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(5);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        cancellationToken.ThrowIfCancellationRequested();
        return await scope.ServiceProvider.GetRequiredService<IdentitySessionValidator>().IsValidAsync(authenticationState.User);
    }
}
