using System.Security.Claims;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Application.Tenancy;

namespace InpremClockingApp.Hosting;

/// <summary>
/// The texted sign-in code for organizations that switch it on (Phase 5 step 5.2; off by default, so
/// the approved app behaves exactly as before). After the password, an admin of such an organization
/// is sent to "Enter the code we texted you" until a code is approved; the proof is a claim tied to
/// the membership, so choosing another organization that asks for codes asks again. Support sessions
/// (provider users, already two-factor) are not asked. Runs after <see cref="TenantContextMiddleware"/>.
/// </summary>
public sealed class SmsSignInGate
{
    public const string ClaimType = "sms_sign_in";
    public const string CodePath = "/Account/SmsSignIn";

    private static readonly PathString[] Exempt =
    {
        new(CodePath), new("/Identity/Account/Logout"), new(TenantContextMiddleware.ChooseOrganizationPath),
        new("/api/public"), new("/webhooks"),
    };

    private readonly RequestDelegate _next;

    public SmsSignInGate(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, MessagingSettingsService settings)
    {
        if (tenant.TenantId is not int tenantId
            || context.Items.ContainsKey(Provider.SupportSessions.ItemKey)
            || Exempt.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))
            || !await settings.SmsSignInRequiredAsync(tenantId, context.RequestAborted))
        {
            await _next(context);
            return;
        }

        var membership = context.User.FindFirstValue(PlatformClaimTypes.MembershipId);
        if (membership != null && context.User.HasClaim(ClaimType, membership))
        {
            await _next(context);
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method))
        {
            var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            context.Response.Redirect($"{CodePath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
            return;
        }
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}

public static class SmsSignInGateExtensions
{
    public static IApplicationBuilder UseSmsSignInGate(this IApplicationBuilder app) => app.UseMiddleware<SmsSignInGate>();
}
