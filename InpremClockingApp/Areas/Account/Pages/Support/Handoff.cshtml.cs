using System.Security.Claims;
using Inprem.Platform.Application.Support;
using Inprem.Platform.Application.Tenancy;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Account.Pages.Support;

/// <summary>
/// The support hand-off on the App site (step 1.5): the single-use, 60-second link from the
/// Provider Console. Opening it only shows what will happen (link scanners cannot use it); the
/// button signs in as admin of that organization for the session.
/// </summary>
[AllowAnonymous]
public class HandoffModel : PageModel
{
    private readonly SupportSessionService _support;
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;

    public HandoffModel(SupportSessionService support, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        _support = support;
        _users = users;
        _signIn = signIn;
    }

    [BindProperty(SupportsGet = true)] public string Token { get; set; } = "";
    public ActiveSupport? Session { get; private set; }

    public async Task OnGetAsync()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Session = await _support.PeekAsync(Token, HttpContext.RequestAborted);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var session = await _support.RedeemAsync(Token, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", HttpContext.RequestAborted);
        var user = session == null ? null : await _users.FindByIdAsync(session.ProviderUserId);
        if (session == null || user == null) return Page(); // Session stays null: "link not valid"

        await _signIn.SignOutAsync();
        await _signIn.SignInWithClaimsAsync(user, isPersistent: false, new[]
        {
            new Claim(PlatformClaimTypes.TenantId, session.TenantId.ToString()),
            new Claim(SupportSessions.SessionIdClaim, session.SessionId.ToString()),
            new Claim(ClaimTypes.Role, IdentitySeeder.AdminRole),
        });
        return LocalRedirect("/BackOffice");
    }
}
