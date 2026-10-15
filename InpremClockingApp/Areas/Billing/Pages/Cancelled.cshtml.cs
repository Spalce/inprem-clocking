using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Areas.Billing.Pages;

/// <summary>The buyer chose "cancel" at PayPal: the checkout ends; nothing was charged.</summary>
public class CancelledModel : OwnerPageModel
{
    private readonly CheckoutService _checkout;

    public CancelledModel(ITenantContext tenant, CheckoutService checkout) : base(tenant) => _checkout = checkout;

    public async Task<IActionResult> OnGetAsync(string key)
    {
        var session = await _checkout.FindForTenantAsync(TenantId, key, HttpContext.RequestAborted);
        if (session != null) await _checkout.CancelByBuyerAsync(session.Id, HttpContext.RequestAborted);
        TempData["Error"] = "The checkout was cancelled at PayPal. Nothing was charged.";
        return RedirectToPage("Index");
    }
}
