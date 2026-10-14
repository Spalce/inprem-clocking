using Inprem.Platform.Application.Provider;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>The emailed setup or recovery link: set a password, then enrol two-factor.</summary>
[AllowAnonymous]
public class AcceptInvitationModel : PageModel
{
    private readonly ProviderSecurityService _provider;
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;
    private readonly ProviderSession _session;

    public AcceptInvitationModel(ProviderSecurityService provider, UserManager<AppUser> users, SignInManager<AppUser> signIn, ProviderSession session)
    {
        _provider = provider;
        _users = users;
        _signIn = signIn;
        _session = session;
    }

    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";
    [BindProperty] public string ConfirmPassword { get; set; } = "";

    public InvitationInfo? Invitation { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync() => Invitation = await _provider.FindInvitationAsync(Token, HttpContext.RequestAborted);

    public async Task<IActionResult> OnPostAsync()
    {
        Invitation = await _provider.FindInvitationAsync(Token, HttpContext.RequestAborted);
        if (Invitation == null) return Page();

        if (Password != ConfirmPassword)
        {
            Error = "The two passwords do not match.";
            return Page();
        }

        var (result, userId) = await _provider.AcceptInvitationAsync(Token, Password, HttpContext.RequestAborted);
        if (!result.Succeeded)
        {
            Error = result.Message;
            return Page();
        }

        await _signIn.SignOutAsync();
        var user = await _users.FindByIdAsync(userId!);
        await _session.SignInAsync(user!, twoFactor: false);
        return RedirectToPage("/EnrolTwoFactor", new { area = "Provider" });
    }
}
