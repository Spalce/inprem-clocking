using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Plans;

/// <summary>
/// One plan, read-only. Retiring (plan or price) and reactivating are buttons here, confirmed in
/// a dialog that says exactly what happens (Billing and Owners only). "Publish to PayPal"
/// (Phase 2 step 2.3) creates the plan's PayPal product and a PayPal plan per active price.
/// </summary>
public class DetailsModel : PageModel
{
    private readonly PlanService _plans;
    private readonly PlanPublisher _publisher;
    private readonly IAuthorizationService _authorization;

    public DetailsModel(PlanService plans, PlanPublisher publisher, IAuthorizationService authorization)
    {
        _plans = plans;
        _publisher = publisher;
        _authorization = authorization;
    }

    public Plan Plan { get; private set; } = null!;
    public int Organizations { get; private set; }
    public bool CanChange { get; private set; }

    /// <summary>The plan has an active price without a PayPal plan.</summary>
    public bool NeedsPublishing { get; private set; }

    /// <summary>PayPal credentials are configured on this site.</summary>
    public bool PayPalAvailable => _publisher.IsAvailable;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var plan = await _plans.FindAsync(id, HttpContext.RequestAborted);
        if (plan == null) return NotFound();
        Plan = plan;
        Organizations = (await _plans.CountSubscriptionsAsync(HttpContext.RequestAborted)).TryGetValue(id, out var count) ? count : 0;
        CanChange = (await _authorization.AuthorizeAsync(User, ProviderPolicies.Billing)).Succeeded;
        NeedsPublishing = PlanPublisher.NeedsPublishing(plan);
        return Page();
    }

    public Task<IActionResult> OnPostRetireAsync(Guid id) =>
        ChangeAsync(id, () => _plans.RetireAsync(id, User.Actor(), HttpContext.RequestAborted), "Plan retired: no new organizations can choose it.");

    public Task<IActionResult> OnPostReactivateAsync(Guid id) =>
        ChangeAsync(id, () => _plans.ReactivateAsync(id, User.Actor(), HttpContext.RequestAborted), "Plan offered again. Add a price if it has none.");

    public Task<IActionResult> OnPostPublishAsync(Guid id) =>
        ChangeAsync(id, () => _publisher.PublishAsync(id, User.Actor(), HttpContext.RequestAborted), "Published to PayPal: every active price now has a PayPal plan.");

    public Task<IActionResult> OnPostRetirePriceAsync(Guid id, Guid priceId) =>
        ChangeAsync(id, () => _plans.RetirePriceAsync(id, priceId, User.Actor(), HttpContext.RequestAborted), "Price retired.");

    private async Task<IActionResult> ChangeAsync(Guid id, Func<Task<PlanResult>> change, string success)
    {
        if (!(await _authorization.AuthorizeAsync(User, ProviderPolicies.Billing)).Succeeded) return Forbid();
        var result = await change();
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? success : result.Error;
        return RedirectToPage(new { id });
    }
}
