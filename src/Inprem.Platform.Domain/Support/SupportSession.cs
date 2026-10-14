namespace Inprem.Platform.Domain.Support;

/// <summary>
/// A time-limited, audited support session (Phase 1 step 1.5): a provider user works in one
/// organization's App site as its admin, after a single-use hand-off link (60 seconds).
/// </summary>
public sealed class SupportSession
{
    public static readonly TimeSpan HandoffLifetime = TimeSpan.FromSeconds(60);
    public const int DefaultMinutes = 60;
    public static readonly int[] AllowedMinutes = { 15, 30, 60, 120, 240 };

    private SupportSession()
    {
        ProviderUserId = "";
        ProviderEmail = "";
        Reason = "";
        HandoffTokenHash = "";
    }

    public Guid Id { get; private set; }
    public int TenantId { get; private set; }

    /// <summary>The provider user's account (AspNetUsers.Id) and email, for the history.</summary>
    public string ProviderUserId { get; private set; }
    public string ProviderEmail { get; private set; }
    public string Reason { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? EndedUtc { get; private set; }

    /// <summary>Who or what ended it: "provider:{id}", "expired", ...</summary>
    public string? EndedBy { get; private set; }

    public string HandoffTokenHash { get; private set; }
    public DateTime HandoffExpiresUtc { get; private set; }
    public DateTime? HandoffUsedUtc { get; private set; }

    public static SupportSession Start(int tenantId, string providerUserId, string providerEmail, string reason, int minutes, string handoffTokenHash, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw new ArgumentException("Give a reason (up to 500 characters).", nameof(reason));
        if (!AllowedMinutes.Contains(minutes)) throw new ArgumentOutOfRangeException(nameof(minutes), $"Choose {string.Join(", ", AllowedMinutes)} minutes.");
        if (string.IsNullOrWhiteSpace(handoffTokenHash)) throw new ArgumentException("A hand-off token is needed.", nameof(handoffTokenHash));
        return new SupportSession
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ProviderUserId = providerUserId, ProviderEmail = providerEmail, Reason = reason.Trim(),
            StartedUtc = nowUtc, ExpiresUtc = nowUtc.AddMinutes(minutes), HandoffTokenHash = handoffTokenHash, HandoffExpiresUtc = nowUtc + HandoffLifetime,
        };
    }

    public bool IsActive(DateTime nowUtc) => EndedUtc == null && nowUtc < ExpiresUtc;

    /// <summary>The hand-off link works once, within 60 seconds, while the session is active.</summary>
    public bool CanRedeem(DateTime nowUtc) => HandoffUsedUtc == null && nowUtc < HandoffExpiresUtc && IsActive(nowUtc);

    public void Redeem(DateTime nowUtc)
    {
        if (!CanRedeem(nowUtc)) throw new InvalidOperationException("The hand-off link is used, expired, or the session is over.");
        HandoffUsedUtc = nowUtc;
    }

    public void End(string endedBy, DateTime nowUtc)
    {
        if (EndedUtc != null) return;
        EndedUtc = nowUtc < ExpiresUtc ? nowUtc : ExpiresUtc;
        EndedBy = endedBy;
    }
}
