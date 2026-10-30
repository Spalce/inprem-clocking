using System.Security.Claims;
using Inprem.Platform.Application.Provider;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InpremClockingApp.Provider;

public enum ProviderAccessLevel
{
    /// <summary>No sign-in needed (login, setup, invitation and approval links).</summary>
    Anonymous,

    /// <summary>A signed-in provider user who may not have two-factor yet (the enrolment page).</summary>
    SignedIn,

    /// <summary>The console: provider user, two-factor enrolled, and this sign-in used two-factor. The default.</summary>
    Console,
}

/// <summary>Marks a Provider page that needs less than full console access.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ProviderAccessAttribute : Attribute
{
    public ProviderAccessAttribute(ProviderAccessLevel level) => Level = level;

    public ProviderAccessLevel Level { get; }
}

/// <summary>
/// Gate for every page in the Provider area (registered on the area folder, so a page added later
/// is covered without doing anything). Design 5.1/5.5, D14: no console page until the person has
/// set a password, enrolled an authenticator app and signed in with it. A revoked session (security
/// stamp changed) or a locked account is signed out; a tenant account or a disabled provider user
/// is sent to the provider login.
/// </summary>
public sealed class ProviderAccessFilter : IAsyncPageFilter
{
    /// <summary>Set by Identity when the sign-in used a second factor.</summary>
    public const string MfaClaimType = "amr";
    public const string MfaClaimValue = "mfa";

    /// <summary>On a session started with a recovery code (Level 1): only enrolment until a new authenticator is confirmed.</summary>
    public const string ReEnrolClaimType = "provider_reenrol";

    private readonly ProviderAccessCheck _access;
    private readonly SignInManager<AppUser> _signIn;

    public ProviderAccessFilter(ProviderAccessCheck access, SignInManager<AppUser> signIn)
    {
        _access = access;
        _signIn = signIn;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        // Setup, invitation and approval links carry one-time tokens in the address: never send it on.
        context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";

        // [ProviderAccess] says it explicitly; otherwise [AllowAnonymous] pages are open and every
        // other page is the console.
        var handlerType = context.ActionDescriptor.HandlerTypeInfo;
        var level = handlerType.GetCustomAttributes(typeof(ProviderAccessAttribute), true).OfType<ProviderAccessAttribute>().FirstOrDefault()?.Level
            ?? (handlerType.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), true) ? ProviderAccessLevel.Anonymous : ProviderAccessLevel.Console);
        var (outcome, _) = await _access.CheckAsync(context.HttpContext.User, level);
        switch (outcome)
        {
            case ProviderAccessOutcome.Allowed:
                await next();
                return;
            case ProviderAccessOutcome.Enrol:
                context.Result = new RedirectToPageResult("/EnrolTwoFactor", new { area = "Provider" });
                return;
            case ProviderAccessOutcome.PasswordChange:
                context.Result = new RedirectToPageResult("/ChangePassword", new { area = "Provider" });
                return;
            case ProviderAccessOutcome.SignOut:
                await _signIn.SignOutAsync();
                break;
        }
        context.Result = new RedirectToPageResult("/Login", new { area = "Provider" });
    }
}

public enum ProviderAccessOutcome
{
    Allowed,

    /// <summary>Not a usable provider user (e.g. a tenant account): to the provider login, session kept.</summary>
    Login,

    /// <summary>Revoked, disabled or locked session, or a console request without two-factor: sign out, then login.</summary>
    SignOut,

    /// <summary>Two-factor not enrolled, or re-enrolment due after a recovery code (Level 1).</summary>
    Enrol,

    PasswordChange,
}

/// <summary>
/// The provider access rules (D14, design 5.5), shared by the Provider pages and the jobs
/// dashboard. The security stamp is checked on every request (not Identity's 30-minute check): a
/// recovery, reset or disable rotates it, and every console session ends at once.
/// </summary>
public sealed class ProviderAccessCheck
{
    private readonly ProviderSecurityService _provider;
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;

    public ProviderAccessCheck(ProviderSecurityService provider, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        _provider = provider;
        _users = users;
        _signIn = signIn;
    }

    public async Task<(ProviderAccessOutcome Outcome, Inprem.Platform.Domain.Provider.ProviderUser? User)> CheckAsync(ClaimsPrincipal principal, ProviderAccessLevel level)
    {
        if (level == ProviderAccessLevel.Anonymous) return (ProviderAccessOutcome.Allowed, null);

        var userId = principal.Identity?.IsAuthenticated == true ? principal.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        if (userId == null) return (ProviderAccessOutcome.Login, null);

        var user = await _signIn.ValidateSecurityStampAsync(principal);
        if (user == null || await _users.IsLockedOutAsync(user)) return (ProviderAccessOutcome.SignOut, null);

        var provider = await _provider.FindUsableProviderUserAsync(user.Id);
        if (provider == null) return (ProviderAccessOutcome.Login, null);

        if (level == ProviderAccessLevel.Console && user.RequiresPasswordChange)
            return (ProviderAccessOutcome.PasswordChange, provider);

        if (level == ProviderAccessLevel.Console)
        {
            if (!await _users.GetTwoFactorEnabledAsync(user) || principal.HasClaim(c => c.Type == ProviderAccessFilter.ReEnrolClaimType))
                return (ProviderAccessOutcome.Enrol, provider);
            if (!principal.HasClaim(ProviderAccessFilter.MfaClaimType, ProviderAccessFilter.MfaClaimValue))
                return (ProviderAccessOutcome.SignOut, provider);
        }

        return (ProviderAccessOutcome.Allowed, provider);
    }
}
