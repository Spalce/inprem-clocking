using System.Globalization;
using System.Text;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Audit;

/// <summary>Audit log filters (Phase 1 step 1.6). Dates are whole UTC days, both included.</summary>
public sealed record AuditQuery(string? Actor = null, int? TenantId = null, string? Action = null, DateTime? From = null, DateTime? To = null, int Page = 1, int PageSize = 50);

public sealed record AuditPage(IReadOnlyList<AuditEntry> Items, int Total, int Page, int PageSize)
{
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

/// <summary>Numbers for the console dashboard.</summary>
public sealed record DashboardFigures(
    IReadOnlyDictionary<string, int> OrganizationsByStatus,
    IReadOnlyDictionary<SubscriptionStatus, int> SubscriptionsByStatus,
    IReadOnlyList<(int TenantId, string Name, DateTime TrialEndsUtc)> TrialsEndingSoon,
    IReadOnlyList<AuditEntry> RecentActivity);

public interface IAuditReader
{
    /// <summary>Entries matching the filters, newest first; <paramref name="take"/> null = all (export).</summary>
    Task<(IReadOnlyList<AuditEntry> Items, int Total)> QueryAsync(AuditQuery query, int skip, int? take, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ActionsAsync(CancellationToken ct = default);
    Task<IReadOnlyDictionary<int, string>> TenantNamesAsync(CancellationToken ct = default);
    Task<DashboardFigures> DashboardAsync(DateTime nowUtc, TimeSpan trialWindow, CancellationToken ct = default);
}

/// <summary>Reading the audit log: list, filters, CSV export, and the dashboard figures.</summary>
public sealed class AuditLogService
{
    public const int MaxExportRows = 50_000;
    public static readonly TimeSpan TrialWindow = TimeSpan.FromDays(14);

    private readonly IAuditReader _reader;
    private readonly IProviderStore _provider;
    private readonly Memberships.IClock _clock;

    public AuditLogService(IAuditReader reader, IProviderStore provider, Memberships.IClock clock)
    {
        _reader = reader;
        _provider = provider;
        _clock = clock;
    }

    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        var size = Math.Clamp(query.PageSize, 1, 200);
        var page = Math.Max(1, query.Page);
        var (items, total) = await _reader.QueryAsync(query, (page - 1) * size, size, ct).ConfigureAwait(false);
        return new AuditPage(items, total, page, size);
    }

    public Task<IReadOnlyList<string>> ActionsAsync(CancellationToken ct = default) => _reader.ActionsAsync(ct);

    public Task<IReadOnlyDictionary<int, string>> TenantNamesAsync(CancellationToken ct = default) => _reader.TenantNamesAsync(ct);

    public Task<DashboardFigures> DashboardAsync(CancellationToken ct = default) => _reader.DashboardAsync(_clock.UtcNow, TrialWindow, ct);

    /// <summary>"provider:{id}" actors as their email (other actors as they are).</summary>
    public async Task<IReadOnlyDictionary<string, string>> ActorNamesAsync(CancellationToken ct = default) =>
        (await _provider.ListProviderUsersAsync(ct).ConfigureAwait(false)).ToDictionary(u => $"provider:{u.UserId}", u => u.Email);

    /// <summary>The filtered log as CSV (UTF-8 with BOM, for spreadsheets), newest first, at most <see cref="MaxExportRows"/> rows.</summary>
    public async Task<byte[]> ExportCsvAsync(AuditQuery query, CancellationToken ct = default)
    {
        var (items, _) = await _reader.QueryAsync(query, 0, MaxExportRows, ct).ConfigureAwait(false);
        var actors = await ActorNamesAsync(ct).ConfigureAwait(false);
        var tenants = await TenantNamesAsync(ct).ConfigureAwait(false);

        var csv = new StringBuilder("When (UTC),Actor,Actor email,Action,Organization id,Organization,Target,Details\r\n");
        foreach (var e in items)
        {
            csv.AppendJoin(',',
                Cell(e.OccurredUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)), Cell(e.Actor), Cell(actors.GetValueOrDefault(e.Actor)), Cell(e.Action),
                Cell(e.TenantId?.ToString(CultureInfo.InvariantCulture)), Cell(e.TenantId is { } t ? tenants.GetValueOrDefault(t) : null), Cell(e.Target), Cell(e.Details));
            csv.Append("\r\n");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    /// <summary>
    /// One CSV cell: quoted when needed, and a value that a spreadsheet would run as a formula
    /// (starting with = + - @ tab or carriage return) is prefixed with an apostrophe.
    /// </summary>
    public static string Cell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}
