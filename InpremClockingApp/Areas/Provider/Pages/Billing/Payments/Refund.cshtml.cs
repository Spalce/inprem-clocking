using Inprem.Platform.Application.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Billing.Payments;

/// <summary>
/// Gives money back on a payment (Phase 4 step 4.2, design 5.5: a sensitive action, so a fresh
/// two-factor code is needed). Owners any amount; Billing users up to Billing:RefundLimit (D28).
/// </summary>
[Authorize(Policy = ProviderPolicies.Billing)]
[RequireRecentTwoFactor]
public class RefundModel : PageModel
{
    private readonly InvoiceService _invoices;
    private readonly IAuthorizationService _authorization;

    public RefundModel(InvoiceService invoices, IAuthorizationService authorization)
    {
        _invoices = invoices;
        _authorization = authorization;
    }

    [BindProperty] public decimal? Amount { get; set; }
    [BindProperty] public string Reason { get; set; } = "";

    public PaymentDetails Details { get; private set; } = null!;
    public bool IsOwner { get; private set; }
    public decimal Limit => _invoices.RefundLimit;
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (!await LoadAsync(id)) return NotFound();
        Amount = Details.Refundable;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Amount == null)
        {
            Error = "Give the amount to refund.";
            return Page();
        }
        var result = await _invoices.RefundAsync(id, Amount.Value, Reason ?? "", User.Actor(), IsOwner, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }
        TempData["Success"] = result.Warning ?? "Refund made.";
        return result.InvoiceId != null
            ? RedirectToPage("/Billing/Invoices/Details", new { id = result.InvoiceId })
            : RedirectToPage("/Tenants/Details", new { id = Details.Payment.TenantId, tab = "invoices" });
    }

    private async Task<bool> LoadAsync(Guid id)
    {
        var details = await _invoices.GetPaymentAsync(id, HttpContext.RequestAborted);
        if (details == null) return false;
        Details = details;
        IsOwner = (await _authorization.AuthorizeAsync(User, ProviderPolicies.Owner)).Succeeded;
        return true;
    }
}
