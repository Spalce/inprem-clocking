using System.Net;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;

namespace Inprem.Platform.Application.Billing;

public interface ICheckoutStore
{
    Task AddAsync(CheckoutSession session, CancellationToken ct = default);
    Task<CheckoutSession?> FindAsync(Guid id, CancellationToken ct = default);
    Task<CheckoutSession?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default);
    Task<CheckoutSession?> FindByExternalIdAsync(string externalSubscriptionId, CancellationToken ct = default);

    /// <summary>The organization's subscription (tracked).</summary>
    Task<Subscription?> FindSubscriptionAsync(int tenantId, CancellationToken ct = default);

    /// <summary>The organization whose subscription has this PayPal id, if any.</summary>
    Task<int?> TenantWithExternalIdAsync(string externalSubscriptionId, CancellationToken ct = default);

    /// <summary>A price with its plan.</summary>
    Task<(Plan Plan, PlanPrice Price)?> FindPriceAsync(Guid priceId, CancellationToken ct = default);

    /// <summary>The price published as this PayPal plan, if any.</summary>
    Task<PlanPrice?> FindPriceByPayPalPlanAsync(string payPalPlanId, CancellationToken ct = default);

    Task<Payment?> FindPaymentAsync(PaymentSource source, string externalId, CancellationToken ct = default);
    Task AddPaymentAsync(Payment payment, CancellationToken ct = default);

    /// <summary>The active Owners' emails of an organization (confirmation emails).</summary>
    Task<IReadOnlyList<string>> OwnerEmailsAsync(int tenantId, CancellationToken ct = default);

    Task<string?> TenantNameAsync(int tenantId, CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="work"/> while holding a database lock on <paramref name="key"/>
    /// (sp_getapplock, any process): confirm, webhook and reconciliation for the same checkout
    /// run one after the other, so activation happens exactly once (D20).
    /// </summary>
    Task<T> WithLockAsync<T>(string key, Func<Task<T>> work, CancellationToken ct = default);

    /// <summary>Forgets tracked changes (after a lock wait, re-read what another process wrote).</summary>
    void Reset();

    Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Where the news about a checkout came from (design 7.5's three sources).</summary>
public enum CheckoutSource
{
    Confirm,
    Webhook,
    Reconciliation,
}

public sealed record StartCheckout(CheckoutPurpose Purpose, int TenantId, Guid PlanPriceId, string ReturnUrl, string CancelUrl, string IdempotencyKey, string Actor);

/// <summary>The result of a checkout call: the session (when there is one) or a message for the person.</summary>
public sealed record CheckoutResult(bool Succeeded, CheckoutSession? Session, string? Error)
{
    public static CheckoutResult Ok(CheckoutSession session) => new(true, session, null);

    public static CheckoutResult Fail(string error, CheckoutSession? session = null) => new(false, session, error);
}

/// <summary>
/// Checkout (Phase 2 step 2.4, design 7.4 and 7.5), shared by the in-app Billing page and, in
/// Phase 3, the website. Our server creates the PayPal subscription from our own catalogue (the
/// browser only ever sends our price id), and an organization becomes Active only when PayPal
/// itself reports the subscription ACTIVE, whichever source asks first (D20): the confirm call
/// after the redirect, a verified webhook, or reconciliation. All three call
/// <see cref="ConfirmAsync"/>, under one lock per checkout, so activation happens exactly once.
/// </summary>
public sealed class CheckoutService
{
    private readonly ICheckoutStore _store;
    private readonly IPaymentProvider _paypal;
    private readonly IProviderStore _provider;
    private readonly IEmailSender _email;
    private readonly IClock _clock;

    public CheckoutService(ICheckoutStore store, IPaymentProvider paypal, IProviderStore provider, IEmailSender email, IClock clock)
    {
        _store = store;
        _paypal = paypal;
        _provider = provider;
        _email = email;
        _clock = clock;
    }

    /// <summary>Approved but not ACTIVE for this long → Failed (reconciliation, step 2.6).</summary>
    public static readonly TimeSpan ApprovedLimit = TimeSpan.FromHours(24);

    // ------------------------------------------------------------------- start

    /// <summary>
    /// Starts a checkout for an organization: convert a trial, resubscribe, or change plan.
    /// The same idempotency key returns the same checkout (a double click, a retried request).
    /// </summary>
    public async Task<CheckoutResult> StartAsync(StartCheckout request, CancellationToken ct = default)
    {
        if (!_paypal.IsConfigured) return CheckoutResult.Fail("Online payment is not available yet. Please contact us.");
        if (request.Purpose == CheckoutPurpose.New) return CheckoutResult.Fail("New organizations check out from the website (Phase 3).");

        var existing = await _store.FindByIdempotencyKeyAsync(request.IdempotencyKey.Trim(), ct).ConfigureAwait(false);
        if (existing != null)
        {
            return existing.TenantId == request.TenantId && existing.PlanPriceId == request.PlanPriceId && existing.Purpose == request.Purpose
                ? (existing.Status == CheckoutStatus.Failed ? CheckoutResult.Fail(existing.FailureReason ?? "This checkout failed.", existing) : CheckoutResult.Ok(existing))
                : CheckoutResult.Fail("This request was already used for a different checkout.");
        }

        if (!_paypal.IsAllowedReturnUrl(request.ReturnUrl) || !_paypal.IsAllowedReturnUrl(request.CancelUrl))
            return CheckoutResult.Fail("The return address is not allowed.");

        var found = await _store.FindPriceAsync(request.PlanPriceId, ct).ConfigureAwait(false);
        if (found is not var (plan, price) || !plan.IsOffered || price.Status != PlanStatus.Active) return CheckoutResult.Fail("That plan or price is not offered.");
        if (price.PayPalPlanId == null) return CheckoutResult.Fail("That price is not available for online payment yet.");

        var subscription = await _store.FindSubscriptionAsync(request.TenantId, ct).ConfigureAwait(false);
        if (subscription == null) return CheckoutResult.Fail("This organization has no subscription.");
        var refusal = Refusal(request.Purpose, subscription, price);
        if (refusal != null) return CheckoutResult.Fail(refusal);

        var now = _clock.UtcNow;
        CheckoutSession session;
        try
        {
            session = CheckoutSession.Create(request.Purpose, request.TenantId, null, price, request.ReturnUrl, request.CancelUrl, request.IdempotencyKey, request.Actor, now);
        }
        catch (ArgumentException e)
        {
            return CheckoutResult.Fail(e.Message);
        }
        await _store.AddAsync(session, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false); // the idempotency key is taken before PayPal is called

        try
        {
            if (request.Purpose == CheckoutPurpose.ChangePlan)
            {
                var approve = await _paypal.ReviseSubscriptionAsync(subscription.ExternalSubscriptionId!, price.PayPalPlanId, request.ReturnUrl, request.CancelUrl, ct).ConfigureAwait(false);
                session.Issued(subscription.ExternalSubscriptionId!, price.PayPalPlanId, approve ?? request.ReturnUrl);
            }
            else
            {
                var created = await _paypal.CreateSubscriptionAsync(price.PayPalPlanId, session.Reference, request.ReturnUrl, request.CancelUrl, $"inprem-checkout-{session.Id:N}", ct).ConfigureAwait(false);
                session.Issued(created.Id, price.PayPalPlanId, created.ApprovalUrl);
            }
        }
        catch (PaymentProviderException e)
        {
            session.Fail($"PayPal: {e.Message}", now);
            await AuditAsync(request.Actor, "checkout.failed", session, $"Could not start: {e.Message}{Debug(e)}", ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            return CheckoutResult.Fail("PayPal is not answering right now. Please try again in a few minutes.", session);
        }

        await AuditAsync(request.Actor, "checkout.started", session, $"{Purpose(session.Purpose)}: {plan.Name}, {PlanService.Describe(price)} (PayPal {session.ExternalSubscriptionId})", ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return CheckoutResult.Ok(session);
    }

    private static string? Refusal(CheckoutPurpose purpose, Subscription s, PlanPrice price) => purpose switch
    {
        CheckoutPurpose.Convert when s.Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Expired) || s.Mode == BillingMode.Free =>
            "Only an organization on a trial can start paying this way.",
        CheckoutPurpose.Resubscribe when s.Status is not (SubscriptionStatus.Expired or SubscriptionStatus.Canceled or SubscriptionStatus.Suspended) =>
            "Only an expired, cancelled or suspended subscription can be started again.",
        CheckoutPurpose.ChangePlan when s.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue) || s.Mode != BillingMode.PayPal || s.ExternalSubscriptionId == null =>
            "Only an active PayPal subscription can change plan.",
        CheckoutPurpose.ChangePlan when s.PlanPriceId == price.Id => "That is already the current price.",
        _ => null,
    };

    // ----------------------------------------------------------------- confirm

    /// <summary>
    /// Asks PayPal about the checkout and applies what PayPal says (design 7.5). Checks before
    /// activating: custom_id is this checkout, plan_id is the plan it was created for (and so
    /// the amount and currency of our catalogue), the PayPal subscription belongs to no other
    /// organization, and the checkout has not expired. Any mismatch fails the checkout and alerts
    /// the provider. Safe to call any number of times, from any source.
    /// </summary>
    public Task<CheckoutResult> ConfirmAsync(Guid sessionId, CheckoutSource source, CancellationToken ct = default) =>
        LockedAsync(sessionId, async session =>
        {
            if (session.IsFinal || session.ExternalSubscriptionId == null) return Result(session);

            PayPalSubscription? paypal;
            try
            {
                paypal = await _paypal.GetSubscriptionAsync(session.ExternalSubscriptionId, ct).ConfigureAwait(false);
            }
            catch (PaymentProviderException)
            {
                return CheckoutResult.Ok(session); // PayPal not reachable: nothing changes; another source will ask again
            }

            var now = _clock.UtcNow;
            if (paypal == null)
            {
                if (session.ExpireIfOver(now)) await FinishAsync(session, source, "checkout.expired", "PayPal does not know the subscription; not approved in time.", ct).ConfigureAwait(false);
                return Result(session);
            }

            var mismatch = await MismatchAsync(session, paypal, ct).ConfigureAwait(false);
            if (mismatch != null)
            {
                session.Fail(mismatch, now);
                await FinishAsync(session, source, "checkout.failed", $"Verification failed: {mismatch}", ct).ConfigureAwait(false);
                await AlertProviderAsync("Checkout failed verification", $"Checkout {session.Reference} for organization {session.TenantId}: {mismatch}. Nothing was activated.", ct).ConfigureAwait(false);
                return Result(session);
            }

            // A plan change: the subscription is ACTIVE all along; the change is done only when PayPal
            // shows the new plan (after the owner approved the revision).
            if (session.Purpose == CheckoutPurpose.ChangePlan && paypal.PlanId != session.PayPalPlanId)
            {
                if (session.ExpireIfOver(now)) await FinishAsync(session, source, "checkout.expired", "The plan change was not approved in time.", ct).ConfigureAwait(false);
                return Result(session);
            }

            if (paypal.State is PayPalSubscriptionState.ApprovalPending && session.ExpireIfOver(now))
            {
                await FinishAsync(session, source, "checkout.expired", "Not approved in time.", ct).ConfigureAwait(false);
                return Result(session);
            }

            var before = session.Status;
            session.Apply(paypal.State, now);
            if (session.Status == before && session.Status != CheckoutStatus.Active) return Result(session);

            switch (session.Status)
            {
                case CheckoutStatus.Approved:
                    await AwaitActivationAsync(session, source, ct).ConfigureAwait(false);
                    break;
                case CheckoutStatus.Active:
                    await ActivateAsync(session, paypal, source, ct).ConfigureAwait(false);
                    break;
                default:
                    await FinishAsync(session, source, session.Status == CheckoutStatus.Cancelled ? "checkout.cancelled" : session.Status == CheckoutStatus.Expired ? "checkout.expired" : "checkout.failed",
                        session.FailureReason ?? $"PayPal reported {paypal.RawStatus}.", ct).ConfigureAwait(false);
                    break;
            }
            return Result(session);
        }, ct);

    /// <summary>
    /// Runs <paramref name="work"/> on the checkout under its organization's billing lock (the
    /// same lock webhooks and reconciliation take), on a fresh read made after the lock.
    /// </summary>
    private async Task<CheckoutResult> LockedAsync(Guid sessionId, Func<CheckoutSession, Task<CheckoutResult>> work, CancellationToken ct)
    {
        var first = await _store.FindAsync(sessionId, ct).ConfigureAwait(false);
        if (first == null) return CheckoutResult.Fail("No such checkout.");
        return await _store.WithLockAsync(BillingLocks.For(first), async () =>
        {
            _store.Reset();
            var session = await _store.FindAsync(sessionId, ct).ConfigureAwait(false);
            return session == null ? CheckoutResult.Fail("No such checkout.") : await work(session).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async Task<string?> MismatchAsync(CheckoutSession session, PayPalSubscription paypal, CancellationToken ct)
    {
        // A plan change keeps the subscription's original custom_id; a new subscription must carry this checkout's.
        if (session.Purpose != CheckoutPurpose.ChangePlan && paypal.CustomId != session.Reference) return "custom_id does not match this checkout";
        if (paypal.Id != session.ExternalSubscriptionId) return "PayPal returned another subscription";

        // The plan: our catalogue's PayPal plan for this price (prices never change, so neither do amount and currency).
        var found = await _store.FindPriceAsync(session.PlanPriceId, ct).ConfigureAwait(false);
        if (found is not var (_, price) || price.PayPalPlanId != session.PayPalPlanId) return "the price is not in our catalogue as checked out";
        var planChanged = paypal.PlanId == session.PayPalPlanId;
        if (!planChanged && session.Purpose != CheckoutPurpose.ChangePlan) return "plan_id is not the plan this checkout was created for";
        if (paypal.LastPaymentCurrency != null && paypal.LastPaymentCurrency != price.Currency) return "the payment currency is not the price's";
        if (paypal.LastPaymentAmount != null && paypal.LastPaymentAmount > price.Amount && session.Purpose != CheckoutPurpose.ChangePlan) return "the payment amount is more than the price";

        var owner = await _store.TenantWithExternalIdAsync(paypal.Id, ct).ConfigureAwait(false);
        if (owner != null && owner != session.TenantId) return "the PayPal subscription is linked to another organization";
        return null;
    }

    private async Task AwaitActivationAsync(CheckoutSession session, CheckoutSource source, CancellationToken ct)
    {
        if (session.Purpose != CheckoutPurpose.ChangePlan)
        {
            var (subscription, price) = await SubscriptionAndPriceAsync(session, ct).ConfigureAwait(false);
            subscription.AwaitActivation(session.ExternalSubscriptionId!, price, _clock.UtcNow);
        }
        await AuditAsync(Actor(source), "checkout.approved", session, "PayPal: approved, waiting for ACTIVE.", ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// ActivateSubscription (step 2.4): PayPal says ACTIVE. Idempotent twice over: the lock lets
    /// one source at a time in, and <see cref="Subscription.Activate"/> changes nothing the
    /// second time. Audit and confirmation email only when it really changed.
    /// </summary>
    private async Task ActivateAsync(CheckoutSession session, PayPalSubscription paypal, CheckoutSource source, CancellationToken ct)
    {
        var (subscription, price) = await SubscriptionAndPriceAsync(session, ct).ConfigureAwait(false);
        var now = _clock.UtcNow;
        bool changed;
        if (session.Purpose == CheckoutPurpose.ChangePlan)
        {
            changed = subscription.ChangePrice(price); // ConfirmAsync gets here only once PayPal shows the new plan
            subscription.Refresh(paypal.StartUtc, paypal.NextBillingUtc, paypal.OutstandingBalance);
        }
        else
        {
            changed = subscription.Activate(paypal.Id, price, paypal.LastPaymentUtc ?? paypal.StartUtc, paypal.NextBillingUtc, now);
        }

        var tenantName = await _store.TenantNameAsync(subscription.TenantId, ct).ConfigureAwait(false) ?? $"organization {subscription.TenantId}";
        await AuditAsync(Actor(source), "checkout.active", session, $"PayPal: ACTIVE ({source}).", ct).ConfigureAwait(false);
        if (changed)
        {
            var action = session.Purpose == CheckoutPurpose.ChangePlan ? "subscription.plan-changed" : "subscription.activated";
            await _store.AddAuditAsync(AuditEntry.Record(Actor(source), action, subscription.Id.ToString(),
                $"{tenantName}: {PlanService.Describe(price)}, PayPal {paypal.Id}, next billing {paypal.NextBillingUtc:yyyy-MM-dd}", now, subscription.TenantId), ct).ConfigureAwait(false);
        }
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        if (changed)
        {
            var (plan, _) = (await _store.FindPriceAsync(price.Id, ct).ConfigureAwait(false))!.Value;
            await EmailOwnersAsync(subscription.TenantId, session.Purpose == CheckoutPurpose.ChangePlan ? "Your plan has changed" : "Your subscription is active",
                $"Thank you. {tenantName} is now on {plan.Name} ({PlanService.Describe(price)}), paid through PayPal.",
                paypal.NextBillingUtc == null ? "PayPal will email you a receipt for each payment." : $"Next payment: {paypal.NextBillingUtc:d MMMM yyyy}. PayPal emails you a receipt for each payment.", ct).ConfigureAwait(false);
        }
    }

    private async Task<(Subscription Subscription, PlanPrice Price)> SubscriptionAndPriceAsync(CheckoutSession session, CancellationToken ct)
    {
        var subscription = await _store.FindSubscriptionAsync(session.TenantId!.Value, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The organization has no subscription.");
        var found = await _store.FindPriceAsync(session.PlanPriceId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("The price is gone.");
        return (subscription, found.Price);
    }

    // ---------------------------------------------------------- other endings

    /// <summary>The buyer came back through the cancel address.</summary>
    public Task<CheckoutResult> CancelByBuyerAsync(Guid sessionId, CancellationToken ct = default) =>
        LockedAsync(sessionId, async session =>
        {
            if (session.CancelByBuyer(_clock.UtcNow)) await FinishAsync(session, CheckoutSource.Confirm, "checkout.cancelled", "The buyer cancelled at PayPal.", ct).ConfigureAwait(false);
            return Result(session);
        }, ct);

    /// <summary>The owner cancels a PayPal subscription: PayPal first, then here (access continues to the period end).</summary>
    public async Task<CheckoutResult> CancelSubscriptionAsync(int tenantId, string reason, string actor, CancellationToken ct = default)
    {
        var subscription = await _store.FindSubscriptionAsync(tenantId, ct).ConfigureAwait(false);
        if (subscription == null || subscription.Mode != BillingMode.PayPal || subscription.ExternalSubscriptionId == null
            || subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.PendingActivation))
            return CheckoutResult.Fail("There is no active PayPal subscription to cancel.");
        if (string.IsNullOrWhiteSpace(reason)) return CheckoutResult.Fail("Give a reason.");

        try
        {
            await _paypal.CancelSubscriptionAsync(subscription.ExternalSubscriptionId, reason.Trim(), ct).ConfigureAwait(false);
        }
        catch (PaymentProviderException e)
        {
            await _store.AddAuditAsync(AuditEntry.Record(actor, "subscription.cancel-failed", subscription.Id.ToString(), $"PayPal: {e.Message}{Debug(e)}", _clock.UtcNow, tenantId), ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            return CheckoutResult.Fail("PayPal is not answering right now. Nothing was cancelled; please try again in a few minutes.");
        }

        subscription.Cancel(_clock.UtcNow);
        await _store.AddAuditAsync(AuditEntry.Record(actor, "subscription.canceled", subscription.Id.ToString(),
            $"{Cut(reason.Trim(), 300)} (access until {subscription.CurrentPeriodEndUtc:yyyy-MM-dd})", _clock.UtcNow, tenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        await EmailOwnersAsync(tenantId, "Your subscription is cancelled",
            subscription.CurrentPeriodEndUtc == null ? "Your subscription is cancelled." : $"Your subscription is cancelled. You keep full access until {subscription.CurrentPeriodEndUtc:d MMMM yyyy}.",
            "You can subscribe again at any time from the Billing page.", ct).ConfigureAwait(false);
        return new CheckoutResult(true, null, null);
    }

    // ------------------------------------------------------------------ helpers

    private async Task FinishAsync(CheckoutSession session, CheckoutSource source, string action, string details, CancellationToken ct)
    {
        await AuditAsync(Actor(source), action, session, details, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static CheckoutResult Result(CheckoutSession session) =>
        session.Status == CheckoutStatus.Failed ? CheckoutResult.Fail("We could not activate your subscription. Please contact us.", session) : CheckoutResult.Ok(session);

    private Task AuditAsync(string actor, string action, CheckoutSession session, string details, CancellationToken ct) =>
        _store.AddAuditAsync(AuditEntry.Record(actor, action, session.Id.ToString(), $"{session.Reference}: {details}", _clock.UtcNow, session.TenantId), ct);

    private static string Actor(CheckoutSource source) => source switch
    {
        CheckoutSource.Webhook => "paypal:webhook",
        CheckoutSource.Reconciliation => "job:reconciliation",
        _ => "paypal:confirm",
    };

    private async Task EmailOwnersAsync(int tenantId, string subject, string line1, string line2, CancellationToken ct)
    {
        foreach (var to in await _store.OwnerEmailsAsync(tenantId, ct).ConfigureAwait(false))
        {
            try
            {
                await _email.SendAsync(to, subject, $"<p>{WebUtility.HtmlEncode(line1)}</p><p>{WebUtility.HtmlEncode(line2)}</p>", ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The change is done and audited; an email that cannot be sent must not undo it.
                await _store.AddAuditAsync(AuditEntry.Record("system", "notification.failed", null, $"{subject}: {e.GetType().Name}", _clock.UtcNow, tenantId), ct).ConfigureAwait(false);
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Billing alerts go to active provider Owners and Billing users (SMS from Phase 5).</summary>
    private async Task AlertProviderAsync(string subject, string text, CancellationToken ct)
    {
        var to = (await _provider.ListProviderUsersAsync(ct).ConfigureAwait(false))
            .Where(u => u.Status == ProviderUserStatus.Active && u.Role is ProviderRole.Owner or ProviderRole.Billing).Select(u => u.Email).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var address in to)
        {
            try
            {
                await _email.SendAsync(address, $"[Inprem billing] {subject}", $"<p>{WebUtility.HtmlEncode(text)}</p>", ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await _store.AddAuditAsync(AuditEntry.Record("system", "notification.failed", null, $"{subject}: {e.GetType().Name}", _clock.UtcNow), ct).ConfigureAwait(false);
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
    }

    private static string Purpose(CheckoutPurpose purpose) => purpose switch
    {
        CheckoutPurpose.Convert => "Trial to paid",
        CheckoutPurpose.Resubscribe => "Subscribe again",
        CheckoutPurpose.ChangePlan => "Change plan",
        _ => "New organization",
    };

    private static string Debug(PaymentProviderException e) => e.DebugId == null ? "" : $" (PayPal debug id {e.DebugId})";

    private static string Cut(string value, int max) => value.Length > max ? value[..max] : value;
}
