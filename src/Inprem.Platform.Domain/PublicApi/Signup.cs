namespace Inprem.Platform.Domain.PublicApi;

public enum SignupMode
{
    /// <summary>J1: free trial, no payment.</summary>
    Trial,

    /// <summary>J2: pay at once; the website continues with a checkout.</summary>
    Subscribe,
}

public enum SignupStatus
{
    PendingVerification,
    Verified,
}

/// <summary>
/// An organization registered on the website (Phase 3 step 3.3). The owner verifies their email
/// and chooses a password in the app; until then nobody can sign in to it (D22).
/// </summary>
public sealed class Signup
{
    public const int MaxResendsPerHour = 3;

    private Signup()
    {
        OwnerUserId = "";
    }

    public Guid Id { get; private set; }
    public int TenantId { get; private set; }
    public string OwnerUserId { get; private set; }
    public SignupMode Mode { get; private set; }
    public Guid PlanPriceId { get; private set; }
    public SignupStatus Status { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? VerifiedUtc { get; private set; }

    /// <summary>When the owner accepted the terms and the privacy policy (both required).</summary>
    public DateTime TermsAcceptedUtc { get; private set; }

    /// <summary>Asked for SMS alerts (only with a mobile number; Phase 5 records the consent properly).</summary>
    public bool SmsOptIn { get; private set; }

    public DateTime? ResendWindowStartUtc { get; private set; }
    public int ResendCount { get; private set; }

    public static Signup Create(Guid id, int tenantId, string ownerUserId, SignupMode mode, Guid planPriceId, bool smsOptIn, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if (string.IsNullOrWhiteSpace(ownerUserId)) throw new ArgumentException("The owner is required.", nameof(ownerUserId));
        return new Signup
        {
            Id = id, TenantId = tenantId, OwnerUserId = ownerUserId, Mode = mode, PlanPriceId = planPriceId,
            Status = SignupStatus.PendingVerification, CreatedUtc = nowUtc, TermsAcceptedUtc = nowUtc, SmsOptIn = smsOptIn,
        };
    }

    /// <summary>Counts a resent verification email; false when 3 were already sent in the last hour.</summary>
    public bool TryCountResend(DateTime nowUtc)
    {
        if (ResendWindowStartUtc == null || nowUtc - ResendWindowStartUtc >= TimeSpan.FromHours(1))
        {
            ResendWindowStartUtc = nowUtc;
            ResendCount = 0;
        }
        if (ResendCount >= MaxResendsPerHour) return false;
        ResendCount++;
        return true;
    }

    public bool MarkVerified(DateTime nowUtc)
    {
        if (Status == SignupStatus.Verified) return false;
        Status = SignupStatus.Verified;
        VerifiedUtc = nowUtc;
        return true;
    }
}
