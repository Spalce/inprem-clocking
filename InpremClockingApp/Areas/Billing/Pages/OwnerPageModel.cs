using System.Security.Claims;
using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Memberships;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Billing.Pages;

/// <summary>
/// The Billing pages (Phase 2 step 2.8) are for the organization's Owner only: other admins get
/// "access denied". Support sessions never get here (refused before, step 1.5).
/// </summary>
[Authorize]
public abstract class OwnerPageModel : PageModel
{
    protected OwnerPageModel(ITenantContext tenant) => Tenant = tenant;

    protected ITenantContext Tenant { get; }

    protected int TenantId => Tenant.TenantId!.Value;

    /// <summary>Who did it, for the audit log.</summary>
    protected string Actor => $"user:{User.FindFirstValue(ClaimTypes.NameIdentifier)}";

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        if (Tenant.TenantId == null || Tenant.Role != MembershipRole.Owner)
        {
            context.Result = Forbid();
            return;
        }
        await next();
    }

    /// <summary>An address on this site (validated against PayPal:ReturnUrlAllowList by the checkout).</summary>
    protected string Absolute(string pathAndQuery) => $"{Request.Scheme}://{Request.Host}{Request.PathBase}{pathAndQuery}";
}
