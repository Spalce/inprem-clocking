using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Notifications;

/// <summary>Edit one message's wording for one channel, switch it off, or go back to the default (Phase 5 step 5.3).</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class EditTemplateModel : PageModel
{
    private readonly NotificationConsole _console;

    public EditTemplateModel(NotificationConsole console) => _console = console;

    [BindProperty(SupportsGet = true)] public string Key { get; set; } = "";
    [BindProperty(SupportsGet = true)] public NotificationChannel Channel { get; set; }
    [BindProperty] public string? Subject { get; set; }
    [BindProperty] public string Body { get; set; } = "";
    [BindProperty] public bool Enabled { get; set; }

    public TemplateView? View { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        View = await _console.TemplateAsync(Key, Channel, HttpContext.RequestAborted);
        if (View == null) return NotFound();
        (Subject, Body, Enabled) = (View.Subject, View.Body, View.Enabled);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        View = await _console.TemplateAsync(Key, Channel, HttpContext.RequestAborted);
        if (View == null) return NotFound();
        Error = await _console.SaveTemplateAsync(Key, Channel, Subject, Body ?? "", Enabled, User.Actor(), HttpContext.RequestAborted);
        if (Error != null) return Page();
        TempData["Message"] = $"The wording of {Key} ({(Channel == NotificationChannel.Sms ? "text" : "email")}) is saved.";
        return RedirectToPage("Templates");
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        if (await _console.TemplateAsync(Key, Channel, HttpContext.RequestAborted) == null) return NotFound();
        await _console.ResetTemplateAsync(Key, Channel, User.Actor(), HttpContext.RequestAborted);
        return RedirectToPage(new { key = Key, channel = Channel });
    }
}
