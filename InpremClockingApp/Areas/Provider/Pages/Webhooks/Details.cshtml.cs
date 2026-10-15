using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Webhooks;

/// <summary>
/// One PayPal event: what it is, how processing went, and "Replay" (process it again; safe,
/// because processing follows PayPal's current state). The raw body is not shown: it holds payer details.
/// </summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class DetailsModel : PageModel
{
    private readonly IBillingEventStore _events;
    private readonly BillingWebhookService _webhooks;

    public DetailsModel(IBillingEventStore events, BillingWebhookService webhooks)
    {
        _events = events;
        _webhooks = webhooks;
    }

    public BillingEvent Event { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string id)
    {
        var found = await _events.FindAsync(id, HttpContext.RequestAborted);
        if (found == null) return NotFound();
        Event = found;
        return Page();
    }

    public async Task<IActionResult> OnPostReplayAsync(string id)
    {
        var replayed = await _webhooks.ReplayAsync(id, User.Actor(), HttpContext.RequestAborted);
        if (!replayed) return NotFound();
        TempData["Success"] = "Queued for processing again. Refresh in a moment to see the result.";
        return RedirectToPage(new { id });
    }
}
