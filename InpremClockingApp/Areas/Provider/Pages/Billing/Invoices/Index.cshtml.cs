using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Billing.Invoices;

/// <summary>Every invoice (Phase 4 step 4.2): read-only list with filters; "+ New invoice" opens its own page.</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class IndexModel : PageModel
{
    private readonly InvoiceService _invoices;

    public IndexModel(InvoiceService invoices) => _invoices = invoices;

    [BindProperty(SupportsGet = true)] public int? TenantId { get; set; }
    [BindProperty(SupportsGet = true)] public InvoiceStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public InvoiceSource? Source { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public InvoicePage Result { get; private set; } = new(Array.Empty<InvoiceListItem>(), 0, 1, 1);

    public async Task OnGetAsync()
    {
        Result = await _invoices.ListAsync(new InvoiceFilter(TenantId, Status, Source, From, To, Math.Max(1, PageNumber)), HttpContext.RequestAborted);
    }

    public Dictionary<string, string> Route(int page) => new()
    {
        ["TenantId"] = TenantId?.ToString() ?? "", ["Status"] = Status?.ToString() ?? "", ["Source"] = Source?.ToString() ?? "",
        ["From"] = From?.ToString("yyyy-MM-dd") ?? "", ["To"] = To?.ToString("yyyy-MM-dd") ?? "", ["p"] = page.ToString(),
    };
}
