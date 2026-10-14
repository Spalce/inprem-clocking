using System.ComponentModel.DataAnnotations;
using Inprem.Platform.Application.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Plans;

/// <summary>New plan with its first price (Billing and Owners).</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class NewModel : PageModel
{
    private readonly PlanService _plans;

    public NewModel(PlanService plans) => _plans = plans;

    [BindProperty, Required, StringLength(50)] public string Code { get; set; } = "";
    [BindProperty] public PlanForm Plan { get; set; } = new();
    [BindProperty] public PriceForm Price { get; set; } = new();

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Check the highlighted values.";
            return Page();
        }

        var result = await _plans.CreateAsync(Code, Plan.ToInput(), Price.Cycle, Price.Amount, Price.Currency, User.Actor(), HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Success"] = $"Plan \"{Plan.Name.Trim()}\" created.";
        return RedirectToPage("Details", new { id = result.PlanId });
    }
}
