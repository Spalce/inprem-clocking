using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Billing;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Areas.Billing.Pages;

/// <summary>
/// Back from PayPal (design 7.5): asks PayPal (the confirm call), then the page polls the status:
/// every 2 seconds for 30 seconds, then every 10 seconds up to 2 minutes. Coming back is never
/// proof of payment: only PayPal's ACTIVE makes the subscription active.
/// </summary>
public class ReturnModel : OwnerPageModel
{
    private readonly CheckoutService _checkout;

    public ReturnModel(ITenantContext tenant, CheckoutService checkout) : base(tenant) => _checkout = checkout;

    [BindProperty(SupportsGet = true)] public string Key { get; set; } = "";

    public CheckoutStatus Status { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var session = await _checkout.FindForTenantAsync(TenantId, Key, HttpContext.RequestAborted);
        if (session == null) return RedirectToPage("Index");
        Status = (await _checkout.ConfirmAsync(session.Id, CheckoutSource.Confirm, HttpContext.RequestAborted)).Session?.Status ?? session.Status;
        return Page();
    }

    /// <summary>The polling call: confirms again (idempotent) and answers the status.</summary>
    public async Task<IActionResult> OnGetStatusAsync()
    {
        var session = await _checkout.FindForTenantAsync(TenantId, Key, HttpContext.RequestAborted);
        if (session == null) return NotFound();
        var status = session.IsFinal ? session.Status : (await _checkout.ConfirmAsync(session.Id, CheckoutSource.Confirm, HttpContext.RequestAborted)).Session?.Status ?? session.Status;
        return new JsonResult(new { status = status.ToString(), final = status is CheckoutStatus.Active or CheckoutStatus.Cancelled or CheckoutStatus.Expired or CheckoutStatus.Failed });
    }
}
