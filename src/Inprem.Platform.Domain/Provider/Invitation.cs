namespace Inprem.Platform.Domain.Provider;

public enum InvitationPurpose
{
    /// <summary>First Provider Owner (bootstrap) or any later provider user: set a password, then enrol two-factor.</summary>
    ProviderSetup,

    /// <summary>Level 3 recovery: the existing owner sets a new password (1 hour).</summary>
    ProviderRecovery,
}

/// <summary>
/// A one-time link sent by email. Only the SHA-256 hash of the link's token is stored; the token
/// itself exists only in the email.
/// </summary>
public sealed class Invitation
{
    private Invitation()
    {
        Email = "";
        UserId = "";
        TokenHash = "";
        CreatedBy = "";
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; }

    /// <summary>The account the invitation is for.</summary>
    public string UserId { get; private set; }
    public InvitationPurpose Purpose { get; private set; }
    public string TokenHash { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? ConsumedUtc { get; private set; }

    /// <summary>Withdrawn before use (e.g. by a recovery, which revokes pending invitations).</summary>
    public DateTime? RevokedUtc { get; private set; }
    public string CreatedBy { get; private set; }

    public static Invitation Create(string email, string userId, InvitationPurpose purpose, string tokenHash, TimeSpan lifetime, string createdBy, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("An invitation needs an email.", nameof(email));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("An invitation needs an account.", nameof(userId));
        if (string.IsNullOrWhiteSpace(tokenHash)) throw new ArgumentException("An invitation needs a token hash.", nameof(tokenHash));
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));

        return new Invitation
        {
            Id = Guid.NewGuid(), Email = email, UserId = userId, Purpose = purpose, TokenHash = tokenHash,
            CreatedUtc = nowUtc, ExpiresUtc = nowUtc + lifetime, CreatedBy = createdBy,
        };
    }

    public bool IsUsable(DateTime nowUtc) => ConsumedUtc == null && RevokedUtc == null && nowUtc < ExpiresUtc;

    public void Consume(DateTime nowUtc)
    {
        if (!IsUsable(nowUtc)) throw new InvalidOperationException("The invitation is used, revoked or expired.");
        ConsumedUtc = nowUtc;
    }

    public void Revoke(DateTime nowUtc)
    {
        if (ConsumedUtc == null && RevokedUtc == null) RevokedUtc = nowUtc;
    }
}
