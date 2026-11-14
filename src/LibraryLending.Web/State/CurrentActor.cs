using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using LibraryLending.Application;
namespace LibraryLending.Web.State;

public sealed class CurrentActor(AuthenticationStateProvider authentication)
{
    public async Task<Actor> GetAsync()
    {
        var principal = (await authentication.GetAuthenticationStateAsync()).User;
        return new Actor(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", principal.IsInRole("Librarian"),
            int.TryParse(principal.FindFirstValue("member_id"), out var memberId) ? memberId : null);
    }
}
