using System.Security.Claims;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Tenancy;
using Microsoft.AspNetCore.Authentication;
using InpremClockingApp.Helpers;
using Microsoft.AspNetCore.Identity;

namespace InpremClockingApp.Hosting;

/// <summary>
/// Fills <see cref="TenantContext"/> for every signed-in request from the <c>membership_id</c>
/// claim, after re-checking that the membership is still Active, belongs to the user, and
/// matches the <c>tenant_id</c> claim (decision D4). Otherwise the person is signed out at once
/// and the request continues anonymously (protected pages then send them to the login page).
/// <list type="bullet">
/// <item>Platform operators (SuperAdmin, no organization) are left as they are.</item>
/// <item>Provider area requests (the Provider Console, design section 5) are not organization
/// requests: no tenant context, nobody signed out here; <c>ProviderAccessFilter</c> guards them.
/// The same for console endpoints outside the area (<see cref="ProviderEndpointMetadata"/>: jobs
/// dashboard, health check).</item>
/// <item>Someone with several Active memberships who has not chosen yet is sent to
/// "Choose organization" (GET) or refused (other methods).</item>
/// </list>
/// Runs after UseAuthentication and before UseAuthorization.
/// </summary>
public sealed class TenantContextMiddleware
{
    public const string ChooseOrganizationPath = "/Account/ChooseOrganization";

    private static readonly PathString[] Exempt =
    {
        new(ChooseOrganizationPath),
        // Someone whose only pending membership is an invitation must be able to accept it.
        new("/Account/Invitations"),
        new("/Identity/Account/Logout"),
        new("/Account/Support"),
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantContextMiddleware> _logger;

    public TenantContextMiddleware(RequestDelegate next, ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TenantContext tenantContext, IMembershipService memberships, Inprem.Platform.Application.Support.SupportSessionService support)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true ||
            string.Equals(context.GetRouteValue("area") as string, SiteMap.ProviderArea, StringComparison.OrdinalIgnoreCase) ||
            context.GetEndpoint()?.Metadata.GetMetadata<ProviderEndpointMetadata>() != null ||
            // The website API (Phase 3) has its own tokens: a back-office cookie gives it no organization.
            context.GetEndpoint()?.Metadata.GetMetadata<PublicApi.PublicApiEndpointMetadata>() != null)
        {
            await _next(context);
            return;
        }

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);

        // Support session (step 1.5): a provider user working as admin of one organization until the
        // session ends. Re-checked on every request; every request is audited; billing is refused.
        var supportClaim = user.FindFirstValue(Provider.SupportSessions.SessionIdClaim);
        if (supportClaim != null)
        {
            var active = userId != null && Guid.TryParse(supportClaim, out var sessionId)
                         && int.TryParse(user.FindFirstValue(PlatformClaimTypes.TenantId), out var supportTenantId)
                ? await support.ValidateAsync(sessionId, userId, supportTenantId, context.RequestAborted)
                : null;
            if (active == null)
            {
                await SignOutAsync(context, "support session over");
                context.Response.Redirect(Provider.SupportSessions.EndedPath);
                return;
            }

            tenantContext.Set(new ActiveMembershipInfo(active.SessionId, active.ProviderUserId, active.TenantId, active.TenantName, Inprem.Platform.Domain.Memberships.MembershipRole.Admin));
            context.Items[Provider.SupportSessions.ItemKey] = active;
            var blocked = Provider.SupportSessions.IsBilling(context);
            await support.RecordRequestAsync(active, context.Request.Method, context.Request.Path + context.Request.QueryString, blocked, context.RequestAborted);
            if (blocked)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Billing is not available during a support session.");
                return;
            }

            await _next(context);
            return;
        }

        var membershipClaim = user.FindFirstValue(PlatformClaimTypes.MembershipId);

        if (membershipClaim == null)
        {
            if (user.IsInRole(IdentitySeeder.SuperAdminRole) && user.FindFirst(PlatformClaimTypes.TenantId) == null)
            {
                await _next(context);
                return;
            }

            if (Exempt.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(context);
                return;
            }

            var active = userId == null ? Array.Empty<ActiveMembershipInfo>() : await memberships.ListActiveAsync(userId, context.RequestAborted);
            if (active.Count > 1)
            {
                if (HttpMethods.IsGet(context.Request.Method))
                {
                    var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
                    context.Response.Redirect($"{ChooseOrganizationPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                }
                return;
            }

            await SignOutAsync(context, "no membership on the sign-in");
            await _next(context);
            return;
        }

        var membership = userId != null && Guid.TryParse(membershipClaim, out var membershipId)
            ? await memberships.ValidateAsync(userId, membershipId, context.RequestAborted)
            : null;

        if (membership == null || user.FindFirstValue(PlatformClaimTypes.TenantId) != membership.TenantId.ToString())
        {
            await SignOutAsync(context, "membership no longer valid");
            await _next(context);
            return;
        }

        tenantContext.Set(membership);
        await _next(context);
    }

    private async Task SignOutAsync(HttpContext context, string reason)
    {
        _logger.LogInformation("Signing out user {UserId}: {Reason}.", context.User.FindFirstValue(ClaimTypes.NameIdentifier), reason);
        await context.SignOutAsync(IdentityConstants.ApplicationScheme);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
    }
}
