using System.Net;
using System.Net.Mail;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.PublicApi;

namespace Inprem.Platform.Application.PublicApi;

public interface IBillingAccessStore
{
    /// <summary>Organizations whose active Owner has this email (the account's email, not its user name), with their names.</summary>
    Task<IReadOnlyList<(int TenantId, string Name)>> OwnedOrganizationsAsync(string email, CancellationToken ct = default);

    Task<int> CodesSinceAsync(int tenantId, DateTime sinceUtc, CancellationToken ct = default);
    Task AddCodeAsync(BillingAccessCode code, CancellationToken ct = default);
    Task<BillingAccessCode?> FindCodeByHashAsync(string codeHash, CancellationToken ct = default);
    Task<string?> TenantNameAsync(int tenantId, CancellationToken ct = default);
    Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Sends the links later (a background job), so the request takes the same time whoever asks.</summary>
public interface IBillingAccessQueue
{
    void Enqueue(string email, string? websiteBase);
}

public enum BillingAccessRequestOutcome
{
    Accepted,
    Invalid,
    Captcha,
}

/// <summary>
/// Existing customers on the website (Phase 3 step 3.5, journeys J3 and J4, D27): an owner asks
/// for a link by email; the answer is always the same, so the API cannot reveal who is a customer.
/// The link carries a one-time code (15 minutes) that the website exchanges for a 30-minute
/// billing-access token for that one organization.
/// </summary>
public sealed class BillingAccessService
{
    public const int MaxLinksPerHour = 3;

    private readonly IBillingAccessStore _store;
    private readonly IBillingAccessQueue _queue;
    private readonly ICaptchaVerifier _captcha;
    private readonly IEmailSender _email;
    private readonly PublicApiTokens _tokens;
    private readonly IClock _clock;

    public BillingAccessService(IBillingAccessStore store, IBillingAccessQueue queue, ICaptchaVerifier captcha, IEmailSender email, PublicApiTokens tokens, IClock clock)
    {
        _store = store;
        _queue = queue;
        _captcha = captcha;
        _email = email;
        _tokens = tokens;
        _clock = clock;
    }

    /// <summary>The request: checks the form and the CAPTCHA, then always queues the same job (known email or not).</summary>
    public async Task<BillingAccessRequestOutcome> RequestAsync(string? email, string? captchaToken, string? remoteIp, string? websiteBase, CancellationToken ct = default)
    {
        if (!IsEmail(email)) return BillingAccessRequestOutcome.Invalid;
        if (!await _captcha.VerifyAsync(captchaToken, remoteIp, ct).ConfigureAwait(false)) return BillingAccessRequestOutcome.Captcha;
        _queue.Enqueue(email!.Trim(), websiteBase);
        return BillingAccessRequestOutcome.Accepted;
    }

    /// <summary>
    /// The job: if the email is an active Owner's, one code per organization (at most 3 per hour each)
    /// and one email with a link for each. Unknown emails: nothing happens, nothing is stored.
    /// </summary>
    public async Task SendLinksAsync(string email, string? websiteBase, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(websiteBase)) return; // no website address configured: no link can be built
        var organizations = await _store.OwnedOrganizationsAsync(email, ct).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var links = new List<(string Name, string Link)>();
        foreach (var (tenantId, name) in organizations)
        {
            if (await _store.CodesSinceAsync(tenantId, now.AddHours(-1), ct).ConfigureAwait(false) >= MaxLinksPerHour) continue;
            var code = SecretTokens.NewToken();
            await _store.AddCodeAsync(BillingAccessCode.Issue(tenantId, SecretTokens.Hash(code), now), ct).ConfigureAwait(false);
            await _store.AddAuditAsync(AuditEntry.Record("website:billing-access", "billing-access.link-sent", tenantId.ToString(), "One-time billing link emailed to the owner (15 minutes).", now, tenantId), ct).ConfigureAwait(false);
            links.Add((name, $"{websiteBase.TrimEnd('/')}/billing?code={Uri.EscapeDataString(code)}"));
        }
        if (links.Count == 0) return;
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        var body = "<p>You asked to manage your Inprem Clock-in subscription. Use the link for your organization; it works once, for 15 minutes:</p>"
                   + string.Concat(links.Select(l => $"<p>{WebUtility.HtmlEncode(l.Name)}: <a href=\"{WebUtility.HtmlEncode(l.Link)}\">{WebUtility.HtmlEncode(l.Link)}</a></p>"))
                   + "<p>If you did not ask for this, you can ignore this email.</p>";
        await _email.SendAsync(email, "Manage your Inprem Clock-in subscription", body, ct).ConfigureAwait(false);
    }

    /// <summary>The website exchanges the code (once) for a billing-access token.</summary>
    public async Task<(string Token, DateTime ExpiresUtc, string OrganizationName)?> ExchangeAsync(string? code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100) return null;
        var found = await _store.FindCodeByHashAsync(SecretTokens.Hash(code), ct).ConfigureAwait(false);
        var now = _clock.UtcNow;
        if (found == null || !found.TryUse(now)) return null;
        await _store.AddAuditAsync(AuditEntry.Record("website:billing-access", "billing-access.session-started", found.TenantId.ToString(), "Billing link used on the website (token for 30 minutes).", now, found.TenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        var (token, value) = await _tokens.IssueBillingAccessAsync(found.TenantId, ct).ConfigureAwait(false);
        return (value, token.ExpiresUtc, await _store.TenantNameAsync(found.TenantId, ct).ConfigureAwait(false) ?? "");
    }

    private static bool IsEmail(string? value)
    {
        var email = value?.Trim() ?? "";
        if (email.Length is < 3 or > 256 || email.Contains(' ')) return false;
        try
        {
            return new MailAddress(email).Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
