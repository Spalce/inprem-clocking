using Microsoft.Extensions.Options;

namespace Inprem.Platform.Infrastructure.PayPal;

public enum PayPalMode
{
    Sandbox,
    Live,
}

/// <summary>
/// Configuration section <c>PayPal</c> (Phase 2 step 2.2). The client id and secret come from
/// user-secrets or the host's settings, never from the repository. Without a client id, PayPal is
/// "not configured": the app runs, and PayPal actions say so.
/// </summary>
public sealed class PayPalOptions
{
    public const string Section = "PayPal";

    public PayPalMode Mode { get; set; } = PayPalMode.Sandbox;

    /// <summary>Must be true for <see cref="PayPalMode.Live"/> (not in Phase 2).</summary>
    public bool AllowLive { get; set; }

    public string? ClientId { get; set; }

    /// <summary>The app's secret (configuration key <c>PayPal:Secret</c>).</summary>
    public string? Secret { get; set; }

    /// <summary>Our webhook's id in the PayPal app (needed to verify webhook signatures).</summary>
    public string? WebhookId { get; set; }

    /// <summary>Origins (e.g. <c>https://app.inpremclockin.org</c>) that return and cancel addresses may use.</summary>
    public string[] ReturnUrlAllowList { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Our fixed return and cancel pages, stored on every PayPal plan (developer's choice, step 2.2:
    /// a fallback in case PayPal stops honouring the deprecated per-subscription addresses).
    /// </summary>
    public string? PlanReturnUrl { get; set; }
    public string? PlanCancelUrl { get; set; }

    /// <summary>Seconds per PayPal call (after which it counts as failed and may be retried).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    public Uri BaseAddress => new(Mode == PayPalMode.Live ? "https://api-m.paypal.com/" : "https://api-m.sandbox.paypal.com/");

    /// <summary>True if <paramref name="url"/> is an absolute https address whose origin is in the allow-list.</summary>
    public bool IsAllowedReturnUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0) return false;
        return ReturnUrlAllowList.Any(origin => Uri.TryCreate(origin, UriKind.Absolute, out var allowed)
            && allowed.Scheme == uri.Scheme && string.Equals(allowed.Host, uri.Host, StringComparison.OrdinalIgnoreCase) && allowed.Port == uri.Port);
    }
}

/// <summary>Checked at startup: a wrong PayPal configuration stops the app instead of failing at the first payment.</summary>
public sealed class PayPalOptionsValidator : IValidateOptions<PayPalOptions>
{
    public ValidateOptionsResult Validate(string? name, PayPalOptions o)
    {
        var errors = new List<string>();
        if (o.Mode == PayPalMode.Live && !o.AllowLive) errors.Add("PayPal:Mode is Live but PayPal:AllowLive is not true (live payments are not enabled in this phase).");
        if (o.IsConfigured && string.IsNullOrWhiteSpace(o.Secret)) errors.Add("PayPal:ClientId is set but PayPal:Secret is missing.");
        if (!o.IsConfigured && !string.IsNullOrWhiteSpace(o.Secret)) errors.Add("PayPal:Secret is set but PayPal:ClientId is missing.");
        if (o.TimeoutSeconds is < 5 or > 120) errors.Add("PayPal:TimeoutSeconds must be 5 to 120.");
        foreach (var origin in o.ReturnUrlAllowList)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.AbsolutePath != "/" || uri.Query.Length > 0)
                errors.Add($"PayPal:ReturnUrlAllowList entry '{origin}' must be an https origin such as https://app.example.org.");
        }
        foreach (var (key, url) in new[] { ("PlanReturnUrl", o.PlanReturnUrl), ("PlanCancelUrl", o.PlanCancelUrl) })
        {
            if (url != null && !o.IsAllowedReturnUrl(url)) errors.Add($"PayPal:{key} must be an https address on an origin in PayPal:ReturnUrlAllowList.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
