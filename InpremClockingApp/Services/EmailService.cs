using System.Net;
using System.Net.Mail;
using InpremClockingApp.Hosting;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Services;

/// <summary>Configuration section <c>Email</c>: host settings only, never in the repository.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string? SmtpHost { get; set; }

    /// <summary>587 (STARTTLS). Port 465 (implicit TLS) is not supported by SmtpClient.</summary>
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }

    /// <summary>The sender address, e.g. no-reply@inpremclockin.org.</summary>
    public string? From { get; set; }

    /// <summary>Development only: save emails as .html files in this folder instead of sending them.</summary>
    public string? DropFolder { get; set; }
}

// Sends the app's emails (password reset, invitations, provider setup and recovery).
// Multi-tenancy Phase 0 (P0-5): the SMTP account that used to be written here was removed; the
// settings now come from configuration (Email:*), set per environment by whoever deploys.
public class EmailService
{
    private readonly EmailOptions _options;
    private readonly IWebHostEnvironment _environment;

    public EmailService(IOptions<EmailOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public async Task SendEmail(string email, string subject, string message)
    {
        if (_environment.IsDevelopment() && !string.IsNullOrWhiteSpace(_options.DropFolder))
        {
            await new FolderEmailSender(_options.DropFolder).SendAsync(email, subject, message);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.SmtpHost) || string.IsNullOrWhiteSpace(_options.From))
        {
            throw new InvalidOperationException(
                "Email is not configured: set Email:SmtpHost and Email:From (and Email:UserName / Email:Password) in the host's settings.");
        }

        if (_options.SmtpPort == 465)
        {
            throw new InvalidOperationException(
                "Email:SmtpPort 465 (implicit TLS) is not supported: use 587 (STARTTLS), which the mail provider normally also offers.");
        }

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.EnableSsl,
            Timeout = 30_000,
            Credentials = string.IsNullOrWhiteSpace(_options.UserName) ? null : new NetworkCredential(_options.UserName, _options.Password),
        };
        using var mail = new MailMessage(_options.From, email, subject, message) { IsBodyHtml = true };
        await client.SendMailAsync(mail);
    }
}
