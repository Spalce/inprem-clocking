using System.Net.Mail;
using System.Text.RegularExpressions;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Application.Time;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Organizations;
using Inprem.Platform.Domain.PublicApi;

namespace Inprem.Platform.Application.PublicApi;

/// <summary>Checks a CAPTCHA answer with its provider (Cloudflare Turnstile, D30).</summary>
public interface ICaptchaVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default);
}

/// <summary>The verification email: a link to the app's page where the owner verifies and chooses a password.</summary>
public interface ISignupMail
{
    bool CanBuildLinks(string? appLinkBase);
    Task SendVerificationAsync(Signup signup, string organizationName, string? appLinkBase, CancellationToken ct = default);
}

public interface ISignupStore
{
    Task<Signup?> FindAsync(Guid signupId, CancellationToken ct = default);
    Task<TenantProfile?> FindProfileAsync(int tenantId, CancellationToken ct = default);
    Task<Subscription?> FindSubscriptionAsync(int tenantId, CancellationToken ct = default);
    Task<string?> TenantNameAsync(int tenantId, CancellationToken ct = default);
    Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record SignupInput(
    string? OrganizationName, string? Slug, string? TimeZone,
    string? OwnerFirstName, string? OwnerLastName, string? OwnerEmail, string? OwnerMobile,
    bool AcceptTerms, bool AcceptPrivacyPolicy, bool SmsOptIn,
    Guid? PlanPriceId, string? Mode, string? CaptchaToken, string? RemoteIp);

public enum SignupError
{
    Validation,
    Captcha,
    SlugTaken,
    EmailInUse,
    PlanNotAvailable,
    Failed,
}

public sealed record SignupResult(Signup? Signup, string? Token, DateTime? TokenExpiresUtc, SignupError? Error, IDictionary<string, string[]>? Errors = null, string? Message = null)
{
    public bool Succeeded => Error == null;

    public static SignupResult Fail(SignupError error, string message, IDictionary<string, string[]>? errors = null) => new(null, null, null, error, errors, message);
}

/// <summary>What the website may know about a signup: ids and statuses only (privacy rule, design 7.2).</summary>
public sealed record SignupView(Guid SignupId, string Status, SubscriptionStatus? SubscriptionStatus);

public enum ResendOutcome
{
    Sent,
    NotFound,
    AlreadyVerified,
    LimitReached,
}

/// <summary>
/// Website registration (Phase 3 step 3.3, journeys J1 and J2). One transaction creates the
/// organization (PendingVerification), the owner's account without a password, the Owner
/// membership, the profile, the subscription (Trialing for a trial, PendingActivation without
/// access for "subscribe") and the signup. The owner gets a verification email and chooses a
/// password in the app; until then nobody can sign in (D22).
/// </summary>
public sealed class SignupService
{
    private static readonly Regex E164 = new(@"^\+[1-9]\d{6,14}$", RegexOptions.Compiled);

    private readonly IOrganizationStore _organizations;
    private readonly IAccountGateway _accounts;
    private readonly ICheckoutStore _billing;
    private readonly ISignupStore _store;
    private readonly ISignupMail _mail;
    private readonly ICaptchaVerifier _captcha;
    private readonly PublicApiTokens _tokens;
    private readonly IClock _clock;
    private readonly Notifications.NotificationService _notifications;

