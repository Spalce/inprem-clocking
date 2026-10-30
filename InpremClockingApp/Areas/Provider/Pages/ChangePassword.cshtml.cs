using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

[ProviderAccess(ProviderAccessLevel.SignedIn)]
public sealed class ChangePasswordModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly ProviderSession _session;

    public ChangePasswordModel(UserManager<AppUser> users, ProviderSession session)
    {
        _users = users;
        _session = session;
    }

    [BindProperty] public string CurrentPassword { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        return user?.RequiresPasswordChange == true ? Page() : RedirectToPage("/EnrolTwoFactor", new { area = "Provider" });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        if (!user.RequiresPasswordChange) return RedirectToPage("/EnrolTwoFactor", new { area = "Provider" });
        if (NewPassword != ConfirmPassword)
        {
            Error = "The two passwords do not match.";
            return Page();
        }

        var result = await _users.ChangePasswordAsync(user, CurrentPassword, NewPassword);
        if (!result.Succeeded)
        {
            Error = string.Join(" ", result.Errors.Select(e => e.Description));
            return Page();
        }

        user.RequiresPasswordChange = false;
        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            Error = string.Join(" ", updated.Errors.Select(e => e.Description));
            return Page();
        }

        await _session.SignInAsync(user, twoFactor: false, User);
        return RedirectToPage("/EnrolTwoFactor", new { area = "Provider" });
    }
}