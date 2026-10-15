using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Tenancy;

namespace InpremClockingApp.Hosting;

/// <summary>
/// Read-only mode for organizations without full access (Phase 2 step 2.7; C3-3 and C3-4):
/// Suspended, Expired, Closed, a trial past its end, or Canceled past the paid period.
/// <list type="bullet">
/// <item><b>Saving is refused</b>: every POST, PUT, PATCH and DELETE of the organization's app,
/// except the report and export endpoints (<c>/api/reports</c>, which are POSTs but only read)
/// and the pages that must keep working (sign-in, own account, choosing an organization,
/// invitations, support sessions, billing).</item>
/// <item><b>The kiosk pauses</b>: its pages and its clock-in/out and break actions (which are GETs)
/// are answered with a friendly page or message.</item>
/// <item>Everything else (back office pages, reports, exports) works as before.</item>
/// </list>
/// The approved pages are not edited; this runs after <see cref="TenantContextMiddleware"/>.
/// Organizations with full access pass straight through.
/// </summary>
public sealed class BillingEnforcementMiddleware
{
    public const string ReadOnlyPath = "/Account/ReadOnly";

    /// <summary>The kiosk: pages and actions (clock in/out, breaks).</summary>
    private static readonly PathString[] Kiosk =
    {
        new("/StaffAttendance"), new("/VolunteerAttendance"), new("/staff-clockin"), new("/volunteer-clockin"), new("/api/controls"),
    };

    /// <summary>POST endpoints that only read: reports and exports.</summary>
    private static readonly PathString[] ReadOnlyPosts = { new("/api/reports") };

    /// <summary>Always allowed: signing in and out, the person's own account, organizations, support, billing.</summary>
    private static readonly PathString[] Always =
    {
        new("/Identity/Account/Login"), new("/Identity/Account/LoginWith2fa"), new("/Identity/Account/LoginWithRecoveryCode"),
        new("/Identity/Account/Logout"), new("/Identity/Account/ForgotPassword"), new("/Identity/Account/ResetPassword"),
        new("/Identity/Account/Manage"), new("/Account"), new("/Billing"), new("/api/billing"), new("/webhooks"),
    };

    private readonly RequestDelegate _next;

    public BillingEnforcementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenant, IEntitlementService entitlements)
    {
        var path = context.Request.Path;
        if (tenant.TenantId is not int tenantId || Starts(path, Always))
        {
            await _next(context);
            return;
        }

        var kiosk = Starts(path, Kiosk);
        var write = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method);
        if (!kiosk && (!write || Starts(path, ReadOnlyPosts)))
        {
            await _next(context);
            return;
        }

        var access = await entitlements.GetAsync(tenantId, context.RequestAborted);
        if (kiosk ? access.CanUseKiosk : access.CanWrite)
        {
            await _next(context);
            return;
        }

        var message = kiosk
            ? $"The kiosk is paused. {access.ReadOnlyReason} Please speak to your organization's admin."
            : $"{access.ReadOnlyReason} Your organization is read-only: reports and exports still work, but changes cannot be saved.";

        if (IsApi(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { message, readOnly = true }, context.RequestAborted);
            return;
        }

        // Pages: the explanation page (a GET, so the browser does not repeat the refused form).
        context.Response.Redirect($"{ReadOnlyPath}?kiosk={(kiosk ? "true" : "false")}", permanent: false);
    }

    private static bool Starts(PathString path, PathString[] prefixes) =>
        prefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsApi(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        || context.Request.Headers.XRequestedWith == "XMLHttpRequest"
        || context.Request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);
}

public static class BillingEnforcementHosting
{
    /// <summary>After UseTenantContext (needs the organization), before UseAuthorization.</summary>
    public static IApplicationBuilder UseBillingEnforcement(this IApplicationBuilder app) => app.UseMiddleware<BillingEnforcementMiddleware>();
}
