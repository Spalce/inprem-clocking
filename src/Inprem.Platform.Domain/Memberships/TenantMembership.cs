namespace Inprem.Platform.Domain.Memberships;

public enum MembershipRole
{
    Owner,
    Admin,
}

public enum MembershipStatus
{
    Invited,
    Active,
    Revoked,
}

/// <summary>
/// A person's membership of one organization (decision D2: one account per person, any number
/// of memberships). Only an Active membership lets the person work in that organization.
/// </summary>
public sealed class TenantMembership
{
    private TenantMembership()
    {
        UserId = "";
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; }
    public int TenantId { get; private set; }
    public MembershipRole Role { get; private set; }
    public MembershipStatus Status { get; private set; }
    public string? InvitedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    /// <summary>When this membership was last used to sign in (Provider Console, Admins tab).</summary>
    public DateTime? LastSignInUtc { get; private set; }

    public void RecordSignIn(DateTime nowUtc) => LastSignInUtc = nowUtc;

    public bool IsActive => Status == MembershipStatus.Active;

    /// <summary>An Active membership, e.g. for an existing admin account (backfill, first sign-in).</summary>
    public static TenantMembership CreateActive(string userId, int tenantId, MembershipRole role, DateTime nowUtc) =>
        Create(userId, tenantId, role, MembershipStatus.Active, invitedByUserId: null, nowUtc);

    /// <summary>An invitation that becomes Active when the person accepts it.</summary>
    public static TenantMembership CreateInvited(string userId, int tenantId, MembershipRole role, string invitedByUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(invitedByUserId)) throw new ArgumentException("An invitation needs the inviting user.", nameof(invitedByUserId));
        return Create(userId, tenantId, role, MembershipStatus.Invited, invitedByUserId, nowUtc);
    }

    private static TenantMembership Create(string userId, int tenantId, MembershipRole role, MembershipStatus status, string? invitedByUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A membership needs a user.", nameof(userId));
        if (tenantId <= 0) throw new ArgumentOutOfRangeException(nameof(tenantId), "A membership needs an organization.");
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));

        return new TenantMembership
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            Role = role,
            Status = status,
            InvitedByUserId = invitedByUserId,
            CreatedUtc = nowUtc,
        };
    }

    /// <summary>Accepting an invitation. Revoked memberships cannot be reactivated this way.</summary>
    public void Activate()
    {
        if (Status == MembershipStatus.Revoked) throw new InvalidOperationException("A revoked membership cannot be activated.");
        Status = MembershipStatus.Active;
    }

    public void Revoke() => Status = MembershipStatus.Revoked;

    /// <summary>
    /// Invites someone again whose membership was revoked (or declined). An Active membership
    /// cannot be turned back into an invitation.
    /// </summary>
    public void Reinvite(string invitedByUserId)
    {
        if (string.IsNullOrWhiteSpace(invitedByUserId)) throw new ArgumentException("An invitation needs the inviting user.", nameof(invitedByUserId));
        if (Status == MembershipStatus.Active) throw new InvalidOperationException("The person is already a member.");
        Status = MembershipStatus.Invited;
        InvitedByUserId = invitedByUserId;
    }

    /// <summary>Declining an invitation leaves the row as history (Revoked), never deletes it.</summary>
    public void Decline()
    {
        if (Status != MembershipStatus.Invited) throw new InvalidOperationException("Only an invitation can be declined.");
        Status = MembershipStatus.Revoked;
    }

    public void ChangeRole(MembershipRole role)
    {
        if (Status == MembershipStatus.Revoked) throw new InvalidOperationException("A revoked membership cannot change role.");
        Role = role;
    }
}
