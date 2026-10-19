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
/// Step 5.1 has the test message only; step 5.3 adds the alerts. Placeholders are written
/// {name}; {firstName} and {organization} are always filled in.
/// </summary>
public static class NotificationTemplates
{
    public const string TestMessage = "platform.test";

    private static readonly Dictionary<string, TemplateDefault> Defaults = new[]
    {
        new TemplateDefault(TestMessage, "A test message sent from the console, to check that messages arrive.", NotificationUrgency.Urgent,
            "Test message from Inprem Clock-in",
            "Hello {firstName},\n\nThis is a test message from Inprem Clock-in. If you can read it, messages reach you.",
            "Inprem Clock-in: test message. If you can read this, texts reach you. Reply STOP to opt out."),
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
}
