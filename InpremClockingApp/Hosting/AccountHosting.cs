using InpremClockingApp.Services;
using Microsoft.AspNetCore.Identity;
using PlatformEmailSender = Inprem.Platform.Application.Memberships.IEmailSender;

namespace InpremClockingApp.Hosting;

/// <summary>Account security (Phase 0 step 0.4).</summary>
public static class AccountHosting
{
    public static WebApplicationBuilder AddAccountSecurity(this WebApplicationBuilder builder)
    {
        // C3-7: 5 failed sign-ins lock the account for 15 minutes (the login page passes
        // lockoutOnFailure: true). New accounts can be locked (Identity's default, stated here).
        builder.Services.Configure<IdentityOptions>(options =>
        {
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.AllowedForNewUsers = true;
        });

        builder.Services.AddScoped<PlatformEmailSender, ApprovedAppEmailSender>();

        // Step 0.5: account lookups by id/name/email see past the fail-closed AppUser filter
        // (AppUserStore); replaces the store AddEntityFrameworkStores registered.
        builder.Services.AddScoped<IUserStore<Models.Identity.AppUser>, Data.AppUserStore>();
        return builder;
    }
}

/// <summary>
/// The platform's IEmailSender, sending through the approved app's EmailService (the same mailbox
/// that sends password resets today), so invitations reach people now. Swapped for a configured
/// sender when email settings move out of source code (see the EmailService follow-up).
/// </summary>
public sealed class ApprovedAppEmailSender : PlatformEmailSender
{
    private readonly EmailService _emailService;

    public ApprovedAppEmailSender(EmailService emailService) => _emailService = emailService;

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default) =>
        _emailService.SendEmail(to, subject, htmlBody);
}
