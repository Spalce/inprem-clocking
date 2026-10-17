using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>
/// Signs out of the console and always ends on its sign-in page. The form token is not required
/// here: after the session timed out, or after signing in to the app in another tab of the same
/// browser, the page's token no longer matched and the sign-out answered "HTTP 400" (developer's
/// report, 2026-10-17). A forged sign-out can only sign someone out, so nothing is put at risk.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class LogoutModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;

    public LogoutModel(SignInManager<AppUser> signIn) => _signIn = signIn;

    public IActionResult OnGet() => RedirectToPage("/Login", new { area = "Provider" });

    public async Task<IActionResult> OnPostAsync()
    {
        await _signIn.SignOutAsync();
        return RedirectToPage("/Login", new { area = "Provider" });
    }
}
