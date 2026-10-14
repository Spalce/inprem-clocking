using Inprem.Platform.Application.Support;
using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Account.Pages;

/// <summary>The organization's support history (step 1.5): who from Inprem support worked here, when and why.</summary>
[Authorize(Policy = "AdminOnly")]
public class SupportSessionsModel : PageModel
{
    private readonly SupportSessionService _support;
    private readonly ITenantContext _tenant;

    public SupportSessionsModel(SupportSessionService support, ITenantContext tenant)
    {
        _support = support;
        _tenant = tenant;
    }

    public IReadOnlyList<SupportSession> Sessions { get; private set; } = Array.Empty<SupportSession>();

    public async Task OnGetAsync()
    {
        if (_tenant.TenantId is { } tenantId) Sessions = await _support.ListForTenantAsync(tenantId, HttpContext.RequestAborted);
    }
}
