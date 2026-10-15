using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Memberships;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Account.Pages;

/// <summary>Why something could not be saved (or the kiosk is paused): Phase 2 step 2.7, C3-3 and C3-4.</summary>
[Authorize]
public class ReadOnlyModel : PageModel
{
    private readonly ITenantContext _tenant;
    private readonly IEntitlementService _entitlements;

    public ReadOnlyModel(ITenantContext tenant, IEntitlementService entitlements)
    {
        _tenant = tenant;
        _entitlements = entitlements;
    }

    [BindProperty(SupportsGet = true)] public bool Kiosk { get; set; }

    public AccessStatus? Access { get; private set; }
    public bool IsOwner => _tenant.Role == MembershipRole.Owner;

    public async Task<IActionResult> OnGetAsync()
    {
        if (_tenant.TenantId is not int tenantId) return RedirectToPage("/Index");
        Access = await _entitlements.GetAsync(tenantId, HttpContext.RequestAborted);
        if (Access.FullAccess) return Redirect(Kiosk ? "/VolunteerAttendance" : "/BackOffice"); // nothing to explain any more
        return Page();
    }
}
