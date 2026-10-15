using System.Collections.Concurrent;
using Inprem.Platform.Application.PublicApi;
using Microsoft.Extensions.DependencyInjection;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>
/// The test host's billing-link queue: requests are kept, as Hangfire keeps them, and a test runs
/// them afterwards (<see cref="RunAllAsync"/>), so the request itself does the same work for every email.
/// </summary>
public sealed class InlineBillingAccessQueue : IBillingAccessQueue
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ConcurrentQueue<(string Email, string? WebsiteBase)> _pending = new();

    public InlineBillingAccessQueue(IServiceScopeFactory scopes) => _scopes = scopes;

    public int Enqueued { get; private set; }

    public void Enqueue(string email, string? websiteBase)
    {
        Enqueued++;
        _pending.Enqueue((email, websiteBase));
    }

    /// <summary>Runs the queued jobs, like the job server would.</summary>
    public async Task RunAllAsync()
    {
        while (_pending.TryDequeue(out var job))
        {
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<BillingAccessService>().SendLinksAsync(job.Email, job.WebsiteBase);
        }
    }
}
