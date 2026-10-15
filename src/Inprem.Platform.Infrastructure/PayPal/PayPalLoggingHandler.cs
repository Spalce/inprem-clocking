using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Inprem.Platform.Infrastructure.PayPal;

/// <summary>
/// Logs every PayPal call with method, path (no query), status, time and PayPal's debug id as the
/// correlation id. Never logs headers or bodies (tokens, payer details).
/// </summary>
public sealed class PayPalLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PayPalLoggingHandler> _log;

    public PayPalLoggingHandler(ILogger<PayPalLoggingHandler> log) => _log = log;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var path = request.RequestUri?.AbsolutePath ?? "";
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var debugId = response.Headers.TryGetValues("PayPal-Debug-Id", out var values) ? values.FirstOrDefault() : null;
            _log.LogInformation("PayPal {Method} {Path} -> {Status} in {Elapsed} ms (debug id {DebugId})",
                request.Method, path, (int)response.StatusCode, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, debugId);
            return response;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning("PayPal {Method} {Path} failed after {Elapsed} ms: {Error}",
                request.Method, path, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, e.GetType().Name);
            throw;
        }
    }
}
