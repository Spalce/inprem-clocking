using System.Collections.Concurrent;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>
/// A pretend PayPal for the test host (Phase 2): keeps products, plans and subscriptions in
/// memory, honours request ids like PayPal (the same id returns the same object), and lets a
/// test make the next call fail, change a subscription's status, or decide webhook verification.
/// </summary>
public sealed class FakePaymentProvider : IPaymentProvider
{
    public const string AllowedOrigin = "https://localhost";

    private readonly ConcurrentDictionary<string, string> _byRequestId = new();
    private int _counter;

    public bool IsConfigured { get; set; } = true;

    /// <summary>Products by id: (name, description).</summary>
    public ConcurrentDictionary<string, (string Name, string? Description)> Products { get; } = new();

    /// <summary>Billing plans by id.</summary>
    public ConcurrentDictionary<string, NewBillingPlan> Plans { get; } = new();

    /// <summary>Subscriptions by id, as PayPal would report them.</summary>
    public ConcurrentDictionary<string, PayPalSubscription> Subscriptions { get; } = new();

    /// <summary>Every call, in order ("CreatePlan P-…", "GetSubscription I-…").</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>Set to make the next N calls throw (then it counts down).</summary>
    public int FailNextCalls { get; set; }

    private int? _okBeforeFailure;

    /// <summary>Lets the next <paramref name="okCalls"/> calls through, then fails one (a failure halfway).</summary>
    public void FailAfter(int okCalls) => _okBeforeFailure = okCalls;

    /// <summary>What webhook verification answers.</summary>
    public Func<WebhookMessage, bool> Verify { get; set; } = _ => true;

    /// <summary>Origins allowed for return and cancel addresses (the app's own by default; a test adds its website's).</summary>
    public HashSet<string> AllowedOrigins { get; } = new(StringComparer.OrdinalIgnoreCase) { AllowedOrigin };

    public bool IsAllowedReturnUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && AllowedOrigins.Contains(uri.GetLeftPart(UriPartial.Authority));

    public Task<string> CreateProductAsync(string name, string? description, string requestId, CancellationToken ct = default)
    {
        Fail("CreateProduct");
        var id = Once(requestId, () => $"PROD-{Next():D17}");
        Products.TryAdd(id, (name, description));
        Calls.Enqueue($"CreateProduct {id}");
        return Task.FromResult(id);
    }

    public Task<string> CreatePlanAsync(NewBillingPlan plan, string requestId, CancellationToken ct = default)
    {
        Fail("CreatePlan");
        if (!Products.ContainsKey(plan.ProductId)) throw new PaymentProviderException("No such product.", 404);
        var id = Once(requestId, () => $"P-{Next():D24}");
        Plans.TryAdd(id, plan);
        Calls.Enqueue($"CreatePlan {id}");
        return Task.FromResult(id);
    }

    public Task<CreatedSubscription> CreateSubscriptionAsync(string planId, string customId, string returnUrl, string cancelUrl, string requestId, CancellationToken ct = default)
    {
        Fail("CreateSubscription");
        if (!Plans.ContainsKey(planId)) throw new PaymentProviderException("No such plan.", 404);
        var id = Once(requestId, () => $"I-{Next():D12}");
        Subscriptions.TryAdd(id, new PayPalSubscription(id, PayPalSubscriptionState.ApprovalPending, "APPROVAL_PENDING", planId, customId, null, null, null, null, null, 0m, 0));
        Calls.Enqueue($"CreateSubscription {id}");
        return Task.FromResult(new CreatedSubscription(id, PayPalSubscriptionState.ApprovalPending, $"https://www.sandbox.paypal.com/webapps/billing/subscriptions?ba_token=BA-{id}"));
    }

    public Task<PayPalSubscription?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
    {
        Fail("GetSubscription");
        Calls.Enqueue($"GetSubscription {subscriptionId}");
        return Task.FromResult(Subscriptions.TryGetValue(subscriptionId, out var s) ? s : null);
    }

    public Task<string?> ReviseSubscriptionAsync(string subscriptionId, string planId, string returnUrl, string cancelUrl, CancellationToken ct = default)
    {
        Fail("ReviseSubscription");
        Calls.Enqueue($"ReviseSubscription {subscriptionId} {planId}");
        return Task.FromResult<string?>($"https://www.sandbox.paypal.com/webapps/billing/subscriptions/revise?id={subscriptionId}");
    }

    public Task CancelSubscriptionAsync(string subscriptionId, string reason, CancellationToken ct = default)
    {
        Fail("CancelSubscription");
        Calls.Enqueue($"CancelSubscription {subscriptionId}");
        if (Subscriptions.TryGetValue(subscriptionId, out var s)) Subscriptions[subscriptionId] = s with { State = PayPalSubscriptionState.Cancelled, RawStatus = "CANCELLED" };
        return Task.CompletedTask;
    }

    public Task<bool> VerifyWebhookAsync(WebhookMessage message, CancellationToken ct = default) => Task.FromResult(IsConfigured && Verify(message));

    /// <summary>What PayPal would do when the buyer approves (and PayPal activates) a subscription.</summary>
    public void SetState(string subscriptionId, PayPalSubscriptionState state, DateTime? nextBillingUtc = null)
    {
        var s = Subscriptions[subscriptionId];
        Subscriptions[subscriptionId] = s with { State = state, RawStatus = state.ToString().ToUpperInvariant(), NextBillingUtc = nextBillingUtc ?? s.NextBillingUtc, StartUtc = s.StartUtc ?? DateTime.UtcNow };
    }

    private void Fail(string call)
    {
        if (_okBeforeFailure is int ok)
        {
            if (ok > 0)
            {
                _okBeforeFailure = ok - 1;
                return;
            }
            _okBeforeFailure = null;
            FailNextCalls = Math.Max(FailNextCalls, 1);
        }
        if (FailNextCalls <= 0) return;
        FailNextCalls--;
        Calls.Enqueue($"{call} FAILED");
        throw new PaymentProviderException($"PayPal could not complete {call} (pretend failure).", 503, "fake-debug-id");
    }

    private string Once(string requestId, Func<string> create) => _byRequestId.GetOrAdd(requestId, _ => create());

    private int Next() => Interlocked.Increment(ref _counter);
}
