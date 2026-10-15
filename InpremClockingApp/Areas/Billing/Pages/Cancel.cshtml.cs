using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Areas.Billing.Pages;

/// <summary>Cancel the PayPal subscription (own page with a reason; access continues to the end of the paid period).</summary>
public class CancelModel : OwnerPageModel
{
    private readonly BillingAccountService _account;
    private readonly CheckoutService _checkout;

    public CancelModel(ITenantContext tenant, BillingAccountService account, CheckoutService checkout) : base(tenant)
    {
        _account = account;
        _checkout = checkout;
    }

    [BindProperty] public string? Reason { get; set; }

    public BillingOverview Overview { get; private set; } = null!;
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Overview = await _account.GetAsync(TenantId, HttpContext.RequestAborted);
        return Overview.PaysByPayPal ? Page() : RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await _checkout.CancelSubscriptionAsync(TenantId, Reason ?? "", Actor, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            TempData["Success"] = "Your subscription is cancelled. You keep full access until the end of the paid period.";
            return RedirectToPage("Index");
        }
        Overview = await _account.GetAsync(TenantId, HttpContext.RequestAborted);
        Error = result.Error;
        return Page();
    }
}
