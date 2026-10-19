using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Inprem.Platform.Infrastructure.Twilio;

/// <summary>
/// The stand-in sender (Twilio:Mode = StandIn, the default): nothing leaves the machine. Each text is
/// saved as a .txt file in Twilio:StandInFolder when set (open it to read what would have been sent),
/// otherwise only noted in the log, without the number. It answers as Twilio would, with an id, so the
/// outbox records it as sent.
/// </summary>
public sealed class StandInSmsSender : ISmsSender
{
    private readonly TwilioOptions _options;
    private readonly ILogger<StandInSmsSender> _log;

    public StandInSmsSender(IOptions<TwilioOptions> options, ILogger<StandInSmsSender> log)
    {
        _options = options.Value;
        _log = log;
    }

    public bool IsStandIn => true;

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        var id = "SMstandin" + Guid.NewGuid().ToString("N");
        if (!string.IsNullOrWhiteSpace(_options.StandInFolder))
        {
            Directory.CreateDirectory(_options.StandInFolder);
            var file = Path.Combine(_options.StandInFolder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-sms-{toE164.TrimStart('+')}.txt");
            await File.WriteAllTextAsync(file, $"To: {toE164}\r\nId: {id}\r\nSaved: {DateTime.UtcNow:u}\r\n\r\n{body}\r\n", Encoding.UTF8, ct).ConfigureAwait(false);
        }
        _log.LogInformation("Stand-in text to {Phone} ({Length} characters): not sent, Twilio is not set up.", PhoneNumber.Mask(toE164), body.Length);
        return SmsSendResult.Ok(id);
    }
}

/// <summary>
/// Texts through Twilio's Messaging Service (Twilio:Mode = Twilio): POST /2010-04-01/Accounts/{sid}/Messages.json
/// with the Messaging Service, the number, the text and the status callback address. Twilio's answer
/// decides what happens next: accepted (its id is kept), a temporary problem (tried again later), or
/// a refusal (failed; error 21610 means the number replied STOP).
/// </summary>
public sealed class TwilioSmsSender : ISmsSender
{
    public const string HttpClientName = "twilio";
    public const string StatusCallbackPath = "/webhooks/twilio/status";

    private readonly IHttpClientFactory _http;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsSender> _log;

    public TwilioSmsSender(IHttpClientFactory http, IOptions<TwilioOptions> options, ILogger<TwilioSmsSender> log)
    {
        _http = http;
        _options = options.Value;
        _log = log;
    }

    public bool IsStandIn => false;

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid!)}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["MessagingServiceSid"] = _options.MessagingServiceSid!,
                ["To"] = toE164,
                ["Body"] = body,
                ["StatusCallback"] = new Uri(_options.WebhookBaseUrl!, StatusCallbackPath).ToString(),
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}")));

        HttpResponseMessage response;
        try
        {
            response = await _http.CreateClient(HttpClientName).SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("Twilio could not be reached ({Error}) for a text to {Phone}.", e.GetType().Name, PhoneNumber.Mask(toE164));
            return SmsSendResult.Fail("Twilio could not be reached.", temporary: true);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode && Read(json, "sid") is { Length: > 0 } sid)
            {
                _log.LogInformation("Twilio accepted text {Sid} to {Phone}.", sid, PhoneNumber.Mask(toE164));
                return SmsSendResult.Ok(sid);
            }

            var code = Read(json, "code");
            var temporary = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500;
            _log.LogWarning("Twilio refused a text to {Phone}: HTTP {Status}, error {Code}.", PhoneNumber.Mask(toE164), (int)response.StatusCode, code ?? "(none)");
            return SmsSendResult.Fail($"Twilio refused the text (HTTP {(int)response.StatusCode}, error {code ?? "unknown"}).", temporary, optedOut: code == "21610");
        }
    }

    /// <summary>A top-level value of Twilio's JSON answer as text, or null.</summary>
    private static string? Read(string json, string name)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Null ? value.ToString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
