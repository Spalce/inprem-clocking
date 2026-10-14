using Inprem.Platform.Application.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Plans;

/// <summary>Add a price to a plan (one active price per cycle).</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class NewPriceModel : PageModel
{
    private readonly PlanService _plans;

    public NewPriceModel(PlanService plans) => _plans = plans;

    [BindProperty] public PriceForm Price { get; set; } = new();
    public string PlanName { get; private set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var plan = await _plans.FindAsync(id, HttpContext.RequestAborted);
        if (plan == null) return NotFound();
        PlanName = plan.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        var plan = await _plans.FindAsync(id, HttpContext.RequestAborted);
        if (plan == null) return NotFound();
        PlanName = plan.Name;
        if (!ModelState.IsValid)
        {
            Error = "Check the amount.";
            return Page();
        }

        var result = await _plans.AddPriceAsync(id, Price.Cycle, Price.Amount, Price.Currency, User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Success"] = "Price added.";
        return RedirectToPage("Details", new { id });
    }
}
