using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Memberships;
using Inprem.Platform.Domain.Organizations;

namespace Inprem.Platform.Application.Organizations;

/// <summary>One row of the organizations list.</summary>
public sealed record OrganizationSummary(
    int TenantId, string Name, string Slug, OrganizationStatus Status, Guid? PlanId, string? PlanName,
    int People, DateTime? LastActivityUtc, DateTime CreatedUtc, IReadOnlyList<string> OwnerEmails);

public sealed record SubscriptionView(string? PlanName, string? PriceText, BillingMode Mode, SubscriptionStatus Status, DateTime? TrialEndsUtc, DateTime CreatedUtc);

public sealed record AdminView(
    string UserId, string Email, string Name, MembershipRole Role, MembershipStatus Status,
    DateTime? LastSignInUtc, bool TwoFactorEnabled, bool HasPassword);

public sealed record OrganizationDetail(
    OrganizationSummary Summary, string TimeZoneId, string? Address, string? ContactInfo,
    string? StatusReason, DateTime? StatusChangedUtc, int Staff, int Volunteers,
    SubscriptionView? Subscription, IReadOnlyList<AdminView> Admins, IReadOnlyList<TenantNote> Notes, IReadOnlyList<AuditEntry> Audit);

/// <summary>List search and filters (step 1.2).</summary>
public sealed record OrganizationQuery(
    string? Search = null, OrganizationStatus? Status = null, Guid? PlanId = null,
    DateTime? CreatedFrom = null, DateTime? CreatedTo = null, int Page = 1, int PageSize = 25);

public sealed record OrganizationPage(IReadOnlyList<OrganizationSummary> Items, int Total, int Page, int PageSize)
{
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

/// <summary>What the wizard collects (Organization → Owner → Billing → Review).</summary>
public sealed record NewOrganization(
    string Name, string Slug, string TimeZoneId, string? Address, string? ContactInfo,
    string OwnerFirstName, string OwnerLastName, string OwnerEmail, string? OwnerMobile,
    Guid? PlanId, Guid? PlanPriceId, BillingMode Mode, int TrialDays);

public sealed record OrganizationEdit(string Name, string Slug, string TimeZoneId, string? Address, string? ContactInfo);

/// <summary>Everything the store creates in one transaction for a new organization.</summary>
public sealed record OrganizationProvisioning(
    NewOrganization Organization, string? ExistingOwnerUserId, Subscription Subscription, string Actor, DateTime NowUtc);

public sealed record ProvisionedOrganization(int TenantId, string OwnerUserId, bool OwnerIsNewAccount);

public sealed record OrganizationResult(bool Succeeded, int? TenantId, string? Error, string? Warning = null)
{
    public static OrganizationResult Ok(int tenantId, string? warning = null) => new(true, tenantId, null, warning);

    public static OrganizationResult Fail(string error) => new(false, null, error);
}

public sealed record SlugCheck(bool Available, string? Problem, string Suggestion);

public interface IOrganizationStore
{
    Task<IReadOnlyList<OrganizationSummary>> ListAsync(CancellationToken ct = default);
    Task<OrganizationDetail?> GetAsync(int tenantId, CancellationToken ct = default);
    Task<bool> SlugTakenAsync(string slug, int? exceptTenantId, CancellationToken ct = default);

    /// <summary>
    /// Creates, in ONE transaction: the organization (dbo.Tenants), the owner account (new: no
    /// password, Admin role) or uses the existing one, the owner membership (Active for a new
    /// account, Invited for an existing one), the profile, the subscription and the audit entry.
    /// Any failure leaves nothing behind.
    /// </summary>
    Task<ProvisionedOrganization> ProvisionAsync(OrganizationProvisioning provisioning, CancellationToken ct = default);

    Task<TenantProfile?> FindProfileAsync(int tenantId, CancellationToken ct = default);

    /// <summary>Saves the organization's own fields (dbo.Tenants) and profile together with the audit entry.</summary>
    Task UpdateAsync(int tenantId, OrganizationEdit edit, AuditEntry audit, CancellationToken ct = default);

    Task<IReadOnlyList<TenantMembership>> ListMembershipsAsync(int tenantId, CancellationToken ct = default);

    Task AddNoteAsync(TenantNote note, CancellationToken ct = default);
    Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default);

    /// <summary>Saves tracked profile / membership / note changes with the audit entries added.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Emails to organization admins (App site links), implemented with Identity in the web app.</summary>
public interface IOrganizationMail
{
    /// <summary>A new owner without a password: "set your password" link (24 hours) on the App site.</summary>
    Task SendOwnerWelcomeAsync(string userId, string organizationName, string? appLinkBase, CancellationToken ct = default);

    /// <summary>An existing account added as owner: accept the invitation after signing in.</summary>
    Task SendOwnerInvitationAsync(string userId, string organizationName, string? appLinkBase, CancellationToken ct = default);

    /// <summary>A password reset link for an admin who has a password.</summary>
    Task SendPasswordResetAsync(string userId, string organizationName, string? appLinkBase, CancellationToken ct = default);

    /// <summary>True when an App link base is known (configured, or given by the request).</summary>
    bool CanBuildLinks(string? appLinkBase);
}
