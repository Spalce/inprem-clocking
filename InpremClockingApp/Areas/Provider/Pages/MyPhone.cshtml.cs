using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>
/// "My phone for codes" (Phase 5 step 5.2, D14): a console user adds a mobile number, proves it with a
/// texted code, and can then sign in with a texted code instead of the authenticator app. Changing it
/// is a sensitive action: a recent two-factor code first (design 5.5). Removing it needs the same.
/// </summary>
[Authorize(Policy = ProviderPolicies.Support)]
[RequireRecentTwoFactor]
public class MyPhoneModel : PageModel
{
    private readonly UserManager<AppUser> _users;
    private readonly LoginCodeService _codes;

    public MyPhoneModel(UserManager<AppUser> users, LoginCodeService codes)
    {
        _users = users;
        _codes = codes;
    }

    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? Code { get; set; }

    public string? Confirmed { get; private set; }
    public bool AwaitingCode { get; private set; }
    public bool IsStandIn => _codes.IsStandIn;
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        Load(user);
        Message = TempData["Message"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        Load(user);
        var sent = await _codes.SendAsync(user.Id, Phone, LoginCodePurposes.ConfirmPhone, HttpContext.RequestAborted);
        if (!sent.Sent)
        {
            Error = sent.Message;
            return Page();
        }
        Phone = PhoneNumber.ToE164(Phone);
        AwaitingCode = true;
        Message = sent.Message;
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        Load(user);
        var phone = PhoneNumber.ToE164(Phone);
        var result = await _codes.CheckAsync(user.Id, phone, Code, LoginCodePurposes.ConfirmPhone, HttpContext.RequestAborted);
        if (result != OtpCheck.Approved || phone == null)
        {
            AwaitingCode = result is OtpCheck.Wrong;
            Phone = phone;
            Error = result switch
            {
                OtpCheck.Wrong => "The code is not valid.",
                OtpCheck.ExpiredOrUsed => "This code has expired or was already used. Send a new one.",
                OtpCheck.TooManyAttempts => "Too many wrong codes. Wait 15 minutes.",
                _ => "The code could not be checked just now. Try again in a minute.",
            };
            return Page();
        }

        user.PhoneNumber = phone;
        user.PhoneNumberConfirmed = true;
        var updated = await _users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            Error = "The phone could not be saved. Try again.";
            return Page();
        }
        await _codes.RecordPhoneChangeAsync(user.Id, phone, confirmed: true, HttpContext.RequestAborted);
        TempData["Message"] = $"Your phone {PhoneNumber.Mask(phone)} is confirmed. When you sign in, you can choose a texted code instead of the authenticator app.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoveAsync()
    {
        var user = await _users.GetUserAsync(User);
        if (user == null) return RedirectToPage("/Login", new { area = "Provider" });
        var old = user.PhoneNumber;
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        await _users.UpdateAsync(user);
        await _codes.RecordPhoneChangeAsync(user.Id, old, confirmed: false, HttpContext.RequestAborted);
        TempData["Message"] = "Your phone was removed. Sign in with the authenticator app.";
        return RedirectToPage();
    }

    private void Load(AppUser user) =>
        Confirmed = user.PhoneNumberConfirmed ? PhoneNumber.Mask(PhoneNumber.ToE164(user.PhoneNumber)) : null;
}
