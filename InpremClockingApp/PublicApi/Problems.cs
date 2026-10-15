using System.Diagnostics;

namespace InpremClockingApp.PublicApi;

/// <summary>
/// Website API errors (design 7.7): RFC 7807 problem details with a stable <c>code</c> the website
/// can rely on, and a <c>traceId</c> for support. Never a stack trace or personal data.
/// </summary>
public static class Problems
{
    public const string ValidationFailed = "validation_failed";
    public const string CaptchaFailed = "captcha_failed";
    public const string ReturnUrlNotAllowed = "return_url_not_allowed";
    public const string PlanNotAvailable = "plan_not_available";
    public const string InvalidCode = "invalid_code";
    public const string TokenMissing = "token_missing";
    public const string TokenInvalid = "token_invalid";
    public const string TokenExpired = "token_expired";
    public const string NotFound = "not_found";
    public const string SlugTaken = "slug_taken";
    public const string EmailInUse = "email_in_use";
    public const string CheckoutExpired = "checkout_expired";
    public const string CheckoutFinal = "checkout_final";
    public const string SubscriptionNotCancellable = "subscription_not_cancellable";
    public const string CannotKeep = "cannot_keep";
    public const string ResendLimitReached = "resend_limit_reached";
    public const string AlreadyVerified = "already_verified";
    public const string IdempotencyKeyReused = "idempotency_key_reused";
    public const string RateLimited = "rate_limited";
    public const string PaymentProviderUnavailable = "payment_provider_unavailable";

    /// <summary>An unexpected error (added to the agreed list in Phase 3 step 3.1: the draft had no code for a 500).</summary>
    public const string InternalError = "internal_error";

    /// <summary>Every code the API may answer with (the contract test compares it with the agreed document).</summary>
    public static readonly string[] All =
    {
        ValidationFailed, CaptchaFailed, ReturnUrlNotAllowed, PlanNotAvailable, InvalidCode, TokenMissing, TokenInvalid, TokenExpired,
        NotFound, SlugTaken, EmailInUse, CheckoutExpired, CheckoutFinal, SubscriptionNotCancellable, CannotKeep, ResendLimitReached,
        AlreadyVerified, IdempotencyKeyReused, RateLimited, PaymentProviderUnavailable, InternalError,
    };

    public static IResult Create(HttpContext context, int status, string code, string title, string? detail = null, IDictionary<string, string[]>? errors = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
        };
        if (errors is { Count: > 0 }) extensions["errors"] = errors;
        // The endpoint's pattern, not the request's own path: an error never repeats what the caller sent.
        var instance = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "/api/public/v1";
        return Results.Problem(detail: detail, instance: instance, statusCode: status, title: title,
            type: $"{context.Request.Scheme}://{context.Request.Host}/problems/{code}", extensions: extensions);
    }

    public static IResult Validation(HttpContext context, IDictionary<string, string[]> errors) =>
        Create(context, StatusCodes.Status400BadRequest, ValidationFailed, "One or more fields are invalid.", errors: errors);
}

/// <summary>Any unexpected exception in the website API becomes a 500 problem (logged; nothing about it is sent back).</summary>
public sealed class ProblemOnExceptionFilter : IEndpointFilter
{
    private readonly ILogger<ProblemOnExceptionFilter> _log;

    public ProblemOnExceptionFilter(ILogger<ProblemOnExceptionFilter> log) => _log = log;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _log.LogError(e, "Website API error on {Path}.", context.HttpContext.Request.Path);
            return Problems.Create(context.HttpContext, StatusCodes.Status500InternalServerError, Problems.InternalError, "Something went wrong. Try again later.");
        }
    }
}
