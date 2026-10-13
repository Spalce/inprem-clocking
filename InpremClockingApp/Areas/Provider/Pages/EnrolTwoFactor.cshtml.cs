using System.Security.Claims;
using System.Text;
using Inprem.Platform.Application.Provider;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>
/// Two-factor enrolment (D14): add the key to an authenticator app, confirm one code, receive ten
/// recovery codes shown once. Required before any console page; again after a recovery code was
/// used (Level 1) or two-factor was reset (Levels 2-3).
/// </summary>
[ProviderAccess(ProviderAccessLevel.SignedIn)]
public class EnrolTwoFactorModel : PageModel
{
    public const int RecoveryCodeCount = 10;
    private const string Issuer = "Inprem Provider Console";

    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;
    private readonly ProviderSecurityService _provider;

    public EnrolTwoFactorModel(UserManager<AppUser> users, SignInManager<AppUser> signIn, ProviderSecurityService provider)
    {
        _users = users;
        _signIn = signIn;
        _provider = provider;
    }

    [BindProperty] public string Code { get; set; } = "";

    public string SharedKey { get; private set; } = "";
    public string AuthenticatorUri { get; private set; } = "";
    public string? Error { get; private set; }

    /// <summary>Shown once, right after enrolment; never stored in readable form.</summary>
    public IReadOnlyList<string>? RecoveryCodes { get; private set; }

    private bool MustEnrol(AppUser user, bool twoFactorEnabled) => !twoFactorEnabled || User.HasClaim(c => c.Type == ProviderAccessFilter.ReEnrolClaimType);

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        if (!MustEnrol(user, await _users.GetTwoFactorEnabledAsync(user))) return RedirectToPage("/Index", new { area = "Provider" });

        await LoadKeyAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        if (!MustEnrol(user, await _users.GetTwoFactorEnabledAsync(user))) return RedirectToPage("/Index", new { area = "Provider" });

        var valid = await _users.VerifyTwoFactorTokenAsync(user, _users.Options.Tokens.AuthenticatorTokenProvider, AccountGateway.NormalizeCode(Code ?? ""));
        if (!valid)
        {
            Error = "The code is not valid. Check the time on your phone and try the next code.";
            await LoadKeyAsync(user);
            return Page();
        }

        await _users.SetTwoFactorEnabledAsync(user, true);
        RecoveryCodes = (await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))!.ToList();
        await _provider.RecordTwoFactorEnrolledAsync(user.Id, HttpContext.RequestAborted);

        // A fresh sign-in that counts as two-factor (and drops any re-enrol marker).
        await _signIn.SignInWithClaimsAsync(user, isPersistent: false, new[] { new Claim(ProviderAccessFilter.MfaClaimType, ProviderAccessFilter.MfaClaimValue) });
        return Page();
    }

    private async Task LoadKeyAsync(AppUser user)
    {
        var key = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _users.ResetAuthenticatorKeyAsync(user);
            key = await _users.GetAuthenticatorKeyAsync(user);

            // The key change rotated the security stamp: keep this session valid.
            await _signIn.RefreshSignInAsync(user);
        }

        SharedKey = FormatKey(key!);
        AuthenticatorUri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(user.Email ?? "")}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";
    }

    private static string FormatKey(string key)
    {
        var result = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0) result.Append(' ');
            result.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }
        return result.ToString().ToLowerInvariant();
    }
}
