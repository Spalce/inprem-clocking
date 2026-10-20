using System.Globalization;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Domain.Provider;

namespace Inprem.Platform.Application.Notifications;

/// <summary>Which kind of Inprem alert: decides the roles that may get it and the user's choice that applies.</summary>
public enum ProviderAlertKind
{
    /// <summary>All roles; "New signups" on.</summary>
    NewSignup,

    /// <summary>Owners and Billing; "Billing problems" immediate (daily summary users get it in the summary).</summary>
    BillingProblem,

    /// <summary>Owners; "System problems" on.</summary>
    SystemProblem,
}

/// <summary>
/// The console users an Inprem alert goes to (Phase 5 step 5.3): by role, then by each user's choices
/// (<see cref="ProviderAlertPreferences"/>; defaults when they never chose).
/// </summary>
public sealed class ProviderAlertRecipients
{
    private readonly IRecipientDirectory _directory;
    private readonly INotificationStore _store;

    public ProviderAlertRecipients(IRecipientDirectory directory, INotificationStore store)
    {
        _directory = directory;
        _store = store;
    }

    public static IReadOnlyCollection<ProviderRole> RolesFor(ProviderAlertKind kind) => kind switch
    {
        ProviderAlertKind.NewSignup => new[] { ProviderRole.Owner, ProviderRole.Billing, ProviderRole.Support },
        ProviderAlertKind.BillingProblem => new[] { ProviderRole.Owner, ProviderRole.Billing },
        _ => new[] { ProviderRole.Owner },
    };

    /// <summary>The users who get <paramref name="kind"/> now; for billing problems, <paramref name="summary"/> picks the daily-summary users instead.</summary>
    public async Task<IReadOnlyList<NotificationRecipient>> ForAsync(ProviderAlertKind kind, bool summary = false, CancellationToken ct = default)
    {
        var users = await _directory.ProviderUsersAsync(RolesFor(kind), ct).ConfigureAwait(false);
        if (users.Count == 0) return users;
        var chosen = await _store.PreferencesAsync(users.Select(u => u.Ref).ToList(), ct).ConfigureAwait(false);
        return users.Where(u =>
        {
            var p = chosen.GetValueOrDefault(u.Ref) ?? ProviderAlertPreferences.Defaults(u.Ref);
            return kind switch
            {
                ProviderAlertKind.NewSignup => p.NewSignups,
                ProviderAlertKind.BillingProblem => p.BillingProblems == (summary ? AlertDelivery.DailySummary : AlertDelivery.Immediate),
                _ => p.SystemProblems,
            };
        }).ToList();
    }
}

/// <summary>
/// The billing and Inprem alerts (Phase 5 step 5.3, design Appendix C), raised where things happen:
/// subscription sync (payment failed, suspended), the daily check (trials ending, invoices overdue,
/// the provider's daily summary), invoices (issued), website signups, webhook and job failures and
/// the texts' spend. An alert never breaks what raised it: a failure is swallowed (the event itself
/// is in the audit log) and its unsaved messages are dropped. Every alert has an event key, so a
/// repeat (a webhook delivered twice, a re-run job) sends nothing twice.
/// </summary>
public sealed class BillingAlerts
{
    /// <summary>The audit actions that count as billing problems for the daily summary.</summary>
    public static readonly string[] SummaryActions = { "subscription.past-due", "subscription.suspended" };

    private readonly NotificationService _notifications;
    private readonly ProviderAlertRecipients _recipients;
    private readonly INotificationStore _store;
    private readonly IRecipientDirectory _directory;
    private readonly NotificationOptions _options;

    public BillingAlerts(NotificationService notifications, ProviderAlertRecipients recipients, INotificationStore store, IRecipientDirectory directory, NotificationOptions options)
    {
        _notifications = notifications;
        _recipients = recipients;
        _store = store;
        _directory = directory;
        _options = options;
    }

    private string BillingUrl => Url(_options.AppBaseUrl, "/Billing");

    private string ConsoleUrl(int? tenantId) => Url(_options.AdminBaseUrl, tenantId == null ? "" : $"/Tenants/Details/{tenantId}");

