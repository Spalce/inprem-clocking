using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Areas.Billing.Pages;

/// <summary>
/// The fixed return page on every PayPal plan (developer's "both" decision, step 2.2): used only
/// if PayPal ignores the checkout's own return address. PayPal adds <c>subscription_id</c>; the
/// page finds the organization's checkout and continues as the normal return page.
/// </summary>
public class PayPalReturnModel : OwnerPageModel
{
    private readonly CheckoutService _checkout;

    public PayPalReturnModel(ITenantContext tenant, CheckoutService checkout) : base(tenant) => _checkout = checkout;

    public async Task<IActionResult> OnGetAsync([FromQuery(Name = "subscription_id")] string? subscriptionId)
    {
        var session = subscriptionId == null ? null : await _checkout.FindByPayPalForTenantAsync(TenantId, subscriptionId, HttpContext.RequestAborted);
        return session == null ? RedirectToPage("Index") : RedirectToPage("Return", new { key = session.IdempotencyKey });
    }
}
