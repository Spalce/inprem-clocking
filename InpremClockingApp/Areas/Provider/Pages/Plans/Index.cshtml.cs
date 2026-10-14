using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Plans;

/// <summary>Plans list (step 1.3): search, status filter, table, + New. No forms that change anything.</summary>
public class IndexModel : PageModel
{
    private readonly PlanService _plans;

    public IndexModel(PlanService plans) => _plans = plans;

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public PlanStatus? Status { get; set; }

    public IReadOnlyList<Plan> Plans { get; private set; } = Array.Empty<Plan>();
    public IReadOnlyDictionary<Guid, int> Organizations { get; private set; } = new Dictionary<Guid, int>();

    public async Task OnGetAsync()
    {
        Plans = await _plans.ListAsync(Search, Status, HttpContext.RequestAborted);
        Organizations = await _plans.CountSubscriptionsAsync(HttpContext.RequestAborted);
    }
}
