using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Billing;

/// <summary>Every organization's subscription (read-only list; actions on the organization's Subscription tab).</summary>
[Authorize(Policy = ProviderPolicies.Billing)]
public class IndexModel : PageModel
{
    public const int PageSize = 50;

    private readonly BillingAccountService _billing;

    public IndexModel(BillingAccountService billing) => _billing = billing;

    [BindProperty(SupportsGet = true)] public SubscriptionStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public BillingMode? Mode { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public IReadOnlyList<(Subscription Subscription, string TenantName)> Items { get; private set; } = Array.Empty<(Subscription, string)>();
    public int Total { get; private set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    public async Task OnGetAsync()
    {
        PageNumber = Math.Max(1, PageNumber);
        (Items, Total) = await _billing.ListAsync(Status, Mode, (PageNumber - 1) * PageSize, PageSize, HttpContext.RequestAborted);
    }

    public Dictionary<string, string> Route(int page) => new() { ["Status"] = Status?.ToString() ?? "", ["Mode"] = Mode?.ToString() ?? "", ["p"] = page.ToString() };
}
