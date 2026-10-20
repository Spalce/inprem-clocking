using Hangfire;
using Inprem.Platform.Application.Notifications;

namespace InpremClockingApp.StaffAlerts;

/// <summary>
/// Runs the staff alerts check every 5 minutes (Phase 6 step 6.3). One UTC job for every time zone:
/// each run works out each organization's local time itself, so a clock change needs nothing special
/// and an organization's set time is met within 5 minutes. Never two runs at once; a failed run is not
/// retried (the next one is 5 minutes away, and alerts already raised are not raised again).
/// </summary>
public sealed class StaffAlertsJob
{
    public const string JobId = "staff-alerts";
    public const string Cron = "*/5 * * * *";

    private readonly StaffAlertsCheck _check;
    private readonly ILogger<StaffAlertsJob> _log;

    public StaffAlertsJob(StaffAlertsCheck check, ILogger<StaffAlertsJob> log)
    {
        _check = check;
        _log = log;
    }

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 240)]
    [JobDisplayName("Staff alerts: missed clock-outs, long breaks, admin summaries")]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var report = await _check.RunAsync(cancellationToken);
        if (report.Raised > 0)
        {
            _log.LogInformation("Staff alerts: {MissedClockOuts} missed clock-out(s), {LongBreaks} long break(s), {Summaries} admin summary(ies) queued.",
                report.MissedClockOuts, report.LongBreaks, report.Summaries);
        }
    }

    public static void Register(IRecurringJobManager jobs) =>
        jobs.AddOrUpdate<StaffAlertsJob>(JobId, job => job.RunAsync(CancellationToken.None), Cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
