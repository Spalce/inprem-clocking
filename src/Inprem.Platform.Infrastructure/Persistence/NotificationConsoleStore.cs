using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Inprem.Platform.Infrastructure.Persistence;

/// <summary>The console's Notifications pages (Phase 5 step 5.3): reads, and the edited wordings.</summary>
public sealed class NotificationConsoleStore : INotificationConsoleStore
{
    private readonly PlatformDbContext _db;

    public NotificationConsoleStore(PlatformDbContext db) => _db = db;

    public async Task<(IReadOnlyList<NotificationRow> Items, int Total)> ListAsync(NotificationFilter filter, int skip, int take, CancellationToken ct = default)
    {
        var q = _db.Notifications.AsNoTracking().AsQueryable();
        if (filter.Channel is { } channel) q = q.Where(n => n.Channel == channel);
        if (filter.Status is { } status) q = q.Where(n => n.Status == status);
        if (filter.TenantId is { } tenantId) q = q.Where(n => n.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(filter.TemplateKey)) q = q.Where(n => n.TemplateKey == filter.TemplateKey);
        var total = await q.CountAsync(ct).ConfigureAwait(false);
        var rows = await (
            from n in q.OrderByDescending(n => n.CreatedUtc).Skip(skip).Take(take)
            join t in _db.Tenants.AsNoTracking() on n.TenantId equals t.Id into tenants
            from t in tenants.DefaultIfEmpty()
            select new { n, Organization = t == null ? null : t.Name }).ToListAsync(ct).ConfigureAwait(false);
        return (rows.Select(r => new NotificationRow(r.n.Id, r.n.CreatedUtc, r.n.TenantId, r.Organization, r.n.Channel,
            r.n.Channel == NotificationChannel.Sms ? PhoneNumber.Mask(r.n.Destination) : r.n.Destination, r.n.TemplateKey, r.n.Status,
            r.n.SkipReason ?? r.n.Error, r.n.Cost, r.n.SentUtc, r.n.Attempts)).ToList(), total);
    }

    public async Task<IReadOnlyList<OptOutRow>> OptOutsAsync(int take, CancellationToken ct = default) =>
        (await _db.ConsentRecords.AsNoTracking().Where(c => c.Action == ConsentAction.Revoke && c.Source == ConsentSources.SmsStop)
            .OrderByDescending(c => c.RecordedUtc).Take(take).ToListAsync(ct).ConfigureAwait(false))
        .Select(c => new OptOutRow(PhoneNumber.Mask(c.Phone), c.RecordedUtc, c.Source)).ToList();

    public async Task<IReadOnlyList<TenantTextsRow>> TextsByTenantAsync(DateTime sinceUtc, int defaultMonthlyLimit, CancellationToken ct = default)
    {
        var texts = await _db.Notifications.AsNoTracking()
            .Where(n => n.Channel == NotificationChannel.Sms && n.TenantId != null && n.CreatedUtc >= sinceUtc && n.Status != NotificationStatus.Skipped)
            .GroupBy(n => n.TenantId!.Value)
            .Select(g => new { TenantId = g.Key, Count = g.Count(), Cost = g.Sum(n => n.Cost ?? n.EstimatedCost) })
            .ToListAsync(ct).ConfigureAwait(false);
        var settings = await _db.TenantMessagingSettings.AsNoTracking().ToDictionaryAsync(s => s.TenantId, ct).ConfigureAwait(false);
        var ids = texts.Select(t => t.TenantId).Concat(settings.Keys).Distinct().ToList();
        var names = await _db.Tenants.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct).ConfigureAwait(false);
        return ids.Select(id =>
        {
            var used = texts.FirstOrDefault(t => t.TenantId == id);
            var s = settings.GetValueOrDefault(id);
            return new TenantTextsRow(id, names.GetValueOrDefault(id) ?? $"Organization {id}", used?.Count ?? 0, used?.Cost ?? 0m, s?.SmsEnabled ?? true, s?.MonthlySmsLimit ?? defaultMonthlyLimit);
        }).OrderByDescending(r => r.Texts).ThenBy(r => r.Organization).ToList();
    }

    public async Task<IReadOnlyList<NotificationTemplate>> TemplatesAsync(CancellationToken ct = default) =>
        await _db.NotificationTemplates.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

    public Task<NotificationTemplate?> FindTemplateForUpdateAsync(string key, NotificationChannel channel, CancellationToken ct = default) =>
        _db.NotificationTemplates.FirstOrDefaultAsync(t => t.Key == key && t.Channel == channel, ct);

    public async Task AddTemplateAsync(NotificationTemplate template, CancellationToken ct = default) => await _db.NotificationTemplates.AddAsync(template, ct).ConfigureAwait(false);

    public void RemoveTemplate(NotificationTemplate template) => _db.NotificationTemplates.Remove(template);
}
