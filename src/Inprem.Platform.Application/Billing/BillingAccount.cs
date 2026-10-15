using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

public interface IBillingAccountStore
{
    /// <summary>The organization's payments, newest first.</summary>
    Task<IReadOnlyList<Payment>> PaymentsAsync(int tenantId, int max, CancellationToken ct = default);

    /// <summary>Public, offered plans with their active prices that are published to PayPal.</summary>
    Task<IReadOnlyList<Plan>> PublishedPlansAsync(CancellationToken ct = default);

    /// <summary>Every subscription with its organization's name (console Billing list).</summary>
    Task<(IReadOnlyList<(Subscription Subscription, string TenantName)> Items, int Total)> ListAsync(SubscriptionStatus? status, BillingMode? mode, int skip, int take, CancellationToken ct = default);
}

/// <summary>An organization's billing, for its Billing page and the console.</summary>
public sealed record BillingOverview(
    Subscription? Subscription,
    Plan? Plan,
    PlanPrice? Price,
    AccessStatus Access,
    IReadOnlyList<Plan> Offered,
    IReadOnlyList<Payment> Payments)
{
    public bool PaysByPayPal => Subscription is { Mode: BillingMode.PayPal, ExternalSubscriptionId: not null }
        && Subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue;

    /// <summary>What a checkout from the Billing page does now (null: nothing to check out, e.g. Free or Invoiced).</summary>
    public CheckoutPurpose? NextPurpose => Subscription?.Status switch
    {
        null => null,
        _ when Subscription.Mode is BillingMode.Free or BillingMode.Invoiced && Subscription.Status == SubscriptionStatus.Active => null,
        SubscriptionStatus.Trialing => CheckoutPurpose.Convert,
        SubscriptionStatus.Expired or SubscriptionStatus.Canceled or SubscriptionStatus.Suspended => CheckoutPurpose.Resubscribe,
        SubscriptionStatus.Active or SubscriptionStatus.PastDue when PaysByPayPal => CheckoutPurpose.ChangePlan,
        _ => null,
    };
}

/// <summary>
/// The Billing page (owners, App site) and the console's subscription actions (Phase 2 step 2.8).
/// Checkout itself is <see cref="CheckoutService"/>. Console changes are audited, one entry each.
/// </summary>
public sealed class BillingAccountService
{
    private readonly ICheckoutStore _store;
    private readonly IBillingAccountStore _account;
    private readonly IEntitlementService _entitlements;
    private readonly SubscriptionSync _sync;
    private readonly IClock _clock;

    public BillingAccountService(ICheckoutStore store, IBillingAccountStore account, IEntitlementService entitlements, SubscriptionSync sync, IClock clock)
    {
        _store = store;
        _account = account;
        _entitlements = entitlements;
        _sync = sync;
        _clock = clock;
    }

    public async Task<BillingOverview> GetAsync(int tenantId, CancellationToken ct = default)
    {
        var subscription = await _store.FindSubscriptionAsync(tenantId, ct).ConfigureAwait(false);
        (Plan Plan, PlanPrice Price)? current = subscription?.PlanPriceId is Guid priceId ? await _store.FindPriceAsync(priceId, ct).ConfigureAwait(false) : null;
        return new BillingOverview(subscription, current?.Plan, current?.Price,
            await _entitlements.GetAsync(tenantId, ct).ConfigureAwait(false),
            await _account.PublishedPlansAsync(ct).ConfigureAwait(false),
            await _account.PaymentsAsync(tenantId, 50, ct).ConfigureAwait(false));
    }

    public Task<(IReadOnlyList<(Subscription Subscription, string TenantName)> Items, int Total)> ListAsync(SubscriptionStatus? status, BillingMode? mode, int skip, int take, CancellationToken ct = default) =>
        _account.ListAsync(status, mode, skip, take, ct);

    // ---------------------------------------------------------- console actions

    /// <summary>Console: a later trial end (also brings an Expired trial back).</summary>
    public Task<PlanResult> ExtendTrialAsync(int tenantId, DateTime trialEndsUtc, string actor, CancellationToken ct = default) =>
        ChangeAsync(tenantId, actor, "subscription.trial-extended", s =>
        {
            s.ExtendTrial(DateTime.SpecifyKind(trialEndsUtc, DateTimeKind.Utc), _clock.UtcNow);
            return $"Trial now ends {s.TrialEndsUtc:yyyy-MM-dd}.";
        }, ct);

    /// <summary>Console: full access until a date whatever the subscription says, or null to remove it.</summary>
    public Task<PlanResult> GrantFreeAccessAsync(int tenantId, DateTime? untilUtc, string reason, string actor, CancellationToken ct = default) =>
        ChangeAsync(tenantId, actor, untilUtc == null ? "subscription.free-access-removed" : "subscription.free-access-granted", s =>
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Give a reason.");
            s.GrantFreeAccess(untilUtc == null ? null : DateTime.SpecifyKind(untilUtc.Value, DateTimeKind.Utc), _clock.UtcNow);
            var text = reason.Trim();
            return (untilUtc == null ? "Free access removed" : $"Full access until {untilUtc:yyyy-MM-dd}") + $": {(text.Length > 300 ? text[..300] : text)}";
        }, ct);

    /// <summary>Console: compare with PayPal now and correct (the daily reconciliation, for one organization).</summary>
    public async Task<PlanResult> SyncAsync(int tenantId, string actor, CancellationToken ct = default)
    {
        var subscription = await _store.FindSubscriptionAsync(tenantId, ct).ConfigureAwait(false);
        if (subscription?.ExternalSubscriptionId == null) return PlanResult.Fail("This organization has no PayPal subscription.");
        try
        {
            var change = await _sync.SyncAsync(subscription.ExternalSubscriptionId, actor, ct).ConfigureAwait(false);
            if (change == null)
            {
                await _store.AddAuditAsync(AuditEntry.Record(actor, "subscription.synced", subscription.Id.ToString(), "Checked with PayPal: already in line.", _clock.UtcNow, tenantId), ct).ConfigureAwait(false);
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            return new PlanResult(true, null, change);
        }
        catch (PaymentProviderException e)
        {
            return PlanResult.Fail($"PayPal is not answering ({e.Message}). Try again later.");
        }
    }

    private async Task<PlanResult> ChangeAsync(int tenantId, string actor, string action, Func<Subscription, string> change, CancellationToken ct) =>
        await _store.WithLockAsync(BillingLocks.Tenant(tenantId), async () =>
        {
            _store.Reset();
            var subscription = await _store.FindSubscriptionAsync(tenantId, ct).ConfigureAwait(false);
            if (subscription == null) return PlanResult.Fail("This organization has no subscription.");
            string details;
            try
            {
                details = change(subscription);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                return PlanResult.Fail(e is ArgumentException a && a.ParamName != null ? a.Message.Replace($" (Parameter '{a.ParamName}')", "") : e.Message);
            }
            await _store.AddAuditAsync(AuditEntry.Record(actor, action, subscription.Id.ToString(), details, _clock.UtcNow, tenantId), ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            return new PlanResult(true, null, null);
        }, ct).ConfigureAwait(false);
}
