using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Memberships;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Domain.Provider;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

public sealed class NotificationStore : INotificationStore
{
    private readonly PlatformDbContext _db;

    public NotificationStore(PlatformDbContext db) => _db = db;

    public async Task<bool> ExistsAsync(string eventKey, string recipientRef, NotificationChannel channel, CancellationToken ct = default) =>
        _db.Notifications.Local.Any(n => n.EventKey == eventKey && n.RecipientRef == recipientRef && n.Channel == channel)
        || await _db.Notifications.AsNoTracking().AnyAsync(n => n.EventKey == eventKey && n.RecipientRef == recipientRef && n.Channel == channel, ct).ConfigureAwait(false);

    public async Task AddAsync(Notification notification, CancellationToken ct = default) => await _db.Notifications.AddAsync(notification, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<Notification>> DueAsync(DateTime nowUtc, int take, CancellationToken ct = default) =>
        await _db.Notifications.Where(n => n.Status == NotificationStatus.Queued && n.NotBeforeUtc <= nowUtc)
            .OrderBy(n => n.NotBeforeUtc).ThenBy(n => n.CreatedUtc).Take(take).ToListAsync(ct).ConfigureAwait(false);

    public Task<Notification?> FindByExternalIdAsync(string externalId, CancellationToken ct = default) =>
        _db.Notifications.FirstOrDefaultAsync(n => n.ExternalId == externalId, ct);

    public Task<int> CountSmsAsync(int tenantId, DateTime sinceUtc, CancellationToken ct = default) =>
        _db.Notifications.CountAsync(n => n.TenantId == tenantId && n.Channel == NotificationChannel.Sms && n.CreatedUtc >= sinceUtc && n.Status != NotificationStatus.Skipped, ct);

    public async Task<decimal> SmsSpendAsync(DateTime sinceUtc, CancellationToken ct = default) =>
        await _db.Notifications.Where(n => n.Channel == NotificationChannel.Sms && n.CreatedUtc >= sinceUtc && n.Status != NotificationStatus.Skipped)
            .SumAsync(n => (decimal?)(n.Cost ?? n.EstimatedCost), ct).ConfigureAwait(false) ?? 0m;

    public async Task<IReadOnlyList<ConsentRecord>> ConsentHistoryAsync(string phoneE164, CancellationToken ct = default)
    {
        var saved = await _db.ConsentRecords.AsNoTracking().Where(c => c.Phone == phoneE164).ToListAsync(ct).ConfigureAwait(false);
        return saved.Concat(_db.ConsentRecords.Local.Where(c => c.Phone == phoneE164)).DistinctBy(c => c.Id).ToList();
    }

    public async Task AddConsentAsync(ConsentRecord record, CancellationToken ct = default) => await _db.ConsentRecords.AddAsync(record, ct).ConfigureAwait(false);

    public Task<NotificationTemplate?> FindTemplateAsync(string key, NotificationChannel channel, CancellationToken ct = default) =>
        _db.NotificationTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == key && t.Channel == channel, ct);

    public Task<TenantMessagingSettings?> FindSettingsAsync(int tenantId, CancellationToken ct = default) =>
        _db.TenantMessagingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public async Task AddAttemptAsync(string kind, string accountId, bool succeeded, DateTime nowUtc, CancellationToken ct = default) =>
        await _db.SecurityAttempts.AddAsync(new SecurityAttempt { Kind = kind, Succeeded = succeeded, Source = $"user:{accountId}", OccurredUtc = nowUtc }, ct).ConfigureAwait(false);

    public Task<int> CountAttemptsAsync(string kind, string accountId, DateTime sinceUtc, bool succeededOnly = false, bool failedOnly = false, CancellationToken ct = default)
    {
        var source = $"user:{accountId}";
        return _db.SecurityAttempts.CountAsync(a => a.Kind == kind && a.Source == source && a.OccurredUtc > sinceUtc
            && (!succeededOnly || a.Succeeded) && (!failedOnly || !a.Succeeded), ct);
    }

    public async Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) => await _db.AuditEntries.AddAsync(entry, ct).ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}

/// <summary>Who messages go to, from the memberships, the console users and the accounts' details.</summary>
public sealed class RecipientDirectory : IRecipientDirectory
{
    private readonly PlatformDbContext _db;

    public RecipientDirectory(PlatformDbContext db) => _db = db;

    public async Task<OrganizationContact?> OrganizationAsync(int tenantId, CancellationToken ct = default)
    {
        var tenant = await _db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, ct).ConfigureAwait(false);
        return tenant == null ? null : new OrganizationContact(tenant.Id, tenant.Name, tenant.TimeZoneId ?? Application.Time.TimeZoneResolver.DefaultTimeZoneId);
    }

    public async Task<IReadOnlyList<NotificationRecipient>> OrganizationMembersAsync(int tenantId, bool includeAdmins, CancellationToken ct = default)
    {
        var rows = await (
            from m in _db.TenantMemberships.AsNoTracking()
            join a in _db.Accounts.AsNoTracking() on m.UserId equals a.Id
            where m.TenantId == tenantId && m.Status == MembershipStatus.Active && (m.Role == MembershipRole.Owner || (includeAdmins && m.Role == MembershipRole.Admin))
            select new { m.Role, a.Id, a.Email, a.PhoneNumber, a.FirstName }).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new NotificationRecipient(r.Role == MembershipRole.Owner ? RecipientType.OrganizationOwner : RecipientType.OrganizationAdmin, r.Id, r.Email, r.PhoneNumber, r.FirstName)).ToList();
    }

    public async Task<IReadOnlyList<NotificationRecipient>> ProviderUsersAsync(IReadOnlyCollection<ProviderRole> roles, CancellationToken ct = default)
    {
        var rows = await (
            from u in _db.ProviderUsers.AsNoTracking()
            join a in _db.Accounts.AsNoTracking() on u.UserId equals a.Id
            where u.Status == ProviderUserStatus.Active
            select new { u.Role, a.Id, a.Email, a.PhoneNumber, a.FirstName }).ToListAsync(ct).ConfigureAwait(false);
        return rows.Where(r => roles.Count == 0 || roles.Contains(r.Role))
            .Select(r => new NotificationRecipient(RecipientType.ProviderUser, r.Id, r.Email, r.PhoneNumber, r.FirstName)).ToList();
    }
}
