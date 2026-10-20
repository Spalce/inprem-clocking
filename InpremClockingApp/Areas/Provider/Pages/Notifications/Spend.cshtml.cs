using Inprem.Platform.Application.Notifications;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Notifications;

/// <summary>Notifications → Texts and spend (Phase 5 step 5.3): today's spend against the cap, and each organization's texts this month.</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class SpendModel : PageModel
{
    private readonly NotificationConsole _console;

    public SpendModel(NotificationConsole console) => _console = console;

    public IReadOnlyList<TenantTextsRow> Tenants { get; private set; } = Array.Empty<TenantTextsRow>();
    public decimal Today { get; private set; }
    public decimal Cap { get; private set; }

    public async Task OnGetAsync() => (Tenants, Today, Cap) = await _console.SpendAsync(HttpContext.RequestAborted);
}
