namespace Inprem.Platform.Domain.Billing;

public enum BillingMode
{
    /// <summary>PayPal subscription (from Phase 2): starts with a free trial until then.</summary>
    PayPal,

    /// <summary>Invoices sent by the provider, paid offline or by PayPal invoice (Phase 4).</summary>
    Invoiced,

    /// <summary>No charge (e.g. Inprem's own "house" organization, D26).</summary>
    Free,
}

/// <summary>Design 8 states. Phase 1 creates Trialing or Active; PayPal moves the rest from Phase 2.</summary>
public enum SubscriptionStatus
{
    Trialing,
    PendingActivation,
    Active,
    PastDue,
    Suspended,
    Expired,
    Canceled,
    Closed,
}

/// <summary>An organization's subscription to a plan (one per organization).</summary>
public sealed class Subscription
{
    private Subscription()
    {
    }

    public Guid Id { get; private set; }
    public int TenantId { get; private set; }

    /// <summary>Null only for Free without a plan.</summary>
    public Guid? PlanId { get; private set; }
    public Guid? PlanPriceId { get; private set; }
    public BillingMode Mode { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTime? TrialEndsUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    /// <summary>
    /// The subscription a new organization starts with:
    /// <list type="bullet">
    /// <item>PayPal: a trial of at least one day (paying through PayPal starts in Phase 2).</item>
    /// <item>Invoiced: a trial when one is given, otherwise Active.</item>
    /// <item>Free: Active, no trial; the plan is optional.</item>
    /// </list>
    /// </summary>
    public static Subscription Start(int tenantId, Plan? plan, PlanPrice? price, BillingMode mode, int trialDays, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if (trialDays is < 0 or > Plan.MaxTrialDays) throw new ArgumentOutOfRangeException(nameof(trialDays), $"Trial length must be 0 to {Plan.MaxTrialDays} days.");
        if (mode != BillingMode.Free && (plan == null || price == null)) throw new ArgumentException("Choose a plan and price (only Free can go without).", nameof(plan));
        if (plan != null && !plan.IsOffered) throw new ArgumentException("That plan is not offered any more.", nameof(plan));
        if (price != null && (plan == null || price.PlanId != plan.Id || price.Status != PlanStatus.Active)) throw new ArgumentException("That price is not an active price of the plan.", nameof(price));
        if (mode == BillingMode.PayPal && trialDays < 1) throw new ArgumentException("PayPal billing starts in Phase 2: give at least a one-day trial for now.", nameof(trialDays));
        if (mode == BillingMode.Free) trialDays = 0;

        return new Subscription
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PlanId = plan?.Id, PlanPriceId = price?.Id, Mode = mode,
            Status = trialDays > 0 ? SubscriptionStatus.Trialing : SubscriptionStatus.Active,
            TrialEndsUtc = trialDays > 0 ? nowUtc.AddDays(trialDays) : null,
            CreatedUtc = nowUtc,
        };
    }
}
