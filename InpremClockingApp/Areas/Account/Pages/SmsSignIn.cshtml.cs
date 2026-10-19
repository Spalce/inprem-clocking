using System.Net;
using System.Security.Claims;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Notifications;
using InpremClockingApp.Hosting;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PlatformEmailSender = Inprem.Platform.Application.Memberships.IEmailSender;

namespace InpremClockingApp.Areas.Account.Pages;

/// <summary>
/// "Enter the code we texted you" (Phase 5 step 5.2): asked by <see cref="SmsSignInGate"/> after the
/// password when the organization switched texted sign-in codes on. Someone without a confirmed
/// mobile number adds one here and proves it with the same code; they are emailed that a number
/// was added, so a stranger who learnt the password cannot add theirs unnoticed.
/// </summary>
[Authorize]
public class SmsSignInModel : PageModel
{
    private readonly SignInManager<AppUser> _signIn;
    private readonly LoginCodeService _codes;
    private readonly ITenantContext _tenant;
    private readonly PlatformEmailSender _email;

    public SmsSignInModel(SignInManager<AppUser> signIn, LoginCodeService codes, ITenantContext tenant, PlatformEmailSender email)
    {
        _signIn = signIn;
        _codes = codes;
        _tenant = tenant;
        _email = email;
    }

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Code { get; set; }

    /// <summary>The confirmed number, masked; null when the person has none yet.</summary>
    public string? Confirmed { get; private set; }
    public bool CodeSent { get; private set; }
    public bool IsStandIn => _codes.IsStandIn;
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _signIn.UserManager.GetUserAsync(User);
        if (user == null || _tenant.MembershipId == null) return Redirect("~/Identity/Account/Login");
        Load(user);
        return Page();
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        var user = await _signIn.UserManager.GetUserAsync(User);
        if (user == null || _tenant.MembershipId == null) return Redirect("~/Identity/Account/Login");
        Load(user);
        var to = Confirmed != null ? user.PhoneNumber : Phone;
        var sent = await _codes.SendAsync(user.Id, to, LoginCodePurposes.AppSignIn, HttpContext.RequestAborted);
        if (sent.Sent)
        {
            CodeSent = true;
            Phone = Confirmed != null ? null : PhoneNumber.ToE164(Phone);
            Message = sent.Message;
        }
        else
        {
            Error = sent.Message;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _signIn.UserManager.GetUserAsync(User);
        if (user == null || _tenant.MembershipId is not Guid membershipId) return Redirect("~/Identity/Account/Login");
        Load(user);
        var newPhone = Confirmed == null ? PhoneNumber.ToE164(Phone) : null;
        var to = Confirmed != null ? user.PhoneNumber : newPhone;

        var result = await _codes.CheckAsync(user.Id, to, Code, LoginCodePurposes.AppSignIn, HttpContext.RequestAborted);
        if (result != OtpCheck.Approved || to == null)
        {
            CodeSent = result == OtpCheck.Wrong;
            Phone = newPhone;
            Error = result switch
            {
                OtpCheck.Wrong => "The code is not valid.",
                OtpCheck.ExpiredOrUsed => "This code has expired or was already used. Send a new one.",
                OtpCheck.TooManyAttempts => "Too many wrong codes. Wait 15 minutes, then send a new code.",
                _ => "The code could not be checked just now. Try again in a minute.",
            };
            return Page();
        }

        if (newPhone != null)
        {
            user.PhoneNumber = newPhone;
            user.PhoneNumberConfirmed = true;
            await _signIn.UserManager.UpdateAsync(user);
            await _codes.RecordPhoneChangeAsync(user.Id, newPhone, confirmed: true, HttpContext.RequestAborted);
            if (!string.IsNullOrEmpty(user.Email))
            {
                try
                {
                    await _email.SendAsync(user.Email, "A phone number was added to your Inprem Clock-in account",
                        $"<p>The mobile number {WebUtility.HtmlEncode(PhoneNumber.Mask(newPhone))} was added to your account for sign-in codes.</p>" +
                        "<p>If this was not you, change your password at once and tell your organization's owner.</p>", HttpContext.RequestAborted);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // The sign-in goes on; the audit log keeps the change.
                }
            }
        }

        // The proof, tied to this membership; the organization and membership claims stay as they are.
        var current = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var claims = new List<Claim> { new(SmsSignInGate.ClaimType, membershipId.ToString()) };
        foreach (var type in new[] { PlatformClaimTypes.TenantId, PlatformClaimTypes.MembershipId })
            if (User.FindFirst(type) is { } claim) claims.Add(new Claim(type, claim.Value));
        await _signIn.SignInWithClaimsAsync(user, current.Properties, claims);

        return LocalRedirect(!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) && !ReturnUrl.StartsWith(SmsSignInGate.CodePath, StringComparison.OrdinalIgnoreCase)
            ? ReturnUrl
            : Url.Content("~/VolunteerAttendance"));
    }

    private void Load(AppUser user)
    {
        Confirmed = user.PhoneNumberConfirmed && PhoneNumber.ToE164(user.PhoneNumber) is { } e164 ? PhoneNumber.Mask(e164) : null;
        Phone ??= Confirmed == null ? user.PhoneNumber : null;
    }
}
