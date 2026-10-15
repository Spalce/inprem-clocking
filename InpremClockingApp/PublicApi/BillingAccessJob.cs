using Hangfire;
using Inprem.Platform.Application.PublicApi;

namespace InpremClockingApp.PublicApi;

/// <summary>Queues the billing-link email for <see cref="BillingAccessJob"/> (Hangfire).</summary>
public sealed class HangfireBillingAccessQueue : IBillingAccessQueue
{
    private readonly IBackgroundJobClient _jobs;

    public HangfireBillingAccessQueue(IBackgroundJobClient jobs) => _jobs = jobs;

    public void Enqueue(string email, string? websiteBase) => _jobs.Enqueue<BillingAccessJob>(job => job.RunAsync(email, websiteBase, CancellationToken.None));
}

/// <summary>Sends the billing links (or nothing, for an email that is not an owner's), after the website got its answer.</summary>
public sealed class BillingAccessJob
{
    private readonly BillingAccessService _access;

    public BillingAccessJob(BillingAccessService access) => _access = access;

    [AutomaticRetry(Attempts = 3)]
    [JobDisplayName("Billing link email")]
    public Task RunAsync(string email, string? websiteBase, CancellationToken cancellationToken) => _access.SendLinksAsync(email, websiteBase, cancellationToken);
}
