using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Inprem.Platform.Application.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace InpremClockingApp.Areas.Account.Pages.Support;

/// <summary>Ends the current support session (banner button) and signs out.</summary>
[AllowAnonymous]
public class EndModel : PageModel
{
    private readonly SupportSessionService _support;
    private readonly SignInManager<AppUser> _signIn;

    public EndModel(SupportSessionService support, SignInManager<AppUser> signIn)
    {
        _support = support;
        _signIn = signIn;
    }

    public IActionResult OnGet() => Redirect(SupportSessions.EndedPath);

    public async Task<IActionResult> OnPostAsync()
    {
        if (Guid.TryParse(User.FindFirstValue(SupportSessions.SessionIdClaim), out var sessionId))
        {
            await _support.EndAsync(sessionId, $"provider:{User.FindFirstValue(ClaimTypes.NameIdentifier)}", HttpContext.RequestAborted);
            await _signIn.SignOutAsync();
        }
        return Redirect(SupportSessions.EndedPath);
    }
}
