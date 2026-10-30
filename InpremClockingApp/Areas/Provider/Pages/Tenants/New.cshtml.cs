using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.Provider;
using InpremClockingApp.Provider.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Tenants;

/// <summary>
/// New organization wizard (step 1.2): Organization → Owner → Billing → Review. Each step is
/// checked before moving on; the values of the other steps travel as hidden fields. Create
/// checks everything again and makes all records in one transaction.
/// </summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class NewModel : PageModel
{
    public const int Steps = 4;
    public static readonly string[] StepNames = { "Organization", "Owner", "Billing", "Review" };

    private readonly OrganizationService _organizations;
    private readonly PlanService _plans;

    public NewModel(OrganizationService organizations, PlanService plans)
    {
        _organizations = organizations;
        _plans = plans;
    }

    [BindProperty] public int Step { get; set; } = 1;

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Slug { get; set; } = "";
    [BindProperty] public string TimeZoneId { get; set; } = "America/New_York";
    [BindProperty] public string? Address { get; set; }
    [BindProperty] public string? ContactInfo { get; set; }

    [BindProperty] public string OwnerFirstName { get; set; } = "";
    [BindProperty] public string OwnerLastName { get; set; } = "";
    [BindProperty] public string OwnerEmail { get; set; } = "";
    [BindProperty] public string? OwnerMobile { get; set; }

    /// <summary>"planId|priceId", or empty for no plan (Free only).</summary>
    [BindProperty] public string? PlanChoice { get; set; }
    [BindProperty] public BillingMode Mode { get; set; } = BillingMode.PayPal;
    [BindProperty] public int TrialDays { get; set; } = PlanService.DefaultTrialDays;

    public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<Plan> OfferedPlans { get; private set; } = Array.Empty<Plan>();
    public bool OwnerHasAccount { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>Live check for the address name (JSON for console.js).</summary>
    public async Task<IActionResult> OnGetSlugAsync(string? slug)
    {
        var check = await _organizations.CheckSlugAsync(slug, null, HttpContext.RequestAborted);
        return new JsonResult(new { available = check.Available, problem = check.Problem, suggestion = check.Suggestion });
    }

    public async Task<IActionResult> OnPostAsync(string move)
    {
        await LoadAsync();
        if (move == "back")
        {
            Step = Math.Max(1, Step - 1);
            return Page();
        }

        if (move == "create")
        {
            // Temporary-password test flow: retain the real invitation email implementation in OrganizationService,
            // but do not send it until the provider email/Twilio setup is available.
            var result = await _organizations.CreateAsync(Input(), User.Actor(), OrganizationUi.AppLinkBase(HttpContext), HttpContext.RequestAborted, sendEmail: false);
            if (!result.Succeeded)
            {
                Errors = new[] { result.Error! };
                return Page();
            }

            TempData["Success"] = result.Warning ?? $"Organization \"{Name.Trim()}\" created. The owner was emailed.";
            if (!string.IsNullOrWhiteSpace(result.TemporaryPassword))
                TempData["OrganizationTemporaryPassword"] = result.TemporaryPassword;
            return RedirectToPage("Details", new { id = result.TenantId });
        }

        Errors = Step switch
        {
            1 => await _organizations.CheckOrganizationStepAsync(Name, Slug, TimeZoneId, Address, ContactInfo, HttpContext.RequestAborted),
            2 => await _organizations.CheckOwnerStepAsync(OwnerFirstName, OwnerLastName, OwnerEmail, OwnerMobile, HttpContext.RequestAborted),
            3 => await _organizations.CheckBillingStepAsync(ChosenPlan().PlanId, ChosenPlan().PriceId, Mode, TrialDays, HttpContext.RequestAborted),
            _ => Array.Empty<string>(),
        };
        if (Errors.Count == 0)
        {
            Step = Math.Min(Steps, Step + 1);
            ModelState.Clear(); // show the new step's values, not the posted ones
        }
        if (Step == Steps) OwnerHasAccount = await _organizations.OwnerHasAccountAsync(OwnerEmail, HttpContext.RequestAborted);
        return Page();
    }

    private async Task LoadAsync() => OfferedPlans = await _plans.ListOfferedAsync(HttpContext.RequestAborted);

    public (Guid? PlanId, Guid? PriceId) ChosenPlan()
    {
        var parts = (PlanChoice ?? "").Split('|');
        return parts.Length == 2 && Guid.TryParse(parts[0], out var plan) && Guid.TryParse(parts[1], out var price) ? (plan, price) : (null, null);
    }

    public string ChosenPlanText()
    {
        var (planId, priceId) = ChosenPlan();
        var plan = OfferedPlans.FirstOrDefault(p => p.Id == planId);
        var price = plan?.Prices.FirstOrDefault(p => p.Id == priceId);
        return plan == null || price == null ? "No plan" : $"{plan.Name} — {PlanService.Describe(price)}";
    }

    private NewOrganization Input()
    {
        var (planId, priceId) = ChosenPlan();
        return new NewOrganization(Name, Slug, TimeZoneId, Address, ContactInfo, OwnerFirstName, OwnerLastName, OwnerEmail, OwnerMobile, planId, priceId, Mode, TrialDays);
    }
}
