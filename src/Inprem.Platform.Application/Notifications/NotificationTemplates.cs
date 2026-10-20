using System.Net;
using System.Text.RegularExpressions;
using Inprem.Platform.Domain.Notifications;

namespace Inprem.Platform.Application.Notifications;

/// <summary>
/// The default wording of one message. A message has an email, a text or both; the console can
/// replace either wording (a <see cref="NotificationTemplate"/> row) or switch it off.
/// </summary>
public sealed record TemplateDefault(string Key, string Description, NotificationUrgency Urgency, string? EmailSubject, string? EmailBody, string? SmsBody);

/// <summary>
/// Every message the platform can send, with its default wording (Phase 5, design Appendix C).
/// Placeholders are written {name}; {firstName} and {organization} are always filled in. Texts end
/// with "Reply STOP to opt out" (consent rules). "Invoice issued" is a text only: the invoice email
/// itself is sent by the invoice (Inprem's for manual invoices, PayPal's for PayPal invoices).
/// </summary>
public static class NotificationTemplates
{
    public const string TestMessage = "platform.test";
    public const string TrialEnding = "billing.trial-ending";
    public const string PaymentFailed = "billing.payment-failed";
    public const string Suspended = "billing.suspended";
    public const string InvoiceIssued = "billing.invoice-issued";
    public const string InvoiceOverdue = "billing.invoice-overdue";
    public const string NewSignup = "provider.new-signup";
    public const string BillingProblem = "provider.billing-problem";
    public const string BillingSummary = "provider.billing-summary";
    public const string SystemProblem = "provider.system-problem";

    private static readonly Dictionary<string, TemplateDefault> Defaults = new[]
    {
        new TemplateDefault(TestMessage, "A test message sent from the console, to check that messages arrive.", NotificationUrgency.Urgent,
            "Test message from Inprem Clock-in",
            "Hello {firstName},\n\nThis is a test message from Inprem Clock-in. If you can read it, messages reach you.",
            "Inprem Clock-in: test message. If you can read this, texts reach you. Reply STOP to opt out."),

        new TemplateDefault(TrialEnding, "To the owners: the free trial ends in 7 days, and again the day before.", NotificationUrgency.Normal,
            "The Inprem Clock-in trial of {organization} ends {when}",
            "Hello {firstName},\n\nThe free trial of {organization} ends {when} ({trialEnd}). To keep adding records and using the kiosk after that, choose a plan on the Billing page:\n{billingUrl}\n\nYour data stays safe either way: without a plan the account becomes read-only.",
            "Inprem Clock-in: the {organization} trial ends {when}. Choose a plan: {billingUrl} Reply STOP to opt out."),
        new TemplateDefault(PaymentFailed, "To the owners: PayPal could not collect a payment.", NotificationUrgency.Normal,
            "PayPal could not collect the payment for {organization}",
            "Hello {firstName},\n\nPayPal could not collect {amount} for the {organization} subscription. PayPal tries again over the next days. Please check the payment method in your PayPal account.\n\nBilling page: {billingUrl}",
            "Inprem Clock-in: PayPal could not collect {amount} for {organization}. Please check your PayPal payment method. Reply STOP to opt out."),
        new TemplateDefault(Suspended, "To the owners: PayPal suspended the subscription (the account is read-only).", NotificationUrgency.Normal,
            "The {organization} subscription is suspended",
            "Hello {firstName},\n\nPayPal suspended the {organization} subscription because payments could not be collected. The account is read-only until it is paid: you can still sign in, run reports and download exports, but nothing can be changed and the kiosk is paused.\n\nPay the balance in PayPal, or contact Inprem. Billing page: {billingUrl}",
            "Inprem Clock-in: the {organization} subscription is suspended (read-only) until the balance is paid in PayPal. Reply STOP to opt out."),
        new TemplateDefault(InvoiceIssued, "To the owners, by text: a new invoice (the invoice itself comes by email).", NotificationUrgency.Normal,
            null, null,
            "Inprem Clock-in: invoice {number} for {amount} is due {dueDate}. It was sent to your email. Reply STOP to opt out."),
        new TemplateDefault(InvoiceOverdue, "To the owners: an invoice is past its due date.", NotificationUrgency.Normal,
            "Invoice {number} for {organization} is overdue",
            "Hello {firstName},\n\nInvoice {number} for {amount} was due {dueDate} and is not paid yet. If you have paid in the meantime, thank you, and please ignore this message.\n\nBilling page: {billingUrl}",
            "Inprem Clock-in: invoice {number} for {amount} was due {dueDate} and is not paid yet. Reply STOP to opt out."),

        new TemplateDefault(NewSignup, "To Inprem: an organization signed up on the website.", NotificationUrgency.Normal,
            "[Inprem] New signup: {organization}",
            "{organization} signed up on the website: {mode}, plan {plan}.\n\nConsole: {consoleUrl}",
            "Inprem: new signup, {organization} ({mode}, {plan})."),
        new TemplateDefault(BillingProblem, "To Inprem (Owners and Billing): a payment failed or a subscription was suspended.", NotificationUrgency.Normal,
            "[Inprem billing] {organization}: {problem}",
            "{organization}: {problem}.\n\n{details}\n\nConsole: {consoleUrl}",
            "Inprem billing: {organization}: {problem}."),
        new TemplateDefault(BillingSummary, "To Inprem users who chose a daily summary of billing problems.", NotificationUrgency.Normal,
            "[Inprem billing] Daily summary: {count} billing problem(s)",
            "Billing problems of the last day:\n\n{lines}\n\nConsole: {consoleUrl}",
            "Inprem billing: {count} billing problem(s) in the last day. See the console."),
        new TemplateDefault(SystemProblem, "To Inprem's Owners: something needs attention now (urgent: also at night).", NotificationUrgency.Urgent,
            "[Inprem] System problem: {problem}",
            "{problem}\n\n{details}\n\nConsole: {consoleUrl}",
            "Inprem system problem: {problem}."),
    }.ToDictionary(t => t.Key, StringComparer.Ordinal);

    public static IReadOnlyCollection<TemplateDefault> All => Defaults.Values;

    public static TemplateDefault? Find(string key) => Defaults.GetValueOrDefault(key);

    private static readonly Regex Placeholder = new(@"\{([A-Za-z][A-Za-z0-9]*)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The text with its placeholders filled in; an unknown placeholder is left as it is.</summary>
    public static string Render(string text, IReadOnlyDictionary<string, string> values) =>
        Placeholder.Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    /// <summary>A plain-text email body as HTML: encoded, a paragraph per blank-line block, line breaks kept.</summary>
    public static string ToHtml(string plainText) =>
        string.Concat(plainText.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => $"<p>{WebUtility.HtmlEncode(p.Trim()).Replace("\n", "<br>")}</p>"));
}

/// <summary>
/// The exact wording people agree to for texts (design Appendix C: message types, "Msg &amp; data rates
/// may apply", "Reply STOP to opt out"). It is stored with each consent, so a change here is a new
/// wording for new consents only; the website must show this text next to its box.
/// </summary>
public static class ConsentWording
{
    public const string OwnerAlerts =
        "Text me about my organization's Inprem Clock-in account (trial ending, payment problems, suspension, invoices). " +
        "Msg frequency varies. Msg & data rates may apply. Reply STOP to opt out, HELP for help.";

    /// <summary>For Inprem's console users, on "My alerts" (step 5.3).</summary>
    public const string ProviderAlerts =
        "Text me the Inprem console alerts I chose (new signups, billing problems, system problems). " +
        "Msg frequency varies. Msg & data rates may apply. Reply STOP to opt out, HELP for help.";
}
