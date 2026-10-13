using Inprem.Platform.Domain.Memberships;

namespace Inprem.Platform.Application.Tenancy;

/// <summary>
/// The organization the current request works in (decision D4: from the signed-in membership,
/// re-checked every request; never from the URL). Empty outside a request, before sign-in, for
/// a person who has not chosen an organization yet, and for platform operators.
/// </summary>
public interface ITenantContext
{
    int? TenantId { get; }
    Guid? MembershipId { get; }
    MembershipRole? Role { get; }
    bool HasTenant => TenantId.HasValue;
}

/// <summary>Per-request holder, filled once by the tenant-context middleware after validation.</summary>
public sealed class TenantContext : ITenantContext
{
    public int? TenantId { get; private set; }
    public Guid? MembershipId { get; private set; }
    public MembershipRole? Role { get; private set; }

    /// <summary>
    /// Sets the context once per request. Setting the same membership again is allowed (the
    /// error-handling middleware re-runs the pipeline for the error page); a different one is not.
    /// </summary>
    public void Set(ActiveMembershipInfo membership)
    {
        if (MembershipId == membership.MembershipId && TenantId == membership.TenantId) return;
        if (TenantId.HasValue) throw new InvalidOperationException("The tenant context is already set for this request.");
        TenantId = membership.TenantId;
        MembershipId = membership.MembershipId;
        Role = membership.Role;
    }
}

/// <summary>Claim types the platform adds to the sign-in cookie.</summary>
public static class PlatformClaimTypes
{
    /// <summary>Same value the approved app already uses (Helpers/TenantClaimTypes).</summary>
    public const string TenantId = "tenant_id";

    public const string MembershipId = "membership_id";
}

/// <summary>An Active membership with the organization's name, as shown to the person.</summary>
public sealed record ActiveMembershipInfo(Guid MembershipId, string UserId, int TenantId, string TenantName, MembershipRole Role);
