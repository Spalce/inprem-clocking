using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Notifications;

/// <summary>
/// Notifications → Delivery log (Phase 5 step 5.3): every email and text, newest first, with its status
/// and, when it was not sent, why. Texts show the number masked. A list page: no forms (the test
/// message has its own page).
/// </summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class IndexModel : PageModel
{
    public const int PageSize = 50;

    private readonly NotificationConsole _console;

    public IndexModel(NotificationConsole console) => _console = console;

    [BindProperty(SupportsGet = true)] public NotificationChannel? Channel { get; set; }
    [BindProperty(SupportsGet = true)] public NotificationStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public int? TenantId { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public IReadOnlyList<NotificationRow> Items { get; private set; } = Array.Empty<NotificationRow>();
    public int Total { get; private set; }
    public int Pages => Math.Max(1, (Total + PageSize - 1) / PageSize);
    public bool IsStandIn { get; private set; }

    public async Task OnGetAsync([FromServices] ISmsSender sms)
    {
        IsStandIn = sms.IsStandIn;
        PageNumber = Math.Max(1, PageNumber);
        (Items, Total) = await _console.LogAsync(new NotificationFilter(Channel, Status, TenantId, null), PageNumber, PageSize, HttpContext.RequestAborted);
    }

    public Dictionary<string, string> Route(int page) => new()
    {
        ["Channel"] = Channel?.ToString() ?? "", ["Status"] = Status?.ToString() ?? "", ["TenantId"] = TenantId?.ToString() ?? "", ["p"] = page.ToString(),
    };
}