    private static string Url(string? root, string path) => string.IsNullOrWhiteSpace(root) ? path : root.TrimEnd('/') + path;

    private static string Money(decimal amount, string currency = "USD") => $"{amount.ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

    private static string Day(DateTime utc) => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ to the owners

    /// <summary>The trial ends in about <paramref name="days"/> days (7, or 1: "tomorrow").</summary>
    public Task TrialEndingAsync(int tenantId, DateTime trialEndsUtc, int days, CancellationToken ct = default) =>
        SafelyAsync(() => _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.TrialEnding, tenantId, Audience.OrganizationOwners,
            new Dictionary<string, string>
            {
                ["when"] = days <= 1 ? "tomorrow" : $"in {days} days",
                ["trialEnd"] = trialEndsUtc.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
                ["billingUrl"] = BillingUrl,
            }, EventKey: $"trial-ending-{days}:{tenantId}:{Day(trialEndsUtc)}"), ct));

    /// <summary>PayPal could not collect a payment (the subscription is past due): the owners, and Inprem.</summary>
    public async Task PaymentFailedAsync(int tenantId, decimal outstanding, DateTime nowUtc, CancellationToken ct = default)
    {
        await SafelyAsync(() => _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.PaymentFailed, tenantId, Audience.OrganizationOwners,
            new Dictionary<string, string> { ["amount"] = Money(outstanding), ["billingUrl"] = BillingUrl },
            EventKey: $"payment-failed:{tenantId}:{Day(nowUtc)}"), ct)).ConfigureAwait(false);
        await BillingProblemAsync(tenantId, "a payment failed", $"PayPal could not collect {Money(outstanding)}; PayPal retries.", $"payment-failed:{tenantId}:{Day(nowUtc)}", ct).ConfigureAwait(false);
    }

    /// <summary>PayPal suspended the subscription (read-only): the owners, and Inprem.</summary>
    public async Task SuspendedAsync(int tenantId, decimal outstanding, DateTime nowUtc, CancellationToken ct = default)
    {
        await SafelyAsync(() => _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.Suspended, tenantId, Audience.OrganizationOwners,
            new Dictionary<string, string> { ["billingUrl"] = BillingUrl },
            EventKey: $"suspended:{tenantId}:{Day(nowUtc)}"), ct)).ConfigureAwait(false);
        await BillingProblemAsync(tenantId, "the subscription was suspended", $"PayPal suspended it with {Money(outstanding)} outstanding; the organization is read-only.", $"suspended:{tenantId}:{Day(nowUtc)}", ct).ConfigureAwait(false);
    }

    /// <summary>A new invoice was sent: a text to the owners (the invoice email is the invoice's own).</summary>
    public Task InvoiceIssuedAsync(Guid invoiceId, int tenantId, string number, decimal total, string currency, DateOnly dueDate, CancellationToken ct = default) =>
        SafelyAsync(() => _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.InvoiceIssued, tenantId, Audience.OrganizationOwners,
            new Dictionary<string, string>
            {
                ["number"] = number, ["amount"] = Money(total, currency), ["dueDate"] = dueDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
            }, EventKey: $"invoice-issued:{invoiceId}", Channels: new[] { NotificationChannel.Sms }), ct));

    /// <summary>An invoice is past its due date and not fully paid (once per invoice).</summary>
    public Task InvoiceOverdueAsync(Guid invoiceId, int tenantId, string number, decimal outstanding, string currency, DateOnly dueDate, CancellationToken ct = default) =>
        SafelyAsync(() => _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.InvoiceOverdue, tenantId, Audience.OrganizationOwners,
            new Dictionary<string, string>
            {
                ["number"] = number, ["amount"] = Money(outstanding, currency), ["dueDate"] = dueDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
                ["billingUrl"] = BillingUrl,
            }, EventKey: $"invoice-overdue:{invoiceId}"), ct));

    // ------------------------------------------------------------------ to Inprem

    /// <summary>An organization signed up on the website.</summary>
    public async Task NewSignupAsync(int tenantId, string mode, string planName, CancellationToken ct = default)
    {
        await SafelyAsync(async () =>
        {
            var organization = await _directory.OrganizationAsync(tenantId, ct).ConfigureAwait(false);
            var recipients = await _recipients.ForAsync(ProviderAlertKind.NewSignup, ct: ct).ConfigureAwait(false);
            return await _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.NewSignup, null, Audience.Explicit,
                new Dictionary<string, string>
                {
                    ["organization"] = organization?.Name ?? $"Organization {tenantId}", ["mode"] = mode, ["plan"] = planName, ["consoleUrl"] = ConsoleUrl(tenantId),
                }, EventKey: $"new-signup:{tenantId}", Recipients: recipients), ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>Something needs Inprem's Owners now (urgent, also at night). One alert per kind per hour.</summary>
    public Task SystemProblemAsync(string kind, string problem, string details, DateTime nowUtc, CancellationToken ct = default) =>
        SafelyAsync(async () =>
        {
            var recipients = await _recipients.ForAsync(ProviderAlertKind.SystemProblem, ct: ct).ConfigureAwait(false);
            return await _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.SystemProblem, null, Audience.Explicit,
                new Dictionary<string, string> { ["problem"] = problem, ["details"] = details, ["consoleUrl"] = ConsoleUrl(null) },
                EventKey: $"system:{kind}:{nowUtc.ToString("yyyyMMddHH", CultureInfo.InvariantCulture)}", Recipients: recipients), ct).ConfigureAwait(false);
        });

    /// <summary>
    /// The daily summary for console users who chose it (run by the daily check): the billing problems
    /// of the last day, one message. Nothing is sent on a day without problems.
    /// </summary>
    public Task DailySummaryAsync(DateTime nowUtc, CancellationToken ct = default) =>
        SafelyAsync(async () =>
        {
            var recipients = await _recipients.ForAsync(ProviderAlertKind.BillingProblem, summary: true, ct).ConfigureAwait(false);
            if (recipients.Count == 0) return NotificationRaiseResult.Nothing;
            var problems = await _store.AuditSinceAsync(SummaryActions, nowUtc.AddDays(-1), ct).ConfigureAwait(false);
            if (problems.Count == 0) return NotificationRaiseResult.Nothing;

            var lines = new List<string>();
            foreach (var problem in problems)
            {
                var name = problem.TenantId is int id ? (await _directory.OrganizationAsync(id, ct).ConfigureAwait(false))?.Name ?? $"Organization {id}" : "(no organization)";
                lines.Add($"- {problem.OccurredUtc:yyyy-MM-dd HH:mm} UTC, {name}: {(problem.Action == "subscription.suspended" ? "suspended" : "payment failed")}");
            }
            return await _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.BillingSummary, null, Audience.Explicit,
                new Dictionary<string, string> { ["count"] = problems.Count.ToString(CultureInfo.InvariantCulture), ["lines"] = string.Join("\n", lines), ["consoleUrl"] = ConsoleUrl(null) },
                EventKey: $"billing-summary:{Day(nowUtc)}", Recipients: recipients), ct).ConfigureAwait(false);
        });

    private Task BillingProblemAsync(int tenantId, string problem, string details, string eventKey, CancellationToken ct) =>
        SafelyAsync(async () =>
        {
            var recipients = await _recipients.ForAsync(ProviderAlertKind.BillingProblem, ct: ct).ConfigureAwait(false);
            var organization = await _directory.OrganizationAsync(tenantId, ct).ConfigureAwait(false);
            return await _notifications.RaiseAsync(new NotificationEvent(NotificationTemplates.BillingProblem, null, Audience.Explicit,
                new Dictionary<string, string>
                {
                    ["organization"] = organization?.Name ?? $"Organization {tenantId}", ["problem"] = problem, ["details"] = details, ["consoleUrl"] = ConsoleUrl(tenantId),
                }, EventKey: "provider-" + eventKey, Recipients: recipients), ct).ConfigureAwait(false);
        });

    private async Task SafelyAsync(Func<Task<NotificationRaiseResult>> raise)
    {
        try
        {
            await raise().ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _store.DiscardPending();
        }
    }
}
