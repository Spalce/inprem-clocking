using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Billing.Invoices;

/// <summary>Records money received offline on an invoice: date, method, reference, amount. Owners and Billing.</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class RecordPaymentModel : PageModel
{
    private readonly InvoiceService _invoices;

    public RecordPaymentModel(InvoiceService invoices) => _invoices = invoices;

    [BindProperty] public DateOnly? PaidOn { get; set; }
    [BindProperty] public OfflinePaymentMethod Method { get; set; } = OfflinePaymentMethod.BankTransfer;
    [BindProperty] public string Reference { get; set; } = "";
    [BindProperty] public decimal? Amount { get; set; }

    public InvoiceDetails Details { get; private set; } = null!;
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (!await LoadAsync(id)) return NotFound();
        PaidOn = DateOnly.FromDateTime(DateTime.UtcNow);
        Amount = Details.Invoice.Outstanding;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (PaidOn == null || Amount == null)
        {
            Error = "Give the date and the amount.";
            return Page();
        }
        var result = await _invoices.RecordOfflinePaymentAsync(id, new OfflinePaymentInput(PaidOn.Value, Method, Reference ?? "", Amount.Value), User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }
        TempData["Success"] = "Payment recorded.";
        return RedirectToPage("Details", new { id });
    }

    private async Task<bool> LoadAsync(Guid id)
    {
        var details = await _invoices.GetAsync(id, HttpContext.RequestAborted);
        if (details == null) return false;
        Details = details;
        return true;
    }

    public static string Label(OfflinePaymentMethod method) => method switch
    {
        OfflinePaymentMethod.BankTransfer => "Bank transfer",
        OfflinePaymentMethod.MobileMoney => "Mobile money",
        _ => method.ToString(),
    };
}
