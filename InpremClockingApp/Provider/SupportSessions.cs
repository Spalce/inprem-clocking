using Inprem.Platform.Application.Support;

namespace InpremClockingApp.Provider;

/// <summary>Support sessions on the App site (Phase 1 step 1.5).</summary>
public static class SupportSessions
{
    /// <summary>Claim on a support session's App-site sign-in.</summary>
    public const string SessionIdClaim = "support_session_id";

    /// <summary>HttpContext.Items key for the request's <see cref="ActiveSupport"/> (banner, pages).</summary>
    public const string ItemKey = "Inprem.SupportSession";

    public const string EndPath = "/Account/Support/End";
    public const string EndedPath = "/Account/Support/Ended";

    /// <summary>
    /// Billing is not available during a support session: the owner's Billing area (Phase 2), the
    /// old billing pages and invoice downloads. Requests there get 403 and are audited as blocked.
    /// </summary>
    private static readonly PathString[] BillingPaths = { new("/Billing"), new("/Platform"), new("/api/invoices"), new("/api/billing") };

    public static bool IsBilling(HttpContext context) =>
        string.Equals(context.GetRouteValue("area") as string, "Billing", StringComparison.OrdinalIgnoreCase)
        || BillingPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    public static ActiveSupport? Current(HttpContext context) => context.Items[ItemKey] as ActiveSupport;
}
