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

    /// <summary>The user's membership in the organization, any status, tracked for changes.</summary>
    Task<TenantMembership?> FindAsync(string userId, int tenantId, CancellationToken cancellationToken = default);

    /// <summary>A membership by id, any status, tracked for changes.</summary>
    Task<TenantMembership?> FindByIdAsync(Guid membershipId, CancellationToken cancellationToken = default);

    /// <summary>The user's pending invitations, with organization names.</summary>
    Task<IReadOnlyList<ActiveMembershipInfo>> ListInvitedAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Every membership of the organization (any status), as account id and status.</summary>
    Task<IReadOnlyList<MemberStatus>> ListMembersAsync(int tenantId, CancellationToken cancellationToken = default);

    /// <summary>Saves changes made to memberships returned by FindAsync/FindByIdAsync.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record MemberStatus(string UserId, MembershipStatus Status);

/// <summary>Sending email (guardrails section 4). Messages are HTML.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default);
}

/// <summary>The clock (guardrails section 4: outside services behind interfaces).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
