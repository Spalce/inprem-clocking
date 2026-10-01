using System.ComponentModel.DataAnnotations;
using InpremClockingApp.Models.Billing;
using InpremClockingApp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Pages.Platform;

// SuperAdmin-only (see Program.cs's AuthorizeFolder("/Platform", "SuperAdminOnly") and
// multi-tenancy.md Part 2, Phase 8). Cross-tenant invoice queue - generate, mark paid, void,
// and download PDFs. Per-tenant invoice history is also shown read-only on TenantDetail.
public class Invoices : PageModel
{
    private readonly BillingService _billing;
    private readonly TenantAdminService _tenants;

    public Invoices(BillingService billing, TenantAdminService tenants)
    {
        _billing = billing;
        _tenants = tenants;
    }

    public List<Invoice> AllInvoices { get; set; } = new();

    public Dictionary<int, string> TenantNames { get; set; } = new();

    public List<(int Id, string Name)> TenantOptions { get; set; } = new();

    [BindProperty]
    public int GenerateTenantId { get; set; }

    [BindProperty]
    public MarkPaidInput PaidInput { get; set; } = new();

    public class MarkPaidInput
    {
        public int InvoiceId { get; set; }

        [Required, DataType(DataType.Date)]
        [Display(Name = "Paid Date")]
        public DateTime PaidDate { get; set; } = DateTime.UtcNow.Date;

        [StringLength(100)]
        [Display(Name = "Payment Method")]
        public string? PaymentMethod { get; set; }

        [StringLength(100)]
        [Display(Name = "Reference")]
        public string? PaymentReference { get; set; }

        [StringLength(500)]
        public string? Notes { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync().ConfigureAwait(true);
        return Page();
    }

    public async Task<IActionResult> OnPostGenerateAsync()
    {
        if (GenerateTenantId > 0)
        {
            await _billing.GenerateInvoiceAsync(GenerateTenantId).ConfigureAwait(true);
            TempData["Message"] = "Invoice generated for the tenant's current period.";
        }

        return RedirectToPage("./Invoices");
    }

    public async Task<IActionResult> OnPostMarkPaidAsync()
    {
        ModelState.Clear();
        if (!TryValidateModel(PaidInput, nameof(PaidInput)))
        {
            await LoadAsync().ConfigureAwait(true);
            return Page();
        }

        await _billing.MarkInvoicePaidAsync(
            PaidInput.InvoiceId, PaidInput.PaidDate, PaidInput.PaymentMethod, PaidInput.PaymentReference, PaidInput.Notes)
            .ConfigureAwait(true);

        TempData["Message"] = "Invoice marked paid.";
        return RedirectToPage("./Invoices");
    }

    public async Task<IActionResult> OnPostVoidAsync(int invoiceId)
    {
        await _billing.VoidInvoiceAsync(invoiceId).ConfigureAwait(true);
        TempData["Message"] = "Invoice voided.";
        return RedirectToPage("./Invoices");
    }

    private async Task LoadAsync()
    {
        AllInvoices = await _billing.GetAllInvoicesAsync().ConfigureAwait(true);

        var tenants = await _tenants.GetAllTenantsWithSubscriptionsAsync().ConfigureAwait(true);
        TenantNames = tenants.ToDictionary(t => t.Tenant.Id, t => t.Tenant.Name);
        TenantOptions = tenants.Select(t => (t.Tenant.Id, t.Tenant.Name)).ToList();
    }
}
