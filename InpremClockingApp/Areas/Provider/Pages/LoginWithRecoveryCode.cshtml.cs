using System.Security.Claims;
using Inprem.Platform.Application.Provider;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>
/// Recovery Level 1 (self-service): sign in with one of the ten recovery codes. Two-factor stays
/// on (so a password alone still gets nobody in), the lost authenticator's key is replaced at
/// once, and the session can only reach the enrolment page until a new authenticator is
/// confirmed and new recovery codes are issued. Audited; the owner and the other owners are told.
/// </summary>
[AllowAnonymous]
public class LoginWithRecoveryCodeModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly UserManager<AppUser> _users;
    private readonly ProviderSecurityService _provider;
    private readonly ProviderSession _session;

    public LoginWithRecoveryCodeModel(SignInManager<AppUser> signIn, UserManager<AppUser> users, ProviderSecurityService provider, ProviderSession session)
    {
        _signIn = signIn;
        _users = users;
        _provider = provider;
        _session = session;
    }

    [BindProperty] public string RecoveryCode { get; set; } = "";

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() =>
        await _signIn.GetTwoFactorAuthenticationUserAsync() == null ? RedirectToPage("/Login", new { area = "Provider" }) : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });

        var result = await _signIn.TwoFactorRecoveryCodeSignInAsync((RecoveryCode ?? "").Replace(" ", string.Empty));
        if (!result.Succeeded)
        {
            Error = result.IsLockedOut ? "This account is locked after too many failed attempts. Try again in 15 minutes." : "The recovery code is not valid.";
            return Page();
        }

        await _users.ResetAuthenticatorKeyAsync(user);
        await _provider.RecordSelfServiceRecoveryAsync(user.Id, HttpContext.RequestAborted);
        await _session.SignInAsync(user, twoFactor: true, reEnrol: true);
        return RedirectToPage("/EnrolTwoFactor", new { area = "Provider" });
    }
}
