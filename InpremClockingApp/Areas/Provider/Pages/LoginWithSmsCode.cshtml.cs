using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>
/// Second step of the provider sign-in by text (Phase 5 step 5.2, D14): instead of the authenticator
/// app, a code texted to the phone the user confirmed on "My phone for codes". Only offered to people
/// who confirmed one; the authenticator app stays the main way.
/// </summary>
[AllowAnonymous]
public class LoginWithSmsCodeModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly ProviderSession _session;
    private readonly LoginCodeService _codes;

    public LoginWithSmsCodeModel(SignInManager<AppUser> signIn, ProviderSession session, LoginCodeService codes)
    {
        _signIn = signIn;
        _session = session;
        _codes = codes;
    }

    [BindProperty] public string Code { get; set; } = "";

    public string MaskedPhone { get; private set; } = "";
    public bool CodeSent { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        if (!user.PhoneNumberConfirmed) return RedirectToPage("/LoginWith2fa", new { area = "Provider" });
        MaskedPhone = PhoneNumber.Mask(PhoneNumber.ToE164(user.PhoneNumber));
        return Page();
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        if (!user.PhoneNumberConfirmed) return RedirectToPage("/LoginWith2fa", new { area = "Provider" });
        MaskedPhone = PhoneNumber.Mask(PhoneNumber.ToE164(user.PhoneNumber));

        var sent = await _codes.SendAsync(user.Id, user.PhoneNumber, LoginCodePurposes.ProviderSignIn, HttpContext.RequestAborted);
        CodeSent = sent.Sent;
        if (sent.Sent) Message = sent.Message; else Error = sent.Message;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _signIn.GetTwoFactorAuthenticationUserAsync();
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        if (!user.PhoneNumberConfirmed) return RedirectToPage("/LoginWith2fa", new { area = "Provider" });
        MaskedPhone = PhoneNumber.Mask(PhoneNumber.ToE164(user.PhoneNumber));
        CodeSent = true;

        var result = await _codes.CheckAsync(user.Id, user.PhoneNumber, Code, LoginCodePurposes.ProviderSignIn, HttpContext.RequestAborted);
        if (result == OtpCheck.Approved)
        {
            // The same end as an authenticator code: the half-signed-in step is closed, the session starts.
            await HttpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
            await _signIn.UserManager.ResetAccessFailedCountAsync(user);
            await _session.SignInAsync(user, twoFactor: true);
            return RedirectToPage("/Index", new { area = "Provider" });
        }

        Error = result switch
        {
            OtpCheck.Wrong => "The code is not valid.",
            OtpCheck.ExpiredOrUsed => "This code has expired or was already used. Send a new one.",
            OtpCheck.TooManyAttempts => "Too many wrong codes. Wait 15 minutes, or use your authenticator app.",
            _ => "The code could not be checked just now. Try again in a minute, or use your authenticator app.",
        };
        return Page();
    }
}
