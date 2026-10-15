using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Billing;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Areas.Billing.Pages;

/// <summary>
/// The owner's Billing page: current plan and status, subscribe or change plan (through PayPal),
/// cancel, and payment history. Paying happens only at PayPal; this page never sees card details.
/// </summary>
public class IndexModel : OwnerPageModel
{
    private readonly BillingAccountService _account;
    private readonly CheckoutService _checkout;

    public IndexModel(ITenantContext tenant, BillingAccountService account, CheckoutService checkout) : base(tenant)
    {
        _account = account;
        _checkout = checkout;
    }

    public BillingOverview Overview { get; private set; } = null!;

    /// <summary>Taken once per page view, so a double click starts one checkout.</summary>
    public string IdempotencyKey { get; private set; } = Guid.NewGuid().ToString("N");

    public async Task OnGetAsync() => Overview = await _account.GetAsync(TenantId, HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostSubscribeAsync(Guid priceId, string key)
    {
        var overview = await _account.GetAsync(TenantId, HttpContext.RequestAborted);
        if (overview.NextPurpose is not CheckoutPurpose purpose)
        {
            TempData["Error"] = "There is nothing to pay online for this organization. Please contact Inprem.";
            return RedirectToPage();
        }

        var returnUrl = Absolute($"/Billing/Return?key={Uri.EscapeDataString(key)}");
        var cancelUrl = Absolute($"/Billing/Cancelled?key={Uri.EscapeDataString(key)}");
        var started = await _checkout.StartAsync(new StartCheckout(purpose, TenantId, priceId, returnUrl, cancelUrl, key, Actor), HttpContext.RequestAborted);
        if (!started.Succeeded || started.Session?.ApprovalUrl == null)
        {
            TempData["Error"] = started.Error ?? "The checkout could not be started.";
            return RedirectToPage();
        }
        return Redirect(started.Session.ApprovalUrl); // to PayPal
    }
}
