using Inprem.Platform.Application.Notifications;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Notifications;

/// <summary>
/// "Send me a test message" (Phase 5 step 5.3): checks that messages reach the console user, by
/// email, and by text when they agreed to Inprem's texts on "My alerts". The result is in the log.
/// </summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class SendTestModel : PageModel
{
    private readonly NotificationConsole _console;
    private readonly UserManager<AppUser> _users;

    public SendTestModel(NotificationConsole console, UserManager<AppUser> users)
    {
        _console = console;
        _users = users;
    }

    public string? Email { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        Email = user.Email;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        var result = await _console.SendTestAsync(user.Id, user.Email, user.PhoneNumberConfirmed ? user.PhoneNumber : null, user.FirstName, HttpContext.RequestAborted);
        TempData["Message"] = result.Queued == 0
            ? "Nothing could be sent: your account has no email address."
            : $"Test message queued ({result.Queued}): it goes out within a minute. Follow it in the log below."
              + (result.Skipped.Count > 0 ? $" Not by text: {string.Join(", ", result.Skipped.Select(s => s.Reason))}." : "");
        return RedirectToPage("Index");
    }
}
