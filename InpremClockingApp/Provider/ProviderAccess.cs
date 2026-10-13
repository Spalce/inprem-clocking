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

    private readonly ProviderSecurityService _provider;
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;

    public ProviderAccessFilter(ProviderSecurityService provider, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        _provider = provider;
        _users = users;
        _signIn = signIn;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        // Setup, invitation and approval links carry one-time tokens in the address: never send it on.
        context.HttpContext.Response.Headers["Referrer-Policy"] = "no-referrer";

        var level = context.ActionDescriptor.HandlerTypeInfo.GetCustomAttributes(typeof(ProviderAccessAttribute), true)
            .OfType<ProviderAccessAttribute>().FirstOrDefault()?.Level ?? ProviderAccessLevel.Console;
        if (level == ProviderAccessLevel.Anonymous)
        {
            await next();
            return;
        }

        var principal = context.HttpContext.User;
        var userId = principal.Identity?.IsAuthenticated == true ? principal.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        // ValidateSecurityStampAsync on every request (not Identity's 30-minute check): a recovery,
        // reset or disable rotates the stamp, and every console session ends at once.
        var user = userId == null ? null : await _signIn.ValidateSecurityStampAsync(principal);
        if (user == null || await _users.IsLockedOutAsync(user))
        {
            // Revoked, disabled or locked: this session ends here.
            if (userId != null) await _signIn.SignOutAsync();
            context.Result = new RedirectToPageResult("/Login", new { area = "Provider" });
            return;
        }

        if (await _provider.FindUsableProviderUserAsync(user.Id) == null)
        {
            // A tenant account (or a disabled provider user): no console. Not signed out, so a tenant
            // admin who opens a console address on a combined site keeps their App session.
            context.Result = new RedirectToPageResult("/Login", new { area = "Provider" });
            return;
        }

        if (level == ProviderAccessLevel.Console)
        {
            if (!await _users.GetTwoFactorEnabledAsync(user!) || principal.HasClaim(c => c.Type == ReEnrolClaimType))
            {
                context.Result = new RedirectToPageResult("/EnrolTwoFactor", new { area = "Provider" });
                return;
            }

            if (!principal.HasClaim(MfaClaimType, MfaClaimValue))
            {
                await _signIn.SignOutAsync();
                context.Result = new RedirectToPageResult("/Login", new { area = "Provider" });
                return;
            }
        }

        await next();
    }
}
