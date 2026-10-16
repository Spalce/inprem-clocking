using System.Net;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;

namespace Inprem.Platform.Application.Billing;

public interface IReconciliationStore
{
    /// <summary>Checkouts not final yet (Created, AwaitingApproval, Approved), oldest first.</summary>
    Task<IReadOnlyList<Guid>> OpenCheckoutsAsync(int max, CancellationToken ct = default);

    /// <summary>The PayPal subscription id of every organization billed through PayPal.</summary>
    Task<IReadOnlyList<string>> PayPalSubscriptionIdsAsync(CancellationToken ct = default);

    /// <summary>Organizations whose trial has ended but are still Trialing.</summary>
    Task<IReadOnlyList<int>> EndedTrialsAsync(DateTime nowUtc, CancellationToken ct = default);
}

/// <summary>What one reconciliation run did.</summary>
public sealed record ReconciliationReport(int Checked, int Corrected, int Failed, IReadOnlyList<string> Corrections);

/// <summary>
/// Reconciliation (Phase 2 step 2.6, design 7.5 source 3): catches up whatever a webhook did not
/// bring (P2-6: a dropped webhook is corrected within one cycle).
/// <list type="bullet">
/// <item>Every 5 minutes: open checkouts are confirmed with PayPal; checkouts past their time
/// expire; checkouts Approved for longer than 24 hours fail and alert the provider.</item>
/// <item>Daily: every PayPal subscription is compared with PayPal and corrected (each correction
/// audited by <see cref="SubscriptionSync"/>), the provider is told what was corrected, and trials
/// that have ended are marked Expired (access already ends at the trial date, step 2.7).</item>
/// </list>
/// Every step uses the same idempotent use cases as confirm and webhooks, under the same lock.
/// </summary>
public sealed class BillingReconciliation
{
    public const int MaxCheckoutsPerRun = 500;

    private readonly IReconciliationStore _reconciliation;
    private readonly ICheckoutStore _store;
    private readonly CheckoutService _checkout;
    private readonly SubscriptionSync _sync;
    private readonly IProviderStore _provider;
    private readonly IEmailSender _email;
    private readonly IClock _clock;
    private readonly InvoiceSync _invoices;

    public BillingReconciliation(IReconciliationStore reconciliation, ICheckoutStore store, CheckoutService checkout, SubscriptionSync sync, IProviderStore provider, IEmailSender email, IClock clock, InvoiceSync invoices)
    {
        _invoices = invoices;
        _reconciliation = reconciliation;
        _store = store;
        _checkout = checkout;
        _sync = sync;
        _provider = provider;
        _email = email;
        _clock = clock;
    }

    /// <summary>The 5-minute run over open checkouts.</summary>
    public async Task<ReconciliationReport> CheckoutsAsync(CancellationToken ct = default)
    {
        var corrections = new List<string>();
        var failed = 0;
        var ids = await _reconciliation.OpenCheckoutsAsync(MaxCheckoutsPerRun, ct).ConfigureAwait(false);
        foreach (var id in ids)
        {
            try
            {
                var before = (await _store.FindAsync(id, ct).ConfigureAwait(false))?.Status;
                var result = await _checkout.ConfirmAsync(id, CheckoutSource.Reconciliation, ct).ConfigureAwait(false);
                var after = await _checkout.EndStaleAsync(id, ct).ConfigureAwait(false) ?? result.Session?.Status;
                if (after != null && after != before) corrections.Add($"Checkout {result.Session?.Reference ?? id.ToString()}: {before} → {after}");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++; // one bad checkout must not stop the others; it is tried again next run
                _store.Reset();
            }
        }
        return new ReconciliationReport(ids.Count, corrections.Count, failed, corrections);
    }

    /// <summary>The daily run: every PayPal subscription, then ended trials; the provider gets a summary of corrections.</summary>
    public async Task<ReconciliationReport> DailyAsync(CancellationToken ct = default)
    {
        var corrections = new List<string>();
        var failed = 0;
        var ids = await _reconciliation.PayPalSubscriptionIdsAsync(ct).ConfigureAwait(false);
        foreach (var id in ids)
        {
            try
            {
                var change = await _sync.SyncAsync(id, "job:reconciliation", ct).ConfigureAwait(false);
                if (change != null) corrections.Add($"PayPal {id}: {change}");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed++;
                _store.Reset();
            }
        }

        // PayPal invoices not finished (step 4.3): a dropped INVOICING webhook is corrected here.
        var (invoiceChanges, invoiceFailures) = await _invoices.SyncOpenAsync("job:reconciliation", ct).ConfigureAwait(false);
        corrections.AddRange(invoiceChanges);
        failed += invoiceFailures;

        var trials = await _reconciliation.EndedTrialsAsync(_clock.UtcNow, ct).ConfigureAwait(false);
        foreach (var tenant in trials)
        {
            var expired = await _store.WithLockAsync(BillingLocks.Tenant(tenant), async () =>
            {
                _store.Reset();
                var subscription = await _store.FindSubscriptionAsync(tenant, ct).ConfigureAwait(false);
                if (subscription == null || !subscription.ExpireTrialIfOver(_clock.UtcNow)) return false;
                await _store.AddAuditAsync(AuditEntry.Record("job:reconciliation", "subscription.trial-expired", subscription.Id.ToString(),
                    $"Trial ended {subscription.TrialEndsUtc:yyyy-MM-dd}; read-only until the organization subscribes.", _clock.UtcNow, tenant), ct).ConfigureAwait(false);
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
                return true;
            }, ct).ConfigureAwait(false);
            if (expired) corrections.Add($"Organization {tenant}: trial ended");
        }

        var drift = corrections.Where(c => c.StartsWith("PayPal ", StringComparison.Ordinal)).ToList();
        if (drift.Count > 0 || failed > 0)
        {
            await AlertProviderAsync($"Daily PayPal check: {drift.Count} corrected, {failed} could not be checked",
                drift.Count == 0 ? new[] { "Nothing needed correcting." } : drift.ToArray(), failed, ct).ConfigureAwait(false);
        }
        return new ReconciliationReport(ids.Count + trials.Count, corrections.Count, failed, corrections);
    }

    private async Task AlertProviderAsync(string subject, IReadOnlyList<string> lines, int failed, CancellationToken ct)
    {
        var body = string.Concat(lines.Select(l => $"<p>{WebUtility.HtmlEncode(l)}</p>"))
                   + (failed > 0 ? $"<p>{failed} subscription(s) could not be checked (PayPal not answering); they are checked again tomorrow.</p>" : "")
                   + "<p>Each correction is in the audit log.</p>";
        var to = (await _provider.ListProviderUsersAsync(ct).ConfigureAwait(false))
            .Where(u => u.Status == ProviderUserStatus.Active && u.Role is ProviderRole.Owner or ProviderRole.Billing).Select(u => u.Email).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var address in to)
        {
            try
            {
                await _email.SendAsync(address, $"[Inprem billing] {subject}", body, ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                await _store.AddAuditAsync(AuditEntry.Record("system", "notification.failed", null, $"{subject}: {e.GetType().Name}", _clock.UtcNow), ct).ConfigureAwait(false);
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
