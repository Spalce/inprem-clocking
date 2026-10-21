using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.PublicApi;
using Inprem.Platform.Domain.PublicApi;
using Microsoft.AspNetCore.DataProtection;

namespace InpremClockingApp.PublicApi;

/// <summary>
/// <c>Idempotency-Key</c> (design 7.7) for the endpoints that create something:
/// <list type="bullet">
/// <item>The header is required (8 to 100 characters).</item>
/// <item>The same key with the same body within 24 hours gets the first answer again, without
/// running the endpoint a second time.</item>
/// <item>The same key with another body gets 422 <c>idempotency_key_reused</c>.</item>
/// <item>A second request while the first is still running waits for it briefly, else 429.</item>
/// <item>Server errors (5xx) are not kept: the same key can safely be retried.</item>
/// </list>
/// Keys are per endpoint and per caller (the token, or "public" for endpoints without one). The
/// stored answer is encrypted with data protection: an answer can hold a new token (POST /signups).
/// </summary>
public sealed class IdempotencyFilter : IEndpointFilter
{
    public const string Header = "Idempotency-Key";
    private static readonly TimeSpan WaitForFirst = TimeSpan.FromSeconds(10);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString().Trim();
        if (key.Length is < 8 or > 100)
            return Problems.Validation(http, new Dictionary<string, string[]> { [Header] = new[] { "An Idempotency-Key header of 8 to 100 characters is required (for example a UUID)." } });

        var store = http.RequestServices.GetRequiredService<IPublicApiStore>();
        var clock = http.RequestServices.GetRequiredService<IClock>();
        var protector = http.RequestServices.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>().CreateProtector("PublicApi.Idempotency.v1");
        var scope = $"{http.Request.Method} {(http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText}|{http.User.FindFirstValue(PublicApiClaims.TokenId) ?? "public"}";
        var hash = await BodyHashAsync(http.Request);

        var record = IdempotencyRecord.Begin(scope, key, hash, clock.UtcNow);
        if (!await TryBeginAsync(store, record, clock, http.RequestAborted))
        {
            var deadline = DateTime.UtcNow + WaitForFirst;
            while (true)
            {
                var existing = await store.FindIdempotencyAsync(scope, key, clock.UtcNow, http.RequestAborted);
                if (existing == null)
                {
                    if (await TryBeginAsync(store, record, clock, http.RequestAborted)) break; // the first one failed and gave the key back
                    continue;
                }
                if (existing.RequestHash != hash)
                    return Problems.Create(http, StatusCodes.Status422UnprocessableEntity, Problems.IdempotencyKeyReused, "This Idempotency-Key was already used with a different request.");
                if (existing.IsComplete) return new StoredResult(existing.StatusCode!.Value, existing.ContentType, existing.ResponseBody == null ? "" : protector.Unprotect(existing.ResponseBody));
                if (DateTime.UtcNow > deadline)
                {
                    http.Response.Headers.RetryAfter = "1";
                    return Problems.Create(http, StatusCodes.Status429TooManyRequests, Problems.RateLimited, "The same request is still being processed. Try again in a moment.");
                }
                await Task.Delay(200, http.RequestAborted);
            }
        }

        // First time: run the endpoint, keep its answer.
        object? outcome;
        try
        {
            outcome = await next(context);
        }
        catch
        {
            await store.RemoveIdempotencyAsync(record, CancellationToken.None);
            throw;
        }

        var (status, contentType, body) = await CaptureAsync(http, outcome);
        var kept = status < 500 ? protector.Protect(body) : null;
        if (kept == null || kept.Length > IdempotencyRecord.MaxBodyLength)
        {
            await store.RemoveIdempotencyAsync(record, CancellationToken.None);
        }
        else
        {
            record.Complete(status, contentType, kept);
            await store.SaveChangesAsync(CancellationToken.None);
        }
        return new StoredResult(status, contentType, body);
    }

    private static async Task<bool> TryBeginAsync(IPublicApiStore store, IdempotencyRecord record, IClock clock, CancellationToken ct)
    {
        if (await store.FindIdempotencyAsync(record.Scope, record.Key, clock.UtcNow, ct) != null) return false;
        return await store.TryAddIdempotencyAsync(record, ct);
    }

    private static async Task<string> BodyHashAsync(HttpRequest request)
    {
        // The endpoint has usually read its JSON body already; UsePublicApi keeps it re-readable (RequiresIdempotencyKey).
        request.EnableBuffering();
        request.Body.Position = 0;
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, request.HttpContext.RequestAborted);
        request.Body.Position = 0;
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    /// <summary>Runs the endpoint's result into memory, to keep it and to send it.</summary>
    private static async Task<(int Status, string? ContentType, string Body)> CaptureAsync(HttpContext http, object? outcome)
    {
        var result = outcome as IResult ?? Results.Ok(outcome);
        var original = http.Response.Body;
        using var memory = new MemoryStream();
        http.Response.Body = memory;
        try
        {
            await result.ExecuteAsync(http);
        }
        finally
        {
            http.Response.Body = original;
        }
        return (http.Response.StatusCode, http.Response.ContentType, Encoding.UTF8.GetString(memory.ToArray()));
    }

    private sealed class StoredResult : IResult
    {
        private readonly int _status;
        private readonly string? _contentType;
        private readonly string _body;

        public StoredResult(int status, string? contentType, string body)
        {
            _status = status;
            _contentType = contentType;
            _body = body;
        }

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = _status;
            if (_contentType != null) httpContext.Response.ContentType = _contentType;
            await httpContext.Response.WriteAsync(_body, Encoding.UTF8);
        }
    }
}

/// <summary>Marks an endpoint that takes an <c>Idempotency-Key</c>: its body is buffered, and the OpenAPI document declares the header.</summary>
public sealed class RequiresIdempotencyKey
{
    public static readonly RequiresIdempotencyKey Instance = new();
}

public static class IdempotencyExtensions
{
    /// <summary>The endpoint requires an Idempotency-Key and answers repeats from the stored first answer.</summary>
    public static TBuilder RequireIdempotency<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(RequiresIdempotencyKey.Instance).AddEndpointFilter(new IdempotencyFilter());
}
