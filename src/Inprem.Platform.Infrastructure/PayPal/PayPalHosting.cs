using Inprem.Platform.Application.Billing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Inprem.Platform.Infrastructure.PayPal;

public static class PayPalHosting
{
    /// <summary>
    /// Registers PayPal (Phase 2 step 2.2): options validated at startup, one named HttpClient with
    /// logging, a per-attempt timeout and retries of transient failures (5xx, 408, 429, network).
    /// Creates send a PayPal-Request-Id, so a retried create is not made twice.
    /// </summary>
    public static IServiceCollection AddPayPal(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayPalOptions>().Bind(configuration.GetSection(PayPalOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<PayPalOptions>, PayPalOptionsValidator>();
        services.AddTransient<PayPalLoggingHandler>();
        services.TryAddTimeProvider();

        services.AddHttpClient(PayPalPaymentProvider.HttpClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
                client.BaseAddress = options.BaseAddress;
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline times each attempt and the whole call
            })
            // The framework's own HttpClient logging writes request headers at Trace level, the app's
            // PayPal credentials included (found in step 2.2): off for this client; PayPalLoggingHandler
            // logs each call without headers or bodies.
            .RemoveAllLoggers()
            .AddHttpMessageHandler<PayPalLoggingHandler>()
            .AddResilienceHandler("paypal", (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<PayPalOptions>>().Value;
                pipeline.AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds * 3));
                pipeline.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 3, UseJitter = true, Delay = TimeSpan.FromSeconds(1) });
                pipeline.AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds));
            });

        services.AddSingleton<PayPalAccessTokens>();
        services.AddSingleton<IPaymentProvider, PayPalPaymentProvider>();
        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider))) services.AddSingleton(TimeProvider.System);
    }
}
