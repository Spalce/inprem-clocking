using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Tenancy;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Areas.Billing.Pages;

/// <summary>
/// One of the organization's invoices (Phase 4 step 4.4): lines, total, payments received (a
/// receipt once paid) and the PayPal payment link while something is owed. Owners only; another
/// organization's invoice, or a draft, is "not found".
/// </summary>
public class InvoiceModel : OwnerPageModel
{
    private readonly InvoiceService _invoices;

    public InvoiceModel(ITenantContext tenant, InvoiceService invoices) : base(tenant) => _invoices = invoices;

    public OrganizationInvoiceDetails Details { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var details = await _invoices.ForOrganizationAsync(TenantId, id, HttpContext.RequestAborted);
        if (details == null) return NotFound();
        Details = details;
        return Page();
    }
}
