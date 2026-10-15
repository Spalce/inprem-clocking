using System.Text;
using Inprem.Platform.Application.PublicApi;
using Inprem.Platform.Domain.PublicApi;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace InpremClockingApp.Areas.Account.Pages;

/// <summary>
/// A website signup's owner confirms their email and chooses a password (Phase 3 step 3.3, D22).
/// Only then can anyone sign in to the organization; the organization becomes Active.
/// </summary>
[AllowAnonymous]
public class VerifyEmailModel : PageModel
{
    private readonly SignupService _signups;
    private readonly UserManager<AppUser> _users;

    public VerifyEmailModel(SignupService signups, UserManager<AppUser> users)
    {
        _signups = signups;
        _users = users;
    }

    [BindProperty(SupportsGet = true)] public Guid Signup { get; set; }
    [BindProperty(SupportsGet = true)] public string Code { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";

    public string? Email { get; private set; }
    public string? Error { get; private set; }
    public bool Done { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var (signup, user) = await LoadAsync();
        if (signup == null || user == null) Error = "This link is not valid.";
        else if (signup.Status == SignupStatus.Verified) Done = true;
        else Email = user.Email;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var (signup, user) = await LoadAsync();
        if (signup == null || user == null)
        {
            Error = "This link is not valid.";
            return Page();
        }
        Email = user.Email;
        if (signup.Status == SignupStatus.Verified)
        {
            Done = true;
            return Page();
        }
        if (Password != ConfirmPassword)
        {
            Error = "The two passwords are not the same.";
            return Page();
        }

        // The password is checked before the email is confirmed, so a weak password does not use up the link.
        foreach (var validator in _users.PasswordValidators)
        {
            var check = await validator.ValidateAsync(_users, user, Password);
            if (!check.Succeeded)
            {
                Error = string.Join(" ", check.Errors.Select(e => e.Description));
                return Page();
            }
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Code));
        }
        catch (FormatException)
        {
            Error = "This link is not valid.";
            return Page();
        }
        var confirmed = await _users.ConfirmEmailAsync(user, token);
        if (!confirmed.Succeeded)
        {
            Error = "This link has expired or was already used. Ask for a new email on the website.";
            return Page();
        }
        var password = await _users.AddPasswordAsync(user, Password);
        if (!password.Succeeded)
        {
            Error = string.Join(" ", password.Errors.Select(e => e.Description));
            return Page();
        }

        await _signups.MarkVerifiedAsync(signup.Id, HttpContext.RequestAborted);
        Done = true;
        return Page();
    }

    private async Task<(Signup? Signup, AppUser? User)> LoadAsync()
    {
        var signup = Signup == Guid.Empty ? null : await _signups.FindAsync(Signup, HttpContext.RequestAborted);
        var user = signup == null ? null : await _users.FindByIdAsync(signup.OwnerUserId);
        return (signup, user);
    }
}
