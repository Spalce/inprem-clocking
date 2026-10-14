using Hangfire;
using Inprem.Platform.Application.Audit;
using Inprem.Platform.Application.Provider;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace InpremClockingApp.Areas.Provider.Pages;

/// <summary>Provider Console dashboard (step 1.6).</summary>
public class IndexModel : PageModel
{
    private readonly ProviderSecurityService _provider;
    private readonly AuditLogService _audit;
    private readonly JobStorage _jobs;

    public IndexModel(ProviderSecurityService provider, AuditLogService audit, JobStorage jobs)
    {
        _provider = provider;
        _audit = audit;
        _jobs = jobs;
    }

    public OwnerStatus Owners { get; private set; } = new(0, 0);
    public DashboardFigures Figures { get; private set; } = null!;

    /// <summary>Failed background jobs (null when the job storage cannot be read).</summary>
    public long? FailedJobs { get; private set; }
    public IReadOnlyDictionary<string, string> Actors { get; private set; } = new Dictionary<string, string>();

    public async Task OnGetAsync()
    {
        Owners = await _provider.GetOwnerStatusAsync(HttpContext.RequestAborted);
        Figures = await _audit.DashboardAsync(HttpContext.RequestAborted);
        Actors = await _audit.ActorNamesAsync(HttpContext.RequestAborted);
        try
        {
            FailedJobs = _jobs.GetMonitoringApi().FailedCount();
        }
        catch (Exception)
        {
            FailedJobs = null; // e.g. the Hangfire tables are not installed yet: the dashboard still works
        }
    }
}
