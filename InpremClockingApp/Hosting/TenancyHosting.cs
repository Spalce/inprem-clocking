using Inprem.Platform.Application;
using Inprem.Platform.Application.Tenancy;
using Microsoft.AspNetCore.Identity;

namespace InpremClockingApp.Hosting;

/// <summary>Tenant context and memberships (Phase 0 step 0.3).</summary>
public static class TenancyHosting
{
    public static WebApplicationBuilder AddTenancy(this WebApplicationBuilder builder)
    {
        builder.Services.AddPlatformApplication();

        // Identity rebuilds the principal from the claims factory when it re-validates the
        // security stamp (every 30 minutes by default). For someone who picked an organization on
        // "Choose organization" the factory adds no organization, so carry the chosen one over;
        // TenantContextMiddleware still re-checks it on every request.
        builder.Services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.OnRefreshingPrincipal = context =>
            {
                var newIdentity = context.NewPrincipal?.Identities.FirstOrDefault();
                if (newIdentity != null && newIdentity.FindFirst(PlatformClaimTypes.MembershipId) == null)
                {
                    foreach (var type in new[] { PlatformClaimTypes.TenantId, PlatformClaimTypes.MembershipId })
                    {
                        var claim = context.CurrentPrincipal?.FindFirst(type);
                        if (claim != null && newIdentity.FindFirst(type) == null) newIdentity.AddClaim(new System.Security.Claims.Claim(type, claim.Value));
                    }
                }

                // Support sessions (step 1.5): keep the session claim and the Admin role it grants;
                // the middleware still re-checks the session on every request.
                if (newIdentity != null && context.CurrentPrincipal?.FindFirst(Provider.SupportSessions.SessionIdClaim) is { } supportClaim
                    && newIdentity.FindFirst(Provider.SupportSessions.SessionIdClaim) == null)
                {
                    newIdentity.AddClaim(new System.Security.Claims.Claim(supportClaim.Type, supportClaim.Value));
                    if (!newIdentity.HasClaim(System.Security.Claims.ClaimTypes.Role, Helpers.IdentitySeeder.AdminRole))
                        newIdentity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, Helpers.IdentitySeeder.AdminRole));
                }

                // Provider sessions (step 1.1): keep the start, last-seen and two-factor times (and
                // the two-factor proof) when Identity rebuilds the principal, so a refresh can
                // neither end the session early nor extend it.
                if (newIdentity != null)
                {
                    foreach (var type in Provider.ProviderSession.Claims)
                    {
                        if (newIdentity.FindFirst(type) != null) continue;
                        foreach (var claim in context.CurrentPrincipal?.FindAll(type) ?? Enumerable.Empty<System.Security.Claims.Claim>())
                            newIdentity.AddClaim(new System.Security.Claims.Claim(type, claim.Value));
                    }
                }
                return Task.CompletedTask;
            };
        });

        return builder;
    }

    /// <summary>Between UseAuthentication and UseAuthorization.</summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder app) => app.UseMiddleware<TenantContextMiddleware>();
}
