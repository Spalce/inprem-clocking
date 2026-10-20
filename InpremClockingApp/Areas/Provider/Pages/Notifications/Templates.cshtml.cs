using Inprem.Platform.Application.Notifications;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Notifications;

/// <summary>Notifications → Message wording (Phase 5 step 5.3): every message, by email and by text; edited on its own page.</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class TemplatesModel : PageModel
{
    private readonly NotificationConsole _console;

    public TemplatesModel(NotificationConsole console) => _console = console;

    public IReadOnlyList<TemplateView> Templates { get; private set; } = Array.Empty<TemplateView>();

    public async Task OnGetAsync() => Templates = await _console.TemplatesAsync(HttpContext.RequestAborted);
}
