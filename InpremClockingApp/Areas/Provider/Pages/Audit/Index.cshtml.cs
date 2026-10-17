using Inprem.Platform.Application.Audit;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Audit;

/// <summary>
/// Audit log (step 1.6): filters, numbered pages of 25 / 50 / 100 / 200 entries (developer's
/// request, 2026-10-17), CSV export of the filtered log. Owners only; read-only.
/// </summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class IndexModel : PageModel
{
    private readonly AuditLogService _audit;

    public IndexModel(AuditLogService audit) => _audit = audit;

    [BindProperty(SupportsGet = true)] public string? Actor { get; set; }
    [BindProperty(SupportsGet = true)] public int? TenantId { get; set; }
    /// <summary>
    /// The action filter, as "act" in the address: "action" is a route value MVC keeps for itself,
    /// so a link carrying it came out empty (Previous, Next and the CSV link with a filter).
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "act")] public string? Action { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    /// <summary>Entries per page: one of <see cref="PageSizes"/> (50 when anything else is asked).</summary>
    [BindProperty(SupportsGet = true, Name = "size")] public int PageSize { get; set; } = 50;

    public static readonly int[] PageSizes = { 25, 50, 100, 200 };

    public AuditPage Result { get; private set; } = new(Array.Empty<Inprem.Platform.Domain.Audit.AuditEntry>(), 0, 1, 50);
    public IReadOnlyList<string> Actions { get; private set; } = Array.Empty<string>();
    public IReadOnlyDictionary<int, string> Tenants { get; private set; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<string, string> Actors { get; private set; } = new Dictionary<string, string>();

    private AuditQuery Query(int page = 1) => new(Actor, TenantId, Action, From, To, page, PageSize);

    public async Task OnGetAsync()
    {
        if (!PageSizes.Contains(PageSize)) PageSize = 50;
        Result = await _audit.QueryAsync(Query(PageNumber), HttpContext.RequestAborted);
        Actions = await _audit.ActionsAsync(HttpContext.RequestAborted);
        Tenants = await _audit.TenantNamesAsync(HttpContext.RequestAborted);
        Actors = await _audit.ActorNamesAsync(HttpContext.RequestAborted);
    }

    /// <summary>The filtered log as a CSV download (at most 50,000 rows).</summary>
    public async Task<IActionResult> OnGetCsvAsync() =>
        File(await _audit.ExportCsvAsync(Query(), HttpContext.RequestAborted), "text/csv", $"inprem-audit-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");

    public Dictionary<string, string> Route(int page) => new()
    {
        ["Actor"] = Actor ?? "", ["TenantId"] = TenantId?.ToString() ?? "", ["act"] = Action ?? "",
        ["From"] = From?.ToString("yyyy-MM-dd") ?? "", ["To"] = To?.ToString("yyyy-MM-dd") ?? "", ["p"] = page.ToString(), ["size"] = PageSize.ToString(),
    };

    /// <summary>
    /// The page numbers to show: the first, the last, and two on each side of the current one;
    /// null marks a gap ("…"). E.g. page 7 of 20: 1 … 5 6 7 8 9 … 20.
    /// </summary>
    public static IReadOnlyList<int?> PageLinks(int current, int pages)
    {
        var shown = new SortedSet<int> { 1, pages };
        for (var p = current - 2; p <= current + 2; p++) if (p >= 1 && p <= pages) shown.Add(p);
        var links = new List<int?>();
        var previous = 0;
        foreach (var p in shown)
        {
            if (p - previous > 1) links.Add(null);
            links.Add(p);
            previous = p;
        }
        return links;
    }
}
