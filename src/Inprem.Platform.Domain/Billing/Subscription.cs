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

/// <summary>
/// An organization's subscription to a plan (one per organization). The transitions follow the
/// subscription state diagram of design 8 (Phase 2 step 2.1); a method refuses a move the
/// diagram does not have, and repeating a move that already happened changes nothing and
/// returns false (webhooks, confirm calls and reconciliation may all report the same thing).
/// </summary>
public sealed class Subscription
{
    /// <summary>Days kept after Suspended, Expired or the end of a Canceled period before Closed (D24).</summary>
    public const int DefaultRetentionDays = 90;

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

    /// <summary>The paid period PayPal reports (start of the current cycle, next billing time).</summary>
    public DateTime? CurrentPeriodStartUtc { get; private set; }
    public DateTime? CurrentPeriodEndUtc { get; private set; }

    /// <summary>Canceled: access continues until <see cref="CurrentPeriodEndUtc"/>.</summary>
    public bool CancelAtPeriodEnd { get; private set; }

    /// <summary>Amount PayPal reports as owed (failed payments not yet collected).</summary>
    public decimal OutstandingBalance { get; private set; }

    /// <summary>The PayPal subscription id (unique: one PayPal subscription is never linked to two organizations).</summary>
    public string? ExternalSubscriptionId { get; private set; }

    /// <summary>When the status last changed (Closed is counted from it).</summary>
    public DateTime? StatusChangedUtc { get; private set; }

    /// <summary>Console override: full access until then, whatever the status ("grant free access").</summary>
    public DateTime? FreeAccessUntilUtc { get; private set; }

