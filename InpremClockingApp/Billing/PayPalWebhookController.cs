using Hangfire;
using Inprem.Platform.Application.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InpremClockingApp.Billing;

/// <summary>
/// PayPal webhooks (Phase 2 step 2.5): <c>POST /webhooks/paypal</c> on the App site. Verified with
/// PayPal's API (never trusted as sent), stored once by PayPal's event id, answered at once, and
/// processed by a background job. No cookie, no antiforgery token: PayPal's signature is the proof.
/// </summary>
[ApiController]
[Route("webhooks/paypal")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class PayPalWebhookController : ControllerBase
{
    /// <summary>PayPal events are a few kilobytes; anything far larger is not one.</summary>
    public const int MaxBodyBytes = 256 * 1024;

    private readonly BillingWebhookService _webhooks;
    private readonly ILogger<PayPalWebhookController> _log;

    public PayPalWebhookController(BillingWebhookService webhooks, ILogger<PayPalWebhookController> log)
    {
        _webhooks = webhooks;
        _log = log;
    }

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    [Consumes("application/json")]
    public async Task<IActionResult> ReceiveAsync([FromServices] Inprem.Platform.Application.Notifications.BillingAlerts alerts, [FromServices] Inprem.Platform.Application.Memberships.IClock clock)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(HttpContext.RequestAborted);
        var headers = Request.Headers.Where(h => h.Key.StartsWith("PAYPAL-", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        var outcome = await _webhooks.ReceiveAsync(new WebhookMessage(headers, body), HttpContext.RequestAborted);
        if (outcome == WebhookOutcome.Rejected)
        {
            // Suspicious: never the body (payer details), only where it came from.
            _log.LogWarning("PayPal webhook rejected: not verified (transmission {TransmissionId}, from {RemoteIp}).",
                headers.GetValueOrDefault("PAYPAL-TRANSMISSION-ID") ?? "(none)", HttpContext.Connection.RemoteIpAddress);
            // Phase 5 step 5.3: Inprem's Owners hear about it (urgent; at most one alert an hour).
            await alerts.SystemProblemAsync("webhook-rejected", "a PayPal webhook failed verification",
                $"A request to /webhooks/paypal from {HttpContext.Connection.RemoteIpAddress} could not be verified with PayPal and was refused. One is harmless; many may mean someone is probing, or the PayPal webhook id setting is wrong.",
                clock.UtcNow, HttpContext.RequestAborted);
            return BadRequest();
        }
        return Ok();
    }
}

/// <summary>Queues stored PayPal events for <see cref="BillingEventJob"/> (Hangfire).</summary>
public sealed class HangfireBillingEventQueue : IBillingEventQueue
{
    private readonly IBackgroundJobClient _jobs;

    public HangfireBillingEventQueue(IBackgroundJobClient jobs) => _jobs = jobs;

    public void Enqueue(string externalEventId) => _jobs.Enqueue<BillingEventJob>(job => job.RunAsync(externalEventId, CancellationToken.None));
}

/// <summary>
/// The job that processes one PayPal event. Hangfire retries a failure (the error is kept on the
/// event and shown in the console, with "Replay").
/// </summary>
public sealed class BillingEventJob
{
    private readonly BillingEventProcessor _processor;

    public BillingEventJob(BillingEventProcessor processor) => _processor = processor;

    [AutomaticRetry(Attempts = 5, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    [JobDisplayName("PayPal event {0}")]
    public Task RunAsync(string externalEventId, CancellationToken cancellationToken) => _processor.ProcessAsync(externalEventId, cancellationToken);
}
