using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

/// <summary>
/// What an organization may do right now, from its subscription (Phase 2 step 2.7) and whether
/// Inprem has suspended it from the Provider Console (C3-3; found missing in manual testing, T2-3).
/// </summary>
public sealed record AccessStatus(
    int TenantId,
    SubscriptionStatus? Status,
    BillingMode? Mode,
    bool FullAccess,
    int TrialDaysLeft,
    DateTime? TrialEndsUtc,
    DateTime? AccessUntilUtc,
    decimal OutstandingBalance,
    int? MaxPeople,
    bool HasPayPalSubscription = false,
    bool OrganizationSuspended = false)
{
    /// <summary>Save anything (C3-3). Reports and exports work either way.</summary>
    public bool CanWrite => FullAccess;

    /// <summary>Use the kiosk: clock in and out, breaks (C3-4).</summary>
    public bool CanUseKiosk => FullAccess;

    /// <summary>Text messages (Phase 5): not available yet.</summary>
    public bool CanSendSms => false;

    /// <summary>Add one more person (staff or volunteer) to <paramref name="currentPeople"/>, within the plan's limit.</summary>
    public bool CanAddPeople(int currentPeople) => FullAccess && (MaxPeople == null || currentPeople < MaxPeople);

    /// <summary>PayPal is retrying a failed payment (still full access).</summary>
    public bool PaymentProblem => Status == SubscriptionStatus.PastDue;

    /// <summary>Why the organization is read-only, in words for its people (null when it is not).</summary>
    public string? ReadOnlyReason => FullAccess ? null : OrganizationSuspended ? "Inprem has suspended this organization's account. Please contact Inprem." : Status switch
    {
        SubscriptionStatus.Trialing => $"The free trial ended on {TrialEndsUtc:d MMMM yyyy}.",
        SubscriptionStatus.Expired when !HasPayPalSubscription && TrialEndsUtc != null => $"The free trial ended on {TrialEndsUtc:d MMMM yyyy}.",
        SubscriptionStatus.Expired => "The subscription has ended.",
        SubscriptionStatus.Suspended => "The subscription is suspended because a payment could not be collected.",
        SubscriptionStatus.Canceled => $"The subscription was cancelled and ended on {AccessUntilUtc:d MMMM yyyy}.",
        SubscriptionStatus.Closed => "This organization's account is closed.",
        _ => "The subscription is not active.",
    };

    /// <summary>No subscription row (organizations from before the platform tables in a database without the M1f backfill): as before, full access.</summary>
    public static AccessStatus Unrestricted(int tenantId) => new(tenantId, null, null, true, 0, null, null, 0m, null);
}

public interface IEntitlementStore
{
    /// <summary>The organization's subscription and its plan's people limit (read-only).</summary>
    Task<(Subscription Subscription, int? PlanMaxPeople)?> FindAsync(int tenantId, CancellationToken ct = default);

    /// <summary>True when Inprem has suspended the organization from the Provider Console.</summary>
    Task<bool> IsSuspendedAsync(int tenantId, CancellationToken ct = default);
}

public interface IEntitlementService
{
    Task<AccessStatus> GetAsync(int tenantId, CancellationToken ct = default);
}

/// <summary>
/// Entitlements from the subscription (design 8, C3-3, C3-4). Read once per request (scoped):
/// the enforcement guard and the status banner share it. Trial expiry is evaluated here from
/// the trial end date, so access ends on time even before the daily job marks it Expired.
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private readonly IEntitlementStore _store;
    private readonly IClock _clock;
    private readonly Dictionary<int, AccessStatus> _cache = new();

    public EntitlementService(IEntitlementStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public async Task<AccessStatus> GetAsync(int tenantId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(tenantId, out var cached)) return cached;
        var found = await _store.FindAsync(tenantId, ct).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var suspended = await _store.IsSuspendedAsync(tenantId, ct).ConfigureAwait(false);
        var status = found is not var (s, planMax)
            ? AccessStatus.Unrestricted(tenantId)
            : new AccessStatus(tenantId, s.Status, s.Mode, s.HasFullAccess(now), s.TrialDaysLeft(now), s.TrialEndsUtc,
                s.Status == SubscriptionStatus.Canceled ? s.CurrentPeriodEndUtc : null, s.OutstandingBalance, s.MaxPeopleOverride ?? planMax, s.ExternalSubscriptionId != null);
        // Suspended by Inprem: read-only whatever the subscription says (reports and exports still work).
        if (suspended) status = status with { FullAccess = false, OrganizationSuspended = true };
        _cache[tenantId] = status;
        return status;
    }
}
