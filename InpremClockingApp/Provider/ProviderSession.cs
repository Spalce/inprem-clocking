using System.Globalization;
using System.Security.Claims;
using Inprem.Platform.Application.Memberships;
using InpremClockingApp.Hosting;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InpremClockingApp.Provider;

/// <summary>
/// Provider Console sessions (design 5.5, step 1.1): at most 8 hours, ended after 30 minutes
/// without a request, and sensitive actions need a two-factor code entered in the last 5 minutes.
/// The times travel in the sign-in cookie as claims, set only by <see cref="SignInAsync"/>.
/// </summary>
public sealed class ProviderSession
{
    public const string AuthTimeClaim = "provider_auth_time";
    public const string LastSeenClaim = "provider_last_seen";
    public const string TwoFactorTimeClaim = "provider_2fa_time";

    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan RecentTwoFactor = TimeSpan.FromMinutes(5);

    /// <summary>How often the last-seen time in the cookie is refreshed (not on every request).</summary>
    public static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(1);

    public static readonly string[] Claims = { AuthTimeClaim, LastSeenClaim, TwoFactorTimeClaim, ProviderAccessFilter.MfaClaimType, ProviderAccessFilter.ReEnrolClaimType };

    private readonly SignInManager<AppUser> _signIn;
    private readonly IClock _clock;

    public ProviderSession(SignInManager<AppUser> signIn, IClock clock)
    {
        _signIn = signIn;
        _clock = clock;
    }

    /// <summary>
    /// Every provider sign-in goes through here. <paramref name="twoFactor"/>: this sign-in proved a
    /// second factor now. A re-sign-in within the same session (<paramref name="current"/>) keeps
    /// the original start time, so the 8-hour limit cannot be extended, and keeps its two-factor
    /// proof (with its original time) unless a new one was given.
    /// </summary>
    public Task SignInAsync(AppUser user, bool twoFactor, ClaimsPrincipal? current = null, bool reEnrol = false)
    {
        var now = _clock.UtcNow;
        var authTime = ReadTime(current, AuthTimeClaim) ?? now;
        var claims = new List<Claim>
        {
            new(AuthTimeClaim, Write(authTime)),
            new(LastSeenClaim, Write(now)),
        };
        // A fresh second factor counts from now; otherwise a re-sign-in keeps the session's own proof.
        var twoFactorTime = twoFactor ? now
            : current?.HasClaim(ProviderAccessFilter.MfaClaimType, ProviderAccessFilter.MfaClaimValue) == true ? ReadTime(current, TwoFactorTimeClaim) : null;
        if (twoFactorTime != null)
        {
            claims.Add(new Claim(ProviderAccessFilter.MfaClaimType, ProviderAccessFilter.MfaClaimValue));
            claims.Add(new Claim(TwoFactorTimeClaim, Write(twoFactorTime.Value)));
        }
        if (reEnrol) claims.Add(new Claim(ProviderAccessFilter.ReEnrolClaimType, "1"));
        return _signIn.SignInWithClaimsAsync(user, isPersistent: false, claims);
    }

    public bool HasRecentTwoFactor(ClaimsPrincipal principal) =>
        ReadTime(principal, TwoFactorTimeClaim) is { } time && _clock.UtcNow - time <= RecentTwoFactor;

    public static DateTime? ReadTime(ClaimsPrincipal? principal, string type) =>
        principal?.FindFirst(type)?.Value is { } value && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            ? new DateTime(ticks, DateTimeKind.Utc)
            : null;

    private static string Write(DateTime time) => time.Ticks.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Cookie validation for provider sessions (cookies with <see cref="AuthTimeClaim"/>): ends
    /// sessions past 8 hours or idle for 30 minutes, and keeps the last-seen time current.
    /// Other sessions (organization admins) are not affected.
    /// </summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var authTime = ReadTime(principal, AuthTimeClaim);
        if (principal == null || authTime == null) return;

        var now = context.HttpContext.RequestServices.GetRequiredService<IClock>().UtcNow;
        var lastSeen = ReadTime(principal, LastSeenClaim) ?? authTime.Value;
        if (now - authTime.Value > MaxLifetime || now - lastSeen > IdleTimeout)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        if (now - lastSeen >= LastSeenResolution && principal.Identity is ClaimsIdentity identity)
        {
            var renewed = identity.Clone();
            foreach (var old in renewed.FindAll(LastSeenClaim).ToList()) renewed.RemoveClaim(old);
            renewed.AddClaim(new Claim(LastSeenClaim, Write(now)));
            context.ReplacePrincipal(new ClaimsPrincipal(renewed));
            context.ShouldRenew = true;
        }
    }

    /// <summary>Unauthenticated requests to the console go to the provider login, not the App site's.</summary>
    public static bool IsConsoleRequest(HttpContext context) =>
        string.Equals(context.GetRouteValue("area") as string, SiteMap.ProviderArea, StringComparison.OrdinalIgnoreCase)
        || context.GetEndpoint()?.Metadata.GetMetadata<ProviderEndpointMetadata>() != null;
}

/// <summary>
/// Sensitive console actions (design 5.5: refunds, deleting an organization, payment and SMS
/// settings, inviting provider users, resetting another owner): the POST handlers of a page with
/// this attribute need a two-factor code entered in the last 5 minutes, otherwise the person is
/// asked for one (Reauthenticate) and sent back to the page.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequireRecentTwoFactorAttribute : Attribute, IAsyncPageFilter, IOrderedFilter
{
    // After the access gate (which proves a two-factor session at all).
    public int Order => 100;

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
            || http.RequestServices.GetRequiredService<ProviderSession>().HasRecentTwoFactor(http.User))
        {
            await next();
            return;
        }

        var returnUrl = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
        context.Result = new RedirectToPageResult("/Reauthenticate", new { area = SiteMap.ProviderArea, returnUrl = returnUrl.ToString() });
    }
}
