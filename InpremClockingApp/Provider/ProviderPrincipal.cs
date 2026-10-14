using System.Security.Claims;

namespace InpremClockingApp.Provider;

public static class ProviderPrincipal
{
    /// <summary>The audit actor for a signed-in provider user: "provider:{account id}".</summary>
    public static string Actor(this ClaimsPrincipal user) => $"provider:{user.FindFirstValue(ClaimTypes.NameIdentifier)}";
}
