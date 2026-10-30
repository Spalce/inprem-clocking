using Inprem.Platform.Application.Provider;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>
/// Provider sign-in (design 5.5): provider users only, lockout after 5 failures (C3-7 settings).
/// The same message for an unknown email, a tenant account and a wrong password.
/// </summary>
[AllowAnonymous]
public class LoginModel : PageModel
{
    public const string Refused = "Invalid sign-in attempt.";

    private readonly SignInManager<AppUser> _signIn;
    private readonly UserManager<AppUser> _users;
    private readonly ProviderSecurityService _provider;
    private readonly ProviderSession _session;

    public LoginModel(SignInManager<AppUser> signIn, UserManager<AppUser> users, ProviderSecurityService provider, ProviderSession session)
    {
        _signIn = signIn;
        _users = users;
        _provider = provider;
        _session = session;
    }

    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = string.IsNullOrWhiteSpace(Email) ? null : await _users.FindByEmailAsync(Email.Trim());
        if (user == null || await _provider.FindUsableProviderUserAsync(user.Id, HttpContext.RequestAborted) == null)
        {
            Error = Refused;
            return Page();
        }

        var result = await _signIn.PasswordSignInAsync(user, Password, isPersistent: false, lockoutOnFailure: true);
        if (result.RequiresTwoFactor) return RedirectToPage("/LoginWith2fa", new { area = "Provider" });
        if (result.Succeeded)
        {
            await _session.SignInAsync(user, twoFactor: false);
            if (user.RequiresPasswordChange)
                return RedirectToPage("/ChangePassword", new { area = "Provider" });
            return RedirectToPage("/EnrolTwoFactor", new { area = "Provider" });
        }

        Error = result.IsLockedOut ? "This account is locked after too many failed attempts. Try again in 15 minutes." : Refused;
        return Page();
    }
}
