using Inprem.Platform.Application.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Billing.Invoices;

/// <summary>One invoice (Phase 4 step 4.2): status, PayPal link, payments and refunds, and its actions.</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class DetailsModel : PageModel
{
    private readonly InvoiceService _invoices;

    public DetailsModel(InvoiceService invoices) => _invoices = invoices;

    public InvoiceDetails Details { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var details = await _invoices.GetAsync(id, HttpContext.RequestAborted);
        if (details == null) return NotFound();
        Details = details;
        return Page();
    }

    /// <summary>Sends a draft again (PayPal was unreachable), or reminds the organization of a sent invoice.</summary>
    public async Task<IActionResult> OnPostResendAsync(Guid id)
    {
        var result = await _invoices.ResendAsync(id, User.Actor(), HttpContext.RequestAborted);
        if (result.Succeeded) TempData["Success"] = result.Warning ?? "Invoice sent again.";
        else TempData["Error"] = result.Error;
        return RedirectToPage(new { id });
    }
}
