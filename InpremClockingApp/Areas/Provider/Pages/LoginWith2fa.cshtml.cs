using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>Second step of the provider sign-in: the authenticator app code.</summary>
[AllowAnonymous]
public class LoginWith2faModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly ProviderSession _session;

    public LoginWith2faModel(SignInManager<AppUser> signIn, ProviderSession session)
    {
        _signIn = signIn;
        _session = session;
    }

    [BindProperty] public string Code { get; set; } = "";

    public string? Error { get; private set; }

    /// <summary>The user confirmed a phone for codes (Phase 5): a texted code is offered too.</summary>
    public bool CanUseText { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        CanUseText = user.PhoneNumberConfirmed;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });

        var result = await _signIn.TwoFactorAuthenticatorSignInAsync(AccountGateway.NormalizeCode(Code ?? ""), isPersistent: false, rememberClient: false);
        if (result.Succeeded)
        {
            await _session.SignInAsync(user, twoFactor: true);
            return RedirectToPage("/Index", new { area = "Provider" });
        }

        Error = result.IsLockedOut ? "This account is locked after too many failed attempts. Try again in 15 minutes." : "The code is not valid.";
        CanUseText = user.PhoneNumberConfirmed;
        return Page();
    }
}
