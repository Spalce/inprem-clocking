using Inprem.Platform.Application.Tenancy;
using Inprem.Platform.Domain.Memberships;

namespace Inprem.Platform.Application.Memberships;

/// <summary>Persistence of memberships (implemented in Infrastructure).</summary>
public interface IMembershipStore
{
    /// <summary>The membership if it is Active, belongs to the user, and its organization exists.</summary>
    Task<ActiveMembershipInfo?> FindActiveAsync(Guid membershipId, string userId, CancellationToken cancellationToken = default);

    /// <summary>All of the user's Active memberships, ordered by organization name.</summary>
    Task<IReadOnlyList<ActiveMembershipInfo>> ListActiveAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the user has any membership (any status) in the organization.</summary>
    Task<bool> ExistsAsync(string userId, int tenantId, CancellationToken cancellationToken = default);

    Task AddAsync(TenantMembership membership, CancellationToken cancellationToken = default);
}

/// <summary>The clock (guardrails section 4: outside services behind interfaces).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
