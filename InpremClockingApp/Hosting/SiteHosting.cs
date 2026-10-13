using Inprem.Platform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Hosting;

/// <summary>
/// Two-site hosting (design section 2, step 0.2): which pages and controllers this deployment
/// serves, on which host, with which cookie and data-protection application name.
/// </summary>
public static class SiteHosting
{
    public const string AppCookieName = ".Inprem.App";
    public const string AdminCookieName = ".Inprem.Admin";
    public const string AppDataProtectionName = "Inprem.App";
    public const string AdminDataProtectionName = "Inprem.Admin";

    /// <summary>Call after AddDefaultIdentity so the cookie settings here win.</summary>
    public static WebApplicationBuilder AddSiteHosting(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var section = builder.Configuration.GetSection(SiteOptions.SectionName);

        services.AddSingleton<IValidateOptions<SiteOptions>, SiteOptionsValidator>();
        services.AddOptions<SiteOptions>().Bind(section).ValidateOnStart();

        services.AddSingleton<IConfigureOptions<RazorPagesOptions>, ConfigureSiteRouting>();
        services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.Mvc.MvcOptions>, ConfigureSiteRouting>();

        // Host-only (no Domain), so the App and Admin sites never share a sign-in.
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IOptions<SiteOptions>>((cookie, site) =>
            {
                var adminOnly = site.Value.SiteMode == SiteMode.Admin;
                cookie.Cookie.Name = adminOnly ? AdminCookieName : AppCookieName;
                cookie.Cookie.Domain = null;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookie.Cookie.SameSite = adminOnly ? SameSiteMode.Strict : SameSiteMode.Lax;
            });

        // Keys in the database (platform.DataProtectionKeys) so every instance of a site, and
        // every redeployment, can read the cookies; a separate application name per site, so the
        // Admin site cannot read App cookies and vice versa.
        // (The name is set through the options rather than SetApplicationName so it follows the
        // final, validated configuration, not whatever was loaded when services were registered.)
        services.AddDataProtection().PersistKeysToDbContext<PlatformDbContext>();
        services.AddOptions<DataProtectionOptions>()
            .Configure<IOptions<SiteOptions>>((dataProtection, site) =>
                dataProtection.ApplicationDiscriminator =
                    site.Value.SiteMode == SiteMode.Admin ? AdminDataProtectionName : AppDataProtectionName);

        return builder;
    }
}
