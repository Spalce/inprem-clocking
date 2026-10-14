using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>
/// Re-authentication before a sensitive action (design 5.5): a current authenticator code, then
/// back to the page that asked for it. Recovery codes are not accepted here.
/// </summary>
public class ReauthenticateModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly ProviderSession _session;

    public ReauthenticateModel(UserManager<AppUser> users, ProviderSession session)
    {
        _users = users;
        _session = session;
    }

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public string Code { get; set; } = "";

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });

        var valid = await _users.VerifyTwoFactorTokenAsync(user, _users.Options.Tokens.AuthenticatorTokenProvider, AccountGateway.NormalizeCode(Code ?? ""));
        if (!valid)
        {
            await _users.AccessFailedAsync(user);
            Error = "The code is not valid.";
            return Page();
        }

        await _session.SignInAsync(user, twoFactor: true, User);
        return !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToPage("/Index", new { area = "Provider" });
    }
}
