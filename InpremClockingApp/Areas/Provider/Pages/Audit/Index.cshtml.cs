using Inprem.Platform.Application.Audit;
using InpremClockingApp.Provider;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages.Audit;

/// <summary>Audit log (step 1.6): filters, pages of 50, CSV export of the filtered log. Owners only; read-only.</summary>
[Authorize(Policy = ProviderPolicies.Owner)]
public class IndexModel : PageModel
{
    private readonly AuditLogService _audit;

    public IndexModel(AuditLogService audit) => _audit = audit;

    [BindProperty(SupportsGet = true)] public string? Actor { get; set; }
    [BindProperty(SupportsGet = true)] public int? TenantId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Action { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public AuditPage Result { get; private set; } = new(Array.Empty<Inprem.Platform.Domain.Audit.AuditEntry>(), 0, 1, 50);
    public IReadOnlyList<string> Actions { get; private set; } = Array.Empty<string>();
    public IReadOnlyDictionary<int, string> Tenants { get; private set; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<string, string> Actors { get; private set; } = new Dictionary<string, string>();

    private AuditQuery Query(int page = 1) => new(Actor, TenantId, Action, From, To, page);

    public async Task OnGetAsync()
    {
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
        ["Actor"] = Actor ?? "", ["TenantId"] = TenantId?.ToString() ?? "", ["Action"] = Action ?? "",
        ["From"] = From?.ToString("yyyy-MM-dd") ?? "", ["To"] = To?.ToString("yyyy-MM-dd") ?? "", ["p"] = page.ToString(),
    };
}
