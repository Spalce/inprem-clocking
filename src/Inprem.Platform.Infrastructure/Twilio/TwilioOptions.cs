using Microsoft.Extensions.Options;

namespace Inprem.Platform.Infrastructure.Twilio;

public enum SmsMode
{
    /// <summary>Nothing leaves the machine: each text is saved (StandInFolder) or only logged, and marked sent.</summary>
    StandIn,

    /// <summary>Texts go through Twilio's Messaging Service.</summary>
    Twilio,
}

/// <summary>
/// Section <c>Twilio</c> (Phase 5). The default is the stand-in, so nothing is sent until Twilio is set up
/// on purpose. Credentials come from user-secrets or the host's settings, never the repository; test
/// or trial credentials only until the live go-ahead (guardrails 5).
/// </summary>
public sealed class TwilioOptions
{
    public const string Section = "Twilio";

    public SmsMode Mode { get; set; } = SmsMode.StandIn;

    /// <summary>Starts with AC.</summary>
    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }

    /// <summary>The Messaging Service that holds Inprem's sender number (starts with MG).</summary>
    public string? MessagingServiceSid { get; set; }

    public Uri ApiBaseAddress { get; set; } = new("https://api.twilio.com/");

    /// <summary>The Verify service that sends login codes (starts with VA; step 5.2).</summary>
    public string? VerifyServiceSid { get; set; }

    public Uri VerifyBaseAddress { get; set; } = new("https://verify.twilio.com/");

    /// <summary>
    /// The site's public address as Twilio reaches it (e.g. https://app.inpremclockin.org): the status
    /// callback is sent there, and Twilio's signatures are checked against it.
    /// </summary>
    public Uri? WebhookBaseUrl { get; set; }

    /// <summary>Stand-in only: save each text as a .txt file here (like Email:DropFolder).</summary>
    public string? StandInFolder { get; set; }

    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class TwilioOptionsValidator : IValidateOptions<TwilioOptions>
{
    public ValidateOptionsResult Validate(string? name, TwilioOptions options)
    {
        var errors = new List<string>();
        if (options.TimeoutSeconds is < 1 or > 120) errors.Add("Twilio:TimeoutSeconds must be 1 to 120.");
        if (options.WebhookBaseUrl is { } url && (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps || url.AbsolutePath != "/"))
            errors.Add("Twilio:WebhookBaseUrl must be the site's https address without a path, e.g. https://app.inpremclockin.org.");
        if (options.Mode == SmsMode.Twilio)
        {
            if (options.AccountSid is not { Length: 34 } sid || !sid.StartsWith("AC", StringComparison.Ordinal)) errors.Add("Twilio:AccountSid must be the account's SID (AC…, 34 characters).");
            if (string.IsNullOrWhiteSpace(options.AuthToken)) errors.Add("Twilio:AuthToken is required with Twilio:Mode = Twilio.");
            if (options.MessagingServiceSid is not { Length: 34 } service || !service.StartsWith("MG", StringComparison.Ordinal)) errors.Add("Twilio:MessagingServiceSid must be the Messaging Service's SID (MG…, 34 characters).");
            if (options.VerifyServiceSid is not { Length: 34 } verify || !verify.StartsWith("VA", StringComparison.Ordinal)) errors.Add("Twilio:VerifyServiceSid must be the Verify service's SID (VA…, 34 characters).");
            if (!options.VerifyBaseAddress.IsAbsoluteUri || options.VerifyBaseAddress.Scheme != Uri.UriSchemeHttps) errors.Add("Twilio:VerifyBaseAddress must be an https address.");
            if (options.WebhookBaseUrl == null) errors.Add("Twilio:WebhookBaseUrl is required with Twilio:Mode = Twilio (delivery reports and STOP replies come there).");
            if (!options.ApiBaseAddress.IsAbsoluteUri || options.ApiBaseAddress.Scheme != Uri.UriSchemeHttps) errors.Add("Twilio:ApiBaseAddress must be an https address.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