    /// <summary>Console override of the plan's people limit (null = the plan's).</summary>
    public int? MaxPeopleOverride { get; private set; }

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
        Utc(nowUtc);
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
            CreatedUtc = nowUtc, StatusChangedUtc = nowUtc,
        };
    }

    // ------------------------------------------------------------------ access

    /// <summary>
    /// Full access (saving, kiosk): Free; a running trial; PendingActivation (PayPal approved);
    /// Active; PastDue (PayPal is retrying); Canceled until the period ends; or a console
    /// free-access override. Otherwise read-only (reports and exports still work).
    /// </summary>
    public bool HasFullAccess(DateTime nowUtc)
    {
        if (FreeAccessUntilUtc > nowUtc) return true;
        if (Mode == BillingMode.Free && Status == SubscriptionStatus.Active) return true;
        return Status switch
        {
            SubscriptionStatus.Trialing => TrialEndsUtc > nowUtc,
            SubscriptionStatus.PendingActivation or SubscriptionStatus.Active or SubscriptionStatus.PastDue => true,
            SubscriptionStatus.Canceled => CurrentPeriodEndUtc > nowUtc,
            _ => false,
        };
    }

    /// <summary>Whole days of trial left (0 when not trialing or over).</summary>
    public int TrialDaysLeft(DateTime nowUtc) =>
        Status == SubscriptionStatus.Trialing && TrialEndsUtc > nowUtc ? (int)Math.Ceiling((TrialEndsUtc.Value - nowUtc).TotalDays) : 0;

    // ------------------------------------------------------------- transitions

    /// <summary>Trialing → Expired when the trial end date has passed (lazy check and daily job).</summary>
    public bool ExpireTrialIfOver(DateTime nowUtc)
    {
        Utc(nowUtc);
        if (Status != SubscriptionStatus.Trialing || TrialEndsUtc > nowUtc) return false;
        return Move(SubscriptionStatus.Expired, nowUtc);
    }

    /// <summary>
    /// The owner approved a PayPal subscription; waiting for PayPal's ACTIVATED (Trialing, Expired,
    /// Canceled or Suspended → PendingActivation). The price is the one checked out.
    /// </summary>
    public bool AwaitActivation(string externalSubscriptionId, PlanPrice price, DateTime nowUtc)
    {
        Utc(nowUtc);
        Required(externalSubscriptionId);
        if (Status == SubscriptionStatus.PendingActivation && ExternalSubscriptionId == externalSubscriptionId) return false;
        if (Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Expired or SubscriptionStatus.Canceled or SubscriptionStatus.Suspended or SubscriptionStatus.PendingActivation))
            throw Refused("wait for a PayPal activation");
        SetPrice(price);
        Mode = BillingMode.PayPal;
        ExternalSubscriptionId = externalSubscriptionId;
        return Move(SubscriptionStatus.PendingActivation, nowUtc);
    }

    /// <summary>
    /// PayPal reports the subscription ACTIVE (design 7.5: confirm call, verified webhook or
    /// reconciliation, exactly once). From Trialing, Expired, PendingActivation, Suspended (balance
    /// paid and reactivated) or Canceled (resubscribed). Returns false if it was already active
    /// with this PayPal subscription.
    /// </summary>
    public bool Activate(string externalSubscriptionId, PlanPrice price, DateTime? periodStartUtc, DateTime? periodEndUtc, DateTime nowUtc)
    {
        Utc(nowUtc);
        Required(externalSubscriptionId);
        if (Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue && ExternalSubscriptionId == externalSubscriptionId) return false;
        if (Status is SubscriptionStatus.Closed or SubscriptionStatus.Active or SubscriptionStatus.PastDue) throw Refused("activate another PayPal subscription");
        SetPrice(price);
        Mode = BillingMode.PayPal;
        ExternalSubscriptionId = externalSubscriptionId;
        CancelAtPeriodEnd = false;
        OutstandingBalance = 0;
        SetPeriod(periodStartUtc, periodEndUtc);
        return Move(SubscriptionStatus.Active, nowUtc);
    }

    /// <summary>BILLING.SUBSCRIPTION.PAYMENT.FAILED: Active → PastDue (PayPal retries).</summary>
    public bool MarkPastDue(decimal outstandingBalance, DateTime nowUtc)
    {
        Utc(nowUtc);
        if (outstandingBalance < 0) throw new ArgumentOutOfRangeException(nameof(outstandingBalance));
        OutstandingBalance = outstandingBalance;
        if (Status == SubscriptionStatus.PastDue) return false;
        if (Status != SubscriptionStatus.Active) throw Refused("become past due");
        return Move(SubscriptionStatus.PastDue, nowUtc);
    }

    /// <summary>
    /// PAYMENT.SALE.COMPLETED: the period moves on; PastDue → Active and the balance is cleared.
    /// On Active it only advances the period. Returns true if the status changed.
    /// </summary>
    public bool RecordPayment(DateTime? periodStartUtc, DateTime? periodEndUtc, DateTime nowUtc)
    {
        Utc(nowUtc);
        if (Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue)) throw Refused("record a renewal payment");
        SetPeriod(periodStartUtc, periodEndUtc);
        OutstandingBalance = 0;
        return Status == SubscriptionStatus.PastDue && Move(SubscriptionStatus.Active, nowUtc);
    }

    /// <summary>BILLING.SUBSCRIPTION.SUSPENDED: PastDue or Active → Suspended (read-only).</summary>
    public bool Suspend(DateTime nowUtc)
    {
        Utc(nowUtc);
        if (Status == SubscriptionStatus.Suspended) return false;
        if (Status is not (SubscriptionStatus.PastDue or SubscriptionStatus.Active)) throw Refused("be suspended");
        return Move(SubscriptionStatus.Suspended, nowUtc);
    }

    /// <summary>
    /// BILLING.SUBSCRIPTION.CANCELLED (or the owner cancels): Active, PastDue or PendingActivation
    /// → Canceled; full access continues to the end of the paid period.
    /// </summary>
    public bool Cancel(DateTime nowUtc)
    {
        Utc(nowUtc);
        if (Status == SubscriptionStatus.Canceled) return false;
        if (Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.PendingActivation)) throw Refused("be canceled");
        CancelAtPeriodEnd = true;
        return Move(SubscriptionStatus.Canceled, nowUtc);
    }

    /// <summary>
    /// BILLING.SUBSCRIPTION.EXPIRED: the PayPal subscription ran out. Active, PastDue, Canceled
    /// or Suspended → Expired (read-only).
    /// </summary>
    public bool ExpireSubscription(DateTime nowUtc)
    {
        Utc(nowUtc);
        if (Status == SubscriptionStatus.Expired) return false;
        if (Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.Canceled or SubscriptionStatus.Suspended or SubscriptionStatus.PendingActivation))
            throw Refused("expire");
        return Move(SubscriptionStatus.Expired, nowUtc);
    }

    /// <summary>The date after which this subscription may be Closed (null when it may not).</summary>
    public DateTime? ClosableAfterUtc(int retentionDays = DefaultRetentionDays) => Status switch
    {
        SubscriptionStatus.Suspended or SubscriptionStatus.Expired => StatusChangedUtc?.AddDays(retentionDays),
        SubscriptionStatus.Canceled => (CurrentPeriodEndUtc ?? StatusChangedUtc)?.AddDays(retentionDays),
        _ => null,
    };

    /// <summary>Suspended, Expired or Canceled (after its period) → Closed once the retention has passed (D24).</summary>
    public bool Close(DateTime nowUtc, int retentionDays = DefaultRetentionDays)
    {
        Utc(nowUtc);
        if (Status == SubscriptionStatus.Closed) return false;
        var after = ClosableAfterUtc(retentionDays) ?? throw Refused("be closed");
        if (nowUtc < after) throw new InvalidOperationException($"This subscription can be closed after {after:yyyy-MM-dd} (retention {retentionDays} days).");
        return Move(SubscriptionStatus.Closed, nowUtc);
    }

    /// <summary>PayPal reports a changed plan (revision): the new price.</summary>
    public bool ChangePrice(PlanPrice price)
    {
        if (PlanPriceId == price.Id) return false;
        SetPrice(price);
        return true;
    }

    /// <summary>Period dates and balance PayPal reports (BILLING.SUBSCRIPTION.UPDATED, reconciliation).</summary>
    public void Refresh(DateTime? periodStartUtc, DateTime? periodEndUtc, decimal outstandingBalance)
    {
        if (outstandingBalance < 0) throw new ArgumentOutOfRangeException(nameof(outstandingBalance));
        SetPeriod(periodStartUtc, periodEndUtc);
        OutstandingBalance = outstandingBalance;
    }

    // ---------------------------------------------------------- console overrides

    /// <summary>Console: a later trial end (Trialing, or Expired back to Trialing).</summary>
    public void ExtendTrial(DateTime trialEndsUtc, DateTime nowUtc)
    {
        Utc(nowUtc);
        Utc(trialEndsUtc);
        if (trialEndsUtc <= nowUtc) throw new ArgumentException("The new trial end must be in the future.", nameof(trialEndsUtc));
        if (trialEndsUtc > nowUtc.AddDays(Plan.MaxTrialDays)) throw new ArgumentOutOfRangeException(nameof(trialEndsUtc), $"At most {Plan.MaxTrialDays} days from now.");
        if (Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Expired) || ExternalSubscriptionId != null) throw Refused("get a longer trial");
        TrialEndsUtc = trialEndsUtc;
        if (Status == SubscriptionStatus.Expired) Move(SubscriptionStatus.Trialing, nowUtc);
    }

    /// <summary>Console: full access until a date whatever the status, or null to remove it.</summary>
    public void GrantFreeAccess(DateTime? untilUtc, DateTime nowUtc)
    {
        Utc(nowUtc);
        if (untilUtc != null && (Utc(untilUtc.Value) <= nowUtc)) throw new ArgumentException("The date must be in the future.", nameof(untilUtc));
        FreeAccessUntilUtc = untilUtc;
    }

    public void OverrideMaxPeople(int? maxPeople)
    {
        if (maxPeople is < 1 or > 100_000) throw new ArgumentOutOfRangeException(nameof(maxPeople), "1 to 100,000, or empty for the plan's limit.");
        MaxPeopleOverride = maxPeople;
    }

    // ------------------------------------------------------------------ helpers

    private bool Move(SubscriptionStatus to, DateTime nowUtc)
    {
        Status = to;
        StatusChangedUtc = nowUtc;
        return true;
    }

    private void SetPrice(PlanPrice price)
    {
        PlanId = price.PlanId;
        PlanPriceId = price.Id;
    }

    private void SetPeriod(DateTime? startUtc, DateTime? endUtc)
    {
        if (startUtc != null) Utc(startUtc.Value);
        if (endUtc != null) Utc(endUtc.Value);
        if (startUtc != null && endUtc != null && endUtc < startUtc) throw new ArgumentException("The period ends before it starts.", nameof(endUtc));
        if (startUtc != null) CurrentPeriodStartUtc = startUtc;
        if (endUtc != null) CurrentPeriodEndUtc = endUtc;
    }

    private InvalidOperationException Refused(string what) => new($"A {Status} subscription cannot {what}.");

    private static void Required(string externalSubscriptionId)
    {
        if (string.IsNullOrWhiteSpace(externalSubscriptionId) || externalSubscriptionId.Length > 50) throw new ArgumentException("A PayPal subscription id is required (at most 50 characters).", nameof(externalSubscriptionId));
    }

    private static DateTime Utc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("Times are stored in UTC.", nameof(value));
}
