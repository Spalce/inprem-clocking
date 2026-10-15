using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Webhooks;

/// <summary>PayPal webhook events (step 2.5, P2-3: every stored event visible). Read-only list; replay on the event's page.</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class IndexModel : PageModel
{
    public const int PageSize = 50;

    private readonly IBillingEventStore _events;

    public IndexModel(IBillingEventStore events) => _events = events;

    [BindProperty(SupportsGet = true)] public BillingEventStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Type { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public IReadOnlyList<BillingEvent> Items { get; private set; } = Array.Empty<BillingEvent>();
    public int Total { get; private set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    public async Task OnGetAsync()
    {
        PageNumber = Math.Max(1, PageNumber);
        (Items, Total) = await _events.ListAsync(Status, Type, (PageNumber - 1) * PageSize, PageSize, HttpContext.RequestAborted);
    }

    public Dictionary<string, string> Route(int page) => new() { ["Status"] = Status?.ToString() ?? "", ["Type"] = Type ?? "", ["p"] = page.ToString() };
}
