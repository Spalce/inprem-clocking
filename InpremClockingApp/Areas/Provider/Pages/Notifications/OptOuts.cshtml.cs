using Inprem.Platform.Application.Notifications;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Notifications;

/// <summary>Notifications → Opt-outs (Phase 5 step 5.3): numbers that replied STOP (masked), newest first.</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class OptOutsModel : PageModel
{
    private readonly NotificationConsole _console;

    public OptOutsModel(NotificationConsole console) => _console = console;

    public IReadOnlyList<OptOutRow> OptOuts { get; private set; } = Array.Empty<OptOutRow>();

    public async Task OnGetAsync() => OptOuts = await _console.OptOutsAsync(HttpContext.RequestAborted);
}
