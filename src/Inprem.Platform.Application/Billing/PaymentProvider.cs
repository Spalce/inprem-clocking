using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

/// <summary>A PayPal billing plan to create for one plan price (step 2.3).</summary>
public sealed record NewBillingPlan(string ProductId, string Name, string? Description, BillingCycle Cycle, decimal Amount, string Currency);

/// <summary>A PayPal subscription just created: its id, status and the approve link for the buyer.</summary>
public sealed record CreatedSubscription(string Id, PayPalSubscriptionState State, string ApprovalUrl);

/// <summary>What PayPal says about a subscription now (the source of truth, design 7.5).</summary>
public sealed record PayPalSubscription(
    string Id,
    PayPalSubscriptionState State,
    string? RawStatus,
    string? PlanId,
    string? CustomId,
    DateTime? StartUtc,
    DateTime? NextBillingUtc,
    DateTime? LastPaymentUtc,
    decimal? LastPaymentAmount,
    string? LastPaymentCurrency,
    decimal OutstandingBalance,
    int FailedPaymentsCount);

/// <summary>A webhook as received: its headers (any case) and the raw body, unchanged.</summary>
public sealed record WebhookMessage(IReadOnlyDictionary<string, string> Headers, string Body);

/// <summary>A PayPal call failed (after retries); never carries payloads or personal data.</summary>
public sealed class PaymentProviderException : Exception
{
    public PaymentProviderException(string message, int? statusCode = null, string? debugId = null, Exception? inner = null, string? issue = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issue = issue;
    }

    /// <summary>The HTTP status PayPal answered with (null: no answer, e.g. a timeout).</summary>
    public int? StatusCode { get; }

    /// <summary>PayPal's <c>PayPal-Debug-Id</c>, for PayPal support.</summary>
    public string? DebugId { get; }

    /// <summary>PayPal's error code (<c>details[0].issue</c>, else <c>name</c>), e.g. <c>DUPLICATE_INVOICE_NUMBER</c>; never values.</summary>
    public string? Issue { get; }
}

/// <summary>
/// The payment provider (PayPal, Phase 2 step 2.2). Only the server talks to PayPal; the browser
/// only ever sees PayPal's approve link (D19).
/// </summary>
public interface IPaymentProvider
{
    /// <summary>False when no PayPal credentials are configured (development without PayPal).</summary>
    bool IsConfigured { get; }

    /// <summary>Creates a catalog product (once per plan). <paramref name="requestId"/> makes a retry safe.</summary>
    Task<string> CreateProductAsync(string name, string? description, string requestId, CancellationToken ct = default);

    /// <summary>
    /// Creates an active billing plan: one regular cycle at the price, auto-billing of outstanding
    /// amounts and the D23 failure threshold, and our fixed return and cancel pages as a fallback.
    /// </summary>
    Task<string> CreatePlanAsync(NewBillingPlan plan, string requestId, CancellationToken ct = default);

    /// <summary>Creates a subscription for the buyer to approve; <paramref name="customId"/> is the checkout reference.</summary>
    Task<CreatedSubscription> CreateSubscriptionAsync(string planId, string customId, string returnUrl, string cancelUrl, string requestId, CancellationToken ct = default);

    /// <summary>The subscription as PayPal reports it, or null if PayPal does not know it.</summary>
    Task<PayPalSubscription?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>Moves a subscription to another plan; returns the approve link when the buyer must approve the change.</summary>
    Task<string?> ReviseSubscriptionAsync(string subscriptionId, string planId, string returnUrl, string cancelUrl, CancellationToken ct = default);

    /// <summary>Cancels a subscription (already cancelled counts as done).</summary>
    Task CancelSubscriptionAsync(string subscriptionId, string reason, CancellationToken ct = default);

    /// <summary>Asks PayPal whether a webhook really came from PayPal for our webhook id. Never throws for a bad message.</summary>
    Task<bool> VerifyWebhookAsync(WebhookMessage message, CancellationToken ct = default);

    /// <summary>True if <paramref name="url"/> is an https address on an allowed origin (return and cancel addresses).</summary>
    bool IsAllowedReturnUrl(string url);
}
