using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Infrastructure.Twilio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Notifications;

/// <summary>
/// Twilio's reports (Phase 5 step 5.1) on the App site: <c>POST /webhooks/twilio/status</c> (a text's
/// delivery status) and <c>POST /webhooks/twilio/inbound</c> (a reply to Inprem's number: STOP, START,
/// HELP). Each request must carry Twilio's signature, computed over Twilio:WebhookBaseUrl and the path,
/// so a forged report is refused (403) and changes nothing. No cookie, no antiforgery token: the
/// signature is the proof. Bodies hold phone numbers, so they are never logged.
/// </summary>
[ApiController]
[Route("webhooks/twilio")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class TwilioWebhookController : ControllerBase
{
    public const int MaxBodyBytes = 64 * 1024;

    private readonly TwilioReports _reports;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioWebhookController> _log;

    public TwilioWebhookController(TwilioReports reports, IOptions<TwilioOptions> options, ILogger<TwilioWebhookController> log)
    {
        _reports = reports;
        _options = options.Value;
        _log = log;
    }

    [HttpPost("status")]
    [RequestSizeLimit(MaxBodyBytes)]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> StatusAsync()
    {
        var form = await VerifiedFormAsync();
        if (form == null) return StatusCode(StatusCodes.Status403Forbidden);
        await _reports.ApplyStatusAsync(form.GetValueOrDefault("MessageSid"), form.GetValueOrDefault("MessageStatus"),
            form.GetValueOrDefault("ErrorCode"), form.GetValueOrDefault("Price"), HttpContext.RequestAborted);
        return NoContent();
    }

    [HttpPost("inbound")]
    [RequestSizeLimit(MaxBodyBytes)]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> InboundAsync()
    {
        var form = await VerifiedFormAsync();
        if (form == null) return StatusCode(StatusCodes.Status403Forbidden);
        await _reports.InboundAsync(form.GetValueOrDefault("From"), form.GetValueOrDefault("Body"), HttpContext.RequestAborted);
        // An empty answer: Twilio's Advanced Opt-Out replies to STOP, START and HELP itself.
        return Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>", "text/xml");
    }

    /// <summary>The form when Twilio's signature matches, otherwise null (and a warning without the body).</summary>
    private async Task<Dictionary<string, string>?> VerifiedFormAsync()
    {
        var form = (await Request.ReadFormAsync(HttpContext.RequestAborted)).ToDictionary(f => f.Key, f => f.Value.ToString(), StringComparer.Ordinal);
        var signature = Request.Headers["X-Twilio-Signature"].ToString();
        var url = _options.WebhookBaseUrl == null ? null : new Uri(_options.WebhookBaseUrl, Request.Path + Request.QueryString).ToString();
        if (url != null && TwilioSignature.IsValid(_options.AuthToken, url, form, signature)) return form;

        _log.LogWarning("Twilio report refused: {Reason} ({Path}, from {RemoteIp}).",
            url == null ? "Twilio:WebhookBaseUrl is not set" : string.IsNullOrEmpty(signature) ? "no signature" : "wrong signature",
            Request.Path.Value, HttpContext.Connection.RemoteIpAddress);
        return null;
    }
}
