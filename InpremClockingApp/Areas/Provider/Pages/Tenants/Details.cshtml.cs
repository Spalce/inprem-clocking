using Inprem.Platform.Application.Organizations;
using InpremClockingApp.Provider;
using InpremClockingApp.Provider.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>One organization, read-only, in tabs. Admin row actions (resend, reset) are buttons here.</summary>
public class DetailsModel : PageModel
{
    public static readonly (string Key, string Title)[] Tabs =
    {
        ("overview", "Overview"), ("subscription", "Subscription"), ("invoices", "Invoices and payments"), ("admins", "Admins"),
        ("kiosk", "Kiosk"), ("messages", "Messages"), ("audit", "Audit"), ("notes", "Notes"),
    };

    private readonly OrganizationService _organizations;
    private readonly IAuthorizationService _authorization;
    private readonly Inprem.Platform.Application.Support.SupportSessionService _support;
    private readonly Inprem.Platform.Application.Memberships.IClock _clock;
    private readonly Inprem.Platform.Application.Billing.BillingAccountService _billing;

    public DetailsModel(OrganizationService organizations, IAuthorizationService authorization,
        Inprem.Platform.Application.Support.SupportSessionService support, Inprem.Platform.Application.Memberships.IClock clock,
        Inprem.Platform.Application.Billing.BillingAccountService billing)
    {
        _organizations = organizations;
        _authorization = authorization;
        _support = support;
        _clock = clock;
        _billing = billing;
    }

    /// <summary>The organization's billing (Subscription and payments tabs, step 2.8).</summary>
    public Inprem.Platform.Application.Billing.BillingOverview? Billing { get; private set; }

    /// <summary>Owners and Billing users change subscriptions.</summary>
    public bool CanBill { get; private set; }

    /// <summary>Support sessions still running in this organization.</summary>
    public IReadOnlyList<Inprem.Platform.Domain.Support.SupportSession> ActiveSupport { get; private set; } = Array.Empty<Inprem.Platform.Domain.Support.SupportSession>();
    public bool CanSupport { get; private set; }

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "overview";
    public OrganizationDetail Organization { get; private set; } = null!;
    public bool IsOwner { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var detail = await _organizations.GetAsync(id, HttpContext.RequestAborted);
        if (detail == null) return NotFound();
        Organization = detail;
        if (!Tabs.Any(t => t.Key == Tab)) Tab = "overview";
        IsOwner = (await _authorization.AuthorizeAsync(User, ProviderPolicies.Owner)).Succeeded;
        CanSupport = (await _authorization.AuthorizeAsync(User, ProviderPolicies.SupportSessions)).Succeeded;
        CanBill = (await _authorization.AuthorizeAsync(User, ProviderPolicies.Billing)).Succeeded;
        if (Tab is "subscription" or "invoices") Billing = await _billing.GetAsync(id, HttpContext.RequestAborted);
        ActiveSupport = (await _support.ListForTenantAsync(id, HttpContext.RequestAborted)).Where(s => s.IsActive(_clock.UtcNow)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync(int id, string userId) =>
        Done(id, await _organizations.ResendInvitationAsync(id, userId, User.Actor(), OrganizationUi.AppLinkBase(HttpContext), HttpContext.RequestAborted), "Email sent.");

    public async Task<IActionResult> OnPostResetAsync(int id, string userId) =>
        Done(id, await _organizations.SendPasswordResetAsync(id, userId, User.Actor(), OrganizationUi.AppLinkBase(HttpContext), HttpContext.RequestAborted), "Password reset email sent.");

    public async Task<IActionResult> OnPostEndSupportAsync(int id, Guid sessionId)
    {
        if (!(await _authorization.AuthorizeAsync(User, ProviderPolicies.SupportSessions)).Succeeded) return Forbid();
        var ended = await _support.EndAsync(sessionId, User.Actor(), HttpContext.RequestAborted);
        TempData[ended ? "Success" : "Error"] = ended ? "Support session ended." : "That support session is already over.";
        return RedirectToPage(new { id });
    }

    /// <summary>Compare with PayPal now and correct (Owners and Billing).</summary>
    public async Task<IActionResult> OnPostSyncAsync(int id)
    {
        if (!(await _authorization.AuthorizeAsync(User, ProviderPolicies.Billing)).Succeeded) return Forbid();
        var result = await _billing.SyncAsync(id, User.Actor(), HttpContext.RequestAborted);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded
            ? (result.Error == null ? "Checked with PayPal: nothing to correct." : $"Checked with PayPal and corrected ({result.Error}).")
            : result.Error;
        return RedirectToPage(new { id, tab = "subscription" });
    }

    private IActionResult Done(int id, OrganizationResult result, string success)
    {
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? success : result.Error;
        return RedirectToPage(new { id, tab = "admins" });
    }
}
