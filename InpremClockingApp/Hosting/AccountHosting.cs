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

        // Email settings (Email:*) come from host configuration (P0-5). EmailService also handles
        // Email:DropFolder (Development only: emails saved as files), for every sender.
        builder.Services.AddOptions<Services.EmailOptions>().Bind(builder.Configuration.GetSection(Services.EmailOptions.SectionName));
        // Email:Provider = Twilio: one client, no retries (a retried send could email twice); the framework's
        // HttpClient logging writes headers, the Basic credentials included: off.
        builder.Services.AddHttpClient(EmailService.TwilioHttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30)).RemoveAllLoggers();
        builder.Services.AddScoped<PlatformEmailSender, ApprovedAppEmailSender>();

        // Step 0.5: account lookups by id/name/email see past the fail-closed AppUser filter
        // (AppUserStore); replaces the store AddEntityFrameworkStores registered.
        builder.Services.AddScoped<IUserStore<Models.Identity.AppUser>, Data.AppUserStore>();
        return builder;
    }
}

/// <summary>
/// The platform's IEmailSender: sends through the app's EmailService, so every email (approved
/// pages and platform) uses the same configured account (Email:*).
/// </summary>
public sealed class ApprovedAppEmailSender : PlatformEmailSender
{
    private readonly EmailService _emailService;

    public ApprovedAppEmailSender(EmailService emailService) => _emailService = emailService;

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default) =>
        _emailService.SendEmail(to, subject, htmlBody);
}

/// <summary>
/// Development only (Email:DropFolder, used by EmailService): saves each email as an .html file you can open in a
/// browser - recipient, subject and body, links clickable. Nothing leaves the machine.
/// </summary>
public sealed class FolderEmailSender : PlatformEmailSender
{
    private readonly string _folder;

    public FolderEmailSender(string folder) => _folder = folder;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_folder);
        var safeSubject = string.Concat(subject.Where(c => char.IsLetterOrDigit(c) || c == ' ')).Trim().Replace(' ', '-');
        var file = Path.Combine(_folder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{safeSubject}.html");
        var html = $"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>{System.Net.WebUtility.HtmlEncode(subject)}</title></head><body>" +
                   $"<p><b>To:</b> {System.Net.WebUtility.HtmlEncode(to)}<br><b>Subject:</b> {System.Net.WebUtility.HtmlEncode(subject)}</p><hr>{htmlBody}</body></html>";
        await File.WriteAllTextAsync(file, html, cancellationToken);
    }
}
