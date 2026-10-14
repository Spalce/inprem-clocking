using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Organizations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>Organizations list (step 1.2): search, filters, table, pagination, + New.</summary>
public class IndexModel : PageModel
{
    private readonly OrganizationService _organizations;
    private readonly PlanService _plans;

    public IndexModel(OrganizationService organizations, PlanService plans)
    {
        _organizations = organizations;
        _plans = plans;
    }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public OrganizationStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? PlanId { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? CreatedFrom { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? CreatedTo { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public OrganizationPage Result { get; private set; } = new(Array.Empty<OrganizationSummary>(), 0, 1, 25);
    public IReadOnlyList<Plan> Plans { get; private set; } = Array.Empty<Plan>();

    public async Task OnGetAsync()
    {
        Plans = await _plans.ListAsync(null, null, HttpContext.RequestAborted);
        Result = await _organizations.ListAsync(new OrganizationQuery(Search, Status, PlanId, CreatedFrom, CreatedTo, PageNumber), HttpContext.RequestAborted);
    }

    /// <summary>Query values for a page link, keeping the filters.</summary>
    public Dictionary<string, string> PageRoute(int page) => new()
    {
        ["Search"] = Search ?? "", ["Status"] = Status?.ToString() ?? "", ["PlanId"] = PlanId?.ToString() ?? "",
        ["CreatedFrom"] = CreatedFrom?.ToString("yyyy-MM-dd") ?? "", ["CreatedTo"] = CreatedTo?.ToString("yyyy-MM-dd") ?? "", ["p"] = page.ToString(),
    };
}
