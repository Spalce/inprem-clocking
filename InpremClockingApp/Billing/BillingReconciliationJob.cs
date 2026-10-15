using Hangfire;
using Inprem.Platform.Application.Billing;

namespace InpremClockingApp.Billing;

/// <summary>
/// The recurring reconciliation jobs (Phase 2 step 2.6), registered when the job server starts
/// (App site with Jobs:RunServer). Times are UTC.
/// </summary>
public sealed class BillingReconciliationJob
{
    public const string CheckoutsJobId = "billing-checkouts";
    public const string DailyJobId = "billing-daily";

    /// <summary>Every 5 minutes.</summary>
    public const string CheckoutsCron = "*/5 * * * *";

    /// <summary>Every day at 03:15 UTC (night in the US).</summary>
    public const string DailyCron = "15 3 * * *";

    private readonly BillingReconciliation _reconciliation;
    private readonly ILogger<BillingReconciliationJob> _log;

    public BillingReconciliationJob(BillingReconciliation reconciliation, ILogger<BillingReconciliationJob> log)
    {
        _reconciliation = reconciliation;
        _log = log;
    }

    [AutomaticRetry(Attempts = 0)] // the next run is never more than 5 minutes away
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    [JobDisplayName("Billing: open checkouts")]
    public async Task CheckoutsAsync(CancellationToken cancellationToken)
    {
        var report = await _reconciliation.CheckoutsAsync(cancellationToken);
        if (report.Checked > 0) _log.LogInformation("Billing checkouts: {Checked} checked, {Corrected} changed, {Failed} failed.", report.Checked, report.Corrected, report.Failed);
    }

    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    [JobDisplayName("Billing: daily PayPal check")]
    public async Task DailyAsync(CancellationToken cancellationToken)
    {
        var report = await _reconciliation.DailyAsync(cancellationToken);
        _log.LogInformation("Billing daily check: {Checked} checked, {Corrected} corrected, {Failed} failed.", report.Checked, report.Corrected, report.Failed);
    }

    /// <summary>Adds or updates both recurring jobs (safe on every start).</summary>
    public static void Register(IRecurringJobManager jobs)
    {
        var utc = new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc };
        jobs.AddOrUpdate<BillingReconciliationJob>(CheckoutsJobId, job => job.CheckoutsAsync(CancellationToken.None), CheckoutsCron, utc);
        jobs.AddOrUpdate<BillingReconciliationJob>(DailyJobId, job => job.DailyAsync(CancellationToken.None), DailyCron, utc);
    }
}