    public SignupService(IOrganizationStore organizations, IAccountGateway accounts, ICheckoutStore billing, ISignupStore store, ISignupMail mail,
        ICaptchaVerifier captcha, PublicApiTokens tokens, IClock clock, Notifications.NotificationService notifications)
    {
        _notifications = notifications;
        _organizations = organizations;
        _accounts = accounts;
        _billing = billing;
        _store = store;
        _mail = mail;
        _captcha = captcha;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<SignupResult> CreateAsync(SignupInput input, string? appLinkBase, CancellationToken ct = default)
    {
        var errors = Validate(input, out var mode);
        if (errors.Count > 0) return SignupResult.Fail(SignupError.Validation, "One or more fields are invalid.", errors);
        if (!await _captcha.VerifyAsync(input.CaptchaToken, input.RemoteIp, ct).ConfigureAwait(false))
            return SignupResult.Fail(SignupError.Captcha, "The CAPTCHA check failed. Please try again.");
        if (!_mail.CanBuildLinks(appLinkBase)) return SignupResult.Fail(SignupError.Failed, "Registration is not available right now.");

        var found = await _billing.FindPriceAsync(input.PlanPriceId!.Value, ct).ConfigureAwait(false);
        if (found is not var (plan, price) || !plan.IsOffered || !plan.IsPublic || price.Status != PlanStatus.Active)
            return SignupResult.Fail(SignupError.PlanNotAvailable, "That plan is not available.");
        if (mode == SignupMode.Trial && plan.TrialDays < 1) return SignupResult.Fail(SignupError.PlanNotAvailable, "That plan has no free trial; choose subscribe.");

        var slug = input.Slug!.Trim().ToLowerInvariant();
        if (await _organizations.SlugTakenAsync(slug, null, ct).ConfigureAwait(false)) return SignupResult.Fail(SignupError.SlugTaken, $"\"{slug}\" is already used.");
        var email = input.OwnerEmail!.Trim();
        if (await _accounts.FindByEmailAsync(email, ct).ConfigureAwait(false) != null)
            return SignupResult.Fail(SignupError.EmailInUse, "An account with this email already exists. Sign in to the app, or use another email.");

        var now = _clock.UtcNow;
        var signupId = Guid.NewGuid();
        var organization = new NewOrganization(input.OrganizationName!.Trim(), slug, input.TimeZone!.Trim(), null, null,
            input.OwnerFirstName!.Trim(), input.OwnerLastName!.Trim(), email, string.IsNullOrWhiteSpace(input.OwnerMobile) ? null : input.OwnerMobile.Trim(),
            plan.Id, price.Id, BillingMode.PayPal, mode == SignupMode.Trial ? plan.TrialDays : 0);
        var subscription = mode == SignupMode.Trial
            ? Subscription.Start(0, plan, price, BillingMode.PayPal, plan.TrialDays, now)
            : Subscription.StartAwaitingPayment(0, plan, price, now);

        ProvisionedOrganization created;
        try
        {
            created = await _organizations.ProvisionAsync(new OrganizationProvisioning(organization, null, subscription, $"signup:{signupId}", now,
                new SignupProvisioning(signupId, mode, input.SmsOptIn && organization.OwnerMobile != null)), ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Rolled back: nothing exists. A race on the slug or the email ends here too.
            if (await _organizations.SlugTakenAsync(slug, null, ct).ConfigureAwait(false)) return SignupResult.Fail(SignupError.SlugTaken, $"\"{slug}\" is already used.");
            if (await _accounts.FindByEmailAsync(email, ct).ConfigureAwait(false) != null) return SignupResult.Fail(SignupError.EmailInUse, "An account with this email already exists.");
            return SignupResult.Fail(SignupError.Failed, $"The registration could not be completed ({e.GetType().Name}). Please try again.");
        }

        var signup = (await _store.FindAsync(signupId, ct).ConfigureAwait(false))!;
        if (input.SmsOptIn && organization.OwnerMobile != null)
        {
            // Phase 5: the box the owner ticked, with its exact wording. If the number is not a full
            // international one, or recording fails, the owner simply gets no texts (the safe side).
            try
            {
                await _notifications.RecordConsentAsync(organization.OwnerMobile, created.TenantId, created.OwnerUserId,
                    Inprem.Platform.Domain.Notifications.ConsentSources.WebsiteSignup, Notifications.ConsentWording.OwnerAlerts, $"signup:{signupId}", ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
            }
        }
        var (token, value) = await _tokens.IssueSignupAsync(signupId, created.TenantId, ct).ConfigureAwait(false);
        await SendVerificationAsync(signup, organization.Name, appLinkBase, ct).ConfigureAwait(false);
        return new SignupResult(signup, value, token.ExpiresUtc, null);
    }

    public async Task<SignupView?> GetAsync(Guid signupId, CancellationToken ct = default)
    {
        var signup = await _store.FindAsync(signupId, ct).ConfigureAwait(false);
        if (signup == null) return null;
        var subscription = await _store.FindSubscriptionAsync(signup.TenantId, ct).ConfigureAwait(false);
        var status = signup.Status == SignupStatus.PendingVerification ? "PendingVerification"
            : subscription?.Status is SubscriptionStatus.Active or SubscriptionStatus.Trialing ? "Active" : "Verified";
        return new SignupView(signup.Id, status, subscription?.Status);
    }

    /// <summary>A new verification email (at most 3 per hour per signup).</summary>
    public async Task<ResendOutcome> ResendAsync(Guid signupId, string? appLinkBase, CancellationToken ct = default)
    {
        var signup = await _store.FindAsync(signupId, ct).ConfigureAwait(false);
        if (signup == null) return ResendOutcome.NotFound;
        if (signup.Status == SignupStatus.Verified) return ResendOutcome.AlreadyVerified;
        if (!signup.TryCountResend(_clock.UtcNow)) return ResendOutcome.LimitReached;
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        await SendVerificationAsync(signup, await _store.TenantNameAsync(signup.TenantId, ct).ConfigureAwait(false) ?? "your organization", appLinkBase, ct).ConfigureAwait(false);
        return ResendOutcome.Sent;
    }

    /// <summary>The owner verified their email and chose a password (the app's verification page): the organization becomes Active.</summary>
    public async Task<bool> MarkVerifiedAsync(Guid signupId, CancellationToken ct = default)
    {
        var signup = await _store.FindAsync(signupId, ct).ConfigureAwait(false);
        if (signup == null || !signup.MarkVerified(_clock.UtcNow)) return false;
        (await _store.FindProfileAsync(signup.TenantId, ct).ConfigureAwait(false))?.Verified(_clock.UtcNow);
        await _store.AddAuditAsync(AuditEntry.Record($"signup:{signup.Id}", "signup.verified", signup.TenantId.ToString(), "The owner verified their email and chose a password.", _clock.UtcNow, signup.TenantId), ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public Task<Signup?> FindAsync(Guid signupId, CancellationToken ct = default) => _store.FindAsync(signupId, ct);

    private async Task SendVerificationAsync(Signup signup, string organizationName, string? appLinkBase, CancellationToken ct)
    {
        try
        {
            await _mail.SendVerificationAsync(signup, organizationName, appLinkBase, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The signup exists; the website offers "resend". The failure is audited.
            await _store.AddAuditAsync(AuditEntry.Record($"signup:{signup.Id}", "signup.verification-email-failed", signup.TenantId.ToString(), e.GetType().Name, _clock.UtcNow, signup.TenantId), ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Field errors keyed by the JSON property path (design 7.7).</summary>
    private static Dictionary<string, string[]> Validate(SignupInput input, out SignupMode mode)
    {
        var errors = new Dictionary<string, string[]>();
        void Add(string field, string message) => errors[field] = new[] { message };

        var name = input.OrganizationName?.Trim() ?? "";
        if (name.Length is < 2 or > 200) Add("organizationName", "Give the organization's name (2 to 200 characters).");
        var slugProblem = TenantProfile.SlugProblem(input.Slug?.Trim().ToLowerInvariant());
        if (slugProblem != null) Add("slug", slugProblem);
        if (string.IsNullOrWhiteSpace(input.TimeZone) || TimeZoneResolver.TryFind(input.TimeZone.Trim()) == null) Add("timeZone", "Choose one of the time zones from GET /timezones.");
        if (string.IsNullOrWhiteSpace(input.OwnerFirstName) || input.OwnerFirstName.Trim().Length > 100) Add("owner.firstName", "Give the owner's first name (up to 100 characters).");
        if (string.IsNullOrWhiteSpace(input.OwnerLastName) || input.OwnerLastName.Trim().Length > 100) Add("owner.lastName", "Give the owner's last name (up to 100 characters).");
        if (!IsEmail(input.OwnerEmail)) Add("owner.email", "Give a valid email address (up to 256 characters).");
        if (!string.IsNullOrWhiteSpace(input.OwnerMobile) && !E164.IsMatch(input.OwnerMobile.Trim())) Add("owner.mobile", "Use the international format, e.g. +15551234567.");
        if (!input.AcceptTerms) Add("consent.acceptTerms", "The terms must be accepted.");
        if (!input.AcceptPrivacyPolicy) Add("consent.acceptPrivacyPolicy", "The privacy policy must be accepted.");
        if (input.PlanPriceId is null || input.PlanPriceId == Guid.Empty) Add("planPriceId", "Choose a plan price from GET /plans.");
        mode = SignupMode.Trial;
        if (input.Mode == "trial") mode = SignupMode.Trial;
        else if (input.Mode == "subscribe") mode = SignupMode.Subscribe;
        else Add("mode", "Use trial or subscribe.");
        if (string.IsNullOrWhiteSpace(input.CaptchaToken)) Add("captchaToken", "The CAPTCHA answer is required.");
        return errors;
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
