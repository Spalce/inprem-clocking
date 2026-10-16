using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Billing.Invoices;

/// <summary>
/// A new invoice (Phase 4 step 4.2): organization, lines, due date and notes, then Review, then
/// "Create and send". Nothing is saved before the last step. Owners and Billing.
/// </summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class NewModel : PageModel
{
    public const int LineRows = 8;

    private readonly InvoiceService _invoices;
    private readonly OrganizationService _organizations;

    public NewModel(InvoiceService invoices, OrganizationService organizations)
    {
        _invoices = invoices;
        _organizations = organizations;
    }

    public sealed class LineForm
    {
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitAmount { get; set; }
    }

    [BindProperty(SupportsGet = true)] public int? TenantId { get; set; }
    [BindProperty] public InvoiceSource Source { get; set; } = InvoiceSource.PayPalInvoice;
    [BindProperty] public List<LineForm> Lines { get; set; } = new();
    [BindProperty] public DateOnly? DueDate { get; set; }
    [BindProperty] public string? Notes { get; set; }

    public IReadOnlyList<(int Id, string Name)> Organizations { get; private set; } = Array.Empty<(int, string)>();
    public bool Reviewing { get; private set; }
    public string? Error { get; private set; }
    public string TenantName { get; private set; } = "";
    public decimal Total => Filled.Sum(l => l.Quantity * l.UnitAmount);
    public IReadOnlyList<InvoiceLineInput> Filled =>
        Lines.Where(l => !string.IsNullOrWhiteSpace(l.Description)).Select(l => new InvoiceLineInput(l.Description!.Trim(), l.Quantity ?? 1, l.UnitAmount ?? 0)).ToList();

    public async Task OnGetAsync()
    {
        DueDate ??= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostReviewAsync()
    {
        await LoadAsync();
        Error = await CheckAsync();
        Reviewing = Error == null;
        return Page();
    }

    public async Task<IActionResult> OnPostEditAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        await LoadAsync();
        Error = await CheckAsync();
        if (Error != null) return Page();
        var result = await _invoices.CreateAndSendAsync(Input(), User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded && result.InvoiceId == null)
        {
            Error = result.Error;
            return Page();
        }
        if (result.Succeeded) TempData["Success"] = result.Warning ?? (Source == InvoiceSource.Manual ? "Invoice issued and emailed to the organization's owners." : "Invoice created and sent by PayPal.");
        else TempData["Error"] = result.Error;
        return RedirectToPage("Details", new { id = result.InvoiceId });
    }

    private NewInvoiceInput Input() => new(TenantId ?? 0, Source, Filled, DueDate ?? default, Notes);

    private async Task<string?> CheckAsync()
    {
        if (TenantId == null) return "Choose an organization.";
        if (DueDate == null) return "Choose a due date.";
        if (Lines.Any(l => !string.IsNullOrWhiteSpace(l.Description) && (l.Quantity is null or <= 0 || l.UnitAmount is null)))
            return "Every line with a description needs a quantity and a price.";
        return await _invoices.CheckAsync(Input(), HttpContext.RequestAborted);
    }

    private async Task LoadAsync()
    {
        while (Lines.Count < LineRows) Lines.Add(new LineForm());
        var page = await _organizations.ListAsync(new OrganizationQuery(PageSize: 1000), HttpContext.RequestAborted);
        Organizations = page.Items.Select(o => (o.TenantId, o.Name)).OrderBy(o => o.Name).ToList();
        TenantName = Organizations.FirstOrDefault(o => o.Id == TenantId).Name ?? "";
    }
}
