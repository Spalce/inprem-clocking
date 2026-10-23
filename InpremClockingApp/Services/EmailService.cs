using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Inprem.Platform.Infrastructure.Twilio;
using InpremClockingApp.Hosting;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Services;

public enum EmailProvider
{
    /// <summary>The mail account in Email:SmtpHost, UserName, Password.</summary>
    Smtp,

    /// <summary>Twilio's Email API (POST /v1/Emails), with the Twilio API key (Twilio:ApiKeySid, ApiKeySecret) or Twilio:AccountSid and AuthToken.</summary>
    Twilio,
}

/// <summary>Configuration section <c>Email</c>: host settings only, never in the repository.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailProvider Provider { get; set; } = EmailProvider.Smtp;

    public string? SmtpHost { get; set; }

    /// <summary>587 (STARTTLS). Port 465 (implicit TLS) is not supported by SmtpClient.</summary>
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }

    /// <summary>The sender address, e.g. no-reply@inpremclockin.org.</summary>
    public string? From { get; set; }

    /// <summary>Twilio only: the sender's name shown in the inbox.</summary>
    public string FromName { get; set; } = "Inprem Clocking";

    public Uri TwilioApiBaseAddress { get; set; } = new("https://comms.twilio.com/");

    /// <summary>Development only: save emails as .html files in this folder instead of sending them.</summary>
    public string? DropFolder { get; set; }
}

// Sends the app's emails (password reset, invitations, provider setup and recovery).
// Multi-tenancy Phase 0 (P0-5): the SMTP account that used to be written here was removed; the
// settings now come from configuration (Email:*), set per environment by whoever deploys.
// Email:Provider = Twilio sends through Twilio's Email API instead, with the Twilio account that also sends the
// texts: its API key (Twilio:ApiKeySid, ApiKeySecret) when set, otherwise Twilio:AccountSid and AuthToken.
public class EmailService
{
    public const string TwilioHttpClientName = "twilio-email";

    private readonly EmailOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _http;
    private readonly TwilioOptions _twilio;

    public EmailService(IOptions<EmailOptions> options, IWebHostEnvironment environment, IHttpClientFactory http, IOptions<TwilioOptions> twilio)
    {
        _options = options.Value;
        _environment = environment;
        _http = http;
        _twilio = twilio.Value;
    }

    public async Task SendEmail(string email, string subject, string message)
    {
        if (_environment.IsDevelopment() && !string.IsNullOrWhiteSpace(_options.DropFolder))
        {
            await new FolderEmailSender(_options.DropFolder).SendAsync(email, subject, message);
            return;
        }

        if (_options.Provider == EmailProvider.Twilio)
        {
            await SendWithTwilio(email, subject, message);
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

    // Twilio answers 202 (accepted; it sends shortly after). Anything else is a failure, reported without the
    // address or Twilio's message text, which can repeat it.
    private async Task SendWithTwilio(string email, string subject, string message)
    {
        var signIn = _twilio.HasApiKey || (!string.IsNullOrWhiteSpace(_twilio.AccountSid) && !string.IsNullOrWhiteSpace(_twilio.AuthToken));
        if (!signIn || string.IsNullOrWhiteSpace(_options.From))
        {
            throw new InvalidOperationException(
                "Email is not configured: with Email:Provider = Twilio, set Email:From and either Twilio:ApiKeySid and Twilio:ApiKeySecret, or Twilio:AccountSid and Twilio:AuthToken, in the host's settings.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.TwilioApiBaseAddress, "v1/Emails"))
        {
            Content = JsonContent.Create(new
            {
                from = new { address = _options.From, name = _options.FromName },
                to = new[] { new { address = email } },
                content = new { subject, html = message },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _twilio.BasicCredentials());

        using var response = await _http.CreateClient(TwilioHttpClientName).SendAsync(request);
        if (response.IsSuccessStatusCode) return;

        string? code = null;
        try
        {
            using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (answer.RootElement.ValueKind == JsonValueKind.Object && answer.RootElement.TryGetProperty("code", out var value)) code = value.ToString();
        }
        catch (JsonException)
        {
        }
        throw new InvalidOperationException($"Twilio refused the email (HTTP {(int)response.StatusCode}, error {code ?? "unknown"}).");
    }
}
