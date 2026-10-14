using System.Security.Claims;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Provider;
using Microsoft.AspNetCore.Authorization;

namespace InpremClockingApp.Provider;

/// <summary>
/// Provider Console authorization (Phase 1 step 1.1, design 5.5). The roles nest: an Owner can do
/// everything a Billing user can, and both can do everything Support can.
/// <list type="bullet">
/// <item><see cref="Support"/> - any active provider user: dashboard, organizations, plans (read).</item>
/// <item><see cref="Billing"/> - Owner or Billing: billing and webhook events.</item>
/// <item><see cref="Owner"/> - Owner only: provider users, settings, audit log, notifications, jobs.</item>
/// </list>
/// The policies check the role only. Two-factor, the security stamp and session limits are
/// checked by <see cref="ProviderAccessFilter"/> and <see cref="ProviderSession"/>.
/// </summary>
public static class ProviderPolicies
{
    public const string Support = "ProviderSupport";
    public const string Billing = "ProviderBilling";
    public const string Owner = "ProviderOwner";

    public static readonly IReadOnlyDictionary<string, ProviderRole[]> Roles = new Dictionary<string, ProviderRole[]>
    {
        [Support] = new[] { ProviderRole.Owner, ProviderRole.Billing, ProviderRole.Support },
        [Billing] = new[] { ProviderRole.Owner, ProviderRole.Billing },
        [Owner] = new[] { ProviderRole.Owner },
    };

    public static void Add(AuthorizationOptions options)
    {
        foreach (var (name, roles) in Roles)
        {
            options.AddPolicy(name, policy => policy.RequireAuthenticatedUser().AddRequirements(new ProviderRoleRequirement(roles)));
        }
    }
}

public sealed class ProviderRoleRequirement : IAuthorizationRequirement
{
    public ProviderRoleRequirement(IReadOnlyCollection<ProviderRole> roles) => Roles = roles;

    public IReadOnlyCollection<ProviderRole> Roles { get; }
}

/// <summary>Succeeds when the signed-in account is a provider user (Invited or Active) with one of the roles.</summary>
public sealed class ProviderRoleHandler : AuthorizationHandler<ProviderRoleRequirement>
{
    private readonly ProviderSecurityService _provider;

    public ProviderRoleHandler(ProviderSecurityService provider) => _provider = provider;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ProviderRoleRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return;
        var user = await _provider.FindUsableProviderUserAsync(userId);
        if (user != null && requirement.Roles.Contains(user.Role)) context.Succeed(requirement);
    }
}
