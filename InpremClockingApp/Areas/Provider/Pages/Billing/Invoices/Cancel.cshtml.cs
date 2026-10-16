using Inprem.Platform.Application.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Billing.Invoices;

/// <summary>Cancels an invoice nothing was paid on (also at PayPal). Owners and Billing.</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class CancelModel : PageModel
{
    private readonly InvoiceService _invoices;

    public CancelModel(InvoiceService invoices) => _invoices = invoices;

    [BindProperty] public string Reason { get; set; } = "";
    public string Number { get; private set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        if (!await LoadAsync(id)) return NotFound();
        var result = await _invoices.CancelAsync(id, Reason ?? "", User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }
        TempData["Success"] = "Invoice cancelled.";
        return RedirectToPage("Details", new { id });
    }

    private async Task<bool> LoadAsync(Guid id)
    {
        var details = await _invoices.GetAsync(id, HttpContext.RequestAborted);
        if (details == null) return false;
        Number = details.Invoice.Number;
        return true;
    }
}
