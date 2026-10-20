using Hangfire.States;
using Hangfire.Storage;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Notifications;

namespace InpremClockingApp.Notifications;

/// <summary>
/// Phase 5 step 5.3 (design Appendix C, "failed jobs"): when a background job ends in Failed (its
/// retries used up), Inprem's Owners get an urgent system-problem alert, at most one per job per hour.
/// Never the job's arguments (they can hold personal data): its name and the error type only.
/// </summary>
public sealed class JobFailureAlertFilter : IApplyStateFilter
{
    private readonly IServiceScopeFactory _scopes;

    public JobFailureAlertFilter(IServiceScopeFactory scopes) => _scopes = scopes;

    public void OnStateApplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
        if (context.NewState is not FailedState failed) return;
        var job = context.BackgroundJob?.Job;
        var name = job == null ? "unknown job" : $"{job.Type.Name}.{job.Method.Name}";
        try
        {
            using var scope = _scopes.CreateScope();
            var alerts = scope.ServiceProvider.GetRequiredService<BillingAlerts>();
            var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
            alerts.SystemProblemAsync($"job-failed:{name}", $"a background job failed: {name}",
                $"Job {context.BackgroundJob?.Id} ended in Failed after its retries ({failed.Exception?.GetType().Name ?? "no error type"}). See the console's Jobs page.", now)
                .GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // An alert never stops Hangfire from recording the failure.
        }
    }

    public void OnStateUnapplied(ApplyStateContext context, IWriteOnlyTransaction transaction)
    {
    }
}
