using Inprem.Platform.Application.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Plans;

/// <summary>Edit a plan's details (not its code or prices). Save and Cancel; back to the plan.</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class EditModel : PageModel
{
    private readonly PlanService _plans;

    public EditModel(PlanService plans) => _plans = plans;

    [BindProperty] public PlanForm Plan { get; set; } = new();
    public string Code { get; private set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var plan = await _plans.FindAsync(id, HttpContext.RequestAborted);
        if (plan == null) return NotFound();
        Plan = PlanForm.From(plan);
        Code = plan.Code;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id)
    {
        var plan = await _plans.FindAsync(id, HttpContext.RequestAborted);
        if (plan == null) return NotFound();
        Code = plan.Code;
        if (!ModelState.IsValid)
        {
            Error = "Check the highlighted values.";
            return Page();
        }

        var result = await _plans.UpdateAsync(id, Plan.ToInput(), User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Success"] = "Plan saved.";
        return RedirectToPage("Details", new { id });
    }
}
