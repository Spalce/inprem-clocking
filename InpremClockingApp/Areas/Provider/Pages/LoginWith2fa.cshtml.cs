using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>Second step of the provider sign-in: the authenticator app code.</summary>
[ProviderAccess(ProviderAccessLevel.Anonymous)]
public class LoginWith2faModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;

    public LoginWith2faModel(SignInManager<AppUser> signIn) => _signIn = signIn;

    [BindProperty] public string Code { get; set; } = "";

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() =>
        await _signIn.GetTwoFactorAuthenticationUserAsync() == null ? RedirectToPage("/Login", new { area = "Provider" }) : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (await _signIn.GetTwoFactorAuthenticationUserAsync() == null) return RedirectToPage("/Login", new { area = "Provider" });

        var result = await _signIn.TwoFactorAuthenticatorSignInAsync(AccountGateway.NormalizeCode(Code ?? ""), isPersistent: false, rememberClient: false);
        if (result.Succeeded) return RedirectToPage("/Index", new { area = "Provider" });

        Error = result.IsLockedOut ? "This account is locked after too many failed attempts. Try again in 15 minutes." : "The code is not valid.";
        return Page();
    }
}
