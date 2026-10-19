using System.Security.Cryptography;
using System.Text;
using Inprem.Platform.Application.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Inprem.Platform.Infrastructure.Twilio;

/// <summary>
/// Twilio's request signature (X-Twilio-Signature): Base64 of HMAC-SHA1, keyed with the account's auth
/// token, over the full address Twilio called followed by every form field, sorted by name, as
/// name + value. A request without a valid signature did not come from Twilio.
/// </summary>
public static class TwilioSignature
{
    public static string Compute(string authToken, string url, IEnumerable<KeyValuePair<string, string>> form)
    {
        var text = new StringBuilder(url);
        foreach (var (key, value) in form.OrderBy(f => f.Key, StringComparer.Ordinal)) text.Append(key).Append(value);
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(authToken));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
    }

    public static bool IsValid(string? authToken, string url, IEnumerable<KeyValuePair<string, string>> form, string? signature)
    {
        if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(signature)) return false;
        var expected = Encoding.UTF8.GetBytes(Compute(authToken, url, form));
        var given = Encoding.UTF8.GetBytes(signature.Trim());
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }
}

public static class TwilioHosting
{
    /// <summary>
    /// Registers texts (Phase 5 step 5.1): the stand-in sender by default; Twilio's when Twilio:Mode =
    /// Twilio, with one named HttpClient (a per-attempt timeout, no retries: a retried send could text
    /// twice, so a failed send is tried again by the outbox, later).
    /// </summary>
    public static IServiceCollection AddTwilio(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<TwilioOptions>().Bind(configuration.GetSection(TwilioOptions.Section)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TwilioOptions>, TwilioOptionsValidator>();

        services.AddHttpClient(TwilioSmsSender.HttpClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<TwilioOptions>>().Value;
                client.BaseAddress = options.ApiBaseAddress;
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            // The framework's HttpClient logging writes headers at Trace level, the Basic credentials included: off.
            .RemoveAllLoggers()
            .AddResilienceHandler("twilio", (pipeline, context) =>
            {
                var options = context.ServiceProvider.GetRequiredService<IOptions<TwilioOptions>>().Value;
                pipeline.AddTimeout(TimeSpan.FromSeconds(options.TimeoutSeconds));
            });

        services.AddSingleton<StandInSmsSender>();
        services.AddSingleton<TwilioSmsSender>();
        services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<IOptions<TwilioOptions>>().Value.Mode == SmsMode.Twilio
            ? sp.GetRequiredService<TwilioSmsSender>()
            : sp.GetRequiredService<StandInSmsSender>());
        return services;
    }
}
