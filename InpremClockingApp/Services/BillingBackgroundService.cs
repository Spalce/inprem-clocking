namespace InpremClockingApp.Services;

// The app's first background job (see multi-tenancy.md Part 2, Phase 8/decision #10). Runs
// BillingService's daily sweep on a fixed interval, starting shortly after app startup. A
// hosted service is a singleton, but BillingService/ApplicationDbContext are scoped, so each
// tick creates its own DI scope rather than capturing one at construction time.
public class BillingBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingBackgroundService> _logger;

    public BillingBackgroundService(IServiceScopeFactory scopeFactory, ILogger<BillingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var billing = scope.ServiceProvider.GetRequiredService<BillingService>();
                var (renewed, pastDue, suspended) = await billing.RunDailySweepAsync().ConfigureAwait(false);
                _logger.LogInformation(
                    "Billing sweep complete: {Renewed} subscription(s) renewed, {PastDue} marked past due, {Suspended} suspended.",
                    renewed, pastDue, suspended);
            }
            catch (Exception ex)
            {
                // Never let one bad sweep kill the loop - there's no one else to run it, and
                // the next tick 24 hours from now should still be attempted.
                _logger.LogError(ex, "Billing sweep failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
