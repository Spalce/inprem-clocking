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

    public DetailsModel(OrganizationService organizations, IAuthorizationService authorization)
    {
        _organizations = organizations;
        _authorization = authorization;
    }

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
        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync(int id, string userId) =>
        Done(id, await _organizations.ResendInvitationAsync(id, userId, User.Actor(), OrganizationUi.AppLinkBase(HttpContext), HttpContext.RequestAborted), "Email sent.");

    public async Task<IActionResult> OnPostResetAsync(int id, string userId) =>
        Done(id, await _organizations.SendPasswordResetAsync(id, userId, User.Actor(), OrganizationUi.AppLinkBase(HttpContext), HttpContext.RequestAborted), "Password reset email sent.");

    private IActionResult Done(int id, OrganizationResult result, string success)
    {
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? success : result.Error;
        return RedirectToPage(new { id, tab = "admins" });
    }
}
