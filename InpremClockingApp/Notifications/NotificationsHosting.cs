using Hangfire;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Infrastructure.Twilio;
using Microsoft.Extensions.Options;

namespace InpremClockingApp.Notifications;

public static class NotificationsHosting
{
    /// <summary>
    /// Messages (multi-tenancy Phase 5): the notification settings (section "Notifications", checked
    /// at startup), texts (section "Twilio": the stand-in unless Twilio:Mode = Twilio), the send job and
    /// Twilio's reports (/webhooks/twilio/status and /webhooks/twilio/inbound).
    /// </summary>
    public static WebApplicationBuilder AddNotifications(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<NotificationOptions>().Bind(builder.Configuration.GetSection(NotificationOptions.Section))
            .Validate(o => o.Problems().Count == 0, "The Notifications settings are not valid (see NotificationOptions.Problems).")
            .ValidateOnStart();
        // Links in messages (step 5.3): the same addresses the app's emails use.
        builder.Services.PostConfigure<NotificationOptions>(o =>
        {
            o.AppBaseUrl ??= builder.Configuration["Links:AppBaseUrl"];
            o.AdminBaseUrl ??= builder.Configuration["Links:AdminBaseUrl"];
        });
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<NotificationOptions>>().Value);
        builder.Services.AddTwilio(builder.Configuration);
        // Phase 6 step 6.2 (C3-8): the staff and volunteer consent box, recorded only when staff alerts are on.
        builder.Services.AddScoped<InpremClockingApp.StaffAlerts.StaffConsentRecorder>();
        builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => o.Filters.Add(new InpremClockingApp.StaffAlerts.KioskConsentPageFilter()));
        builder.Services.AddScoped<NotificationSendJob>();
        return builder;
    }
}

/// <summary>
/// Sends the outbox's due messages every minute (Phase 5 step 5.1). Never two runs at once; a failed
/// run is not retried by Hangfire (the next run is a minute away, and each message keeps its own attempts).
/// </summary>
public sealed class NotificationSendJob
{
    public const string JobId = "notifications-send";
    public const string Cron = "* * * * *";

    private readonly NotificationSender _sender;
    private readonly ILogger<NotificationSendJob> _log;

    public NotificationSendJob(NotificationSender sender, ILogger<NotificationSendJob> log)
    {
        _sender = sender;
        _log = log;
    }

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    [JobDisplayName("Messages: send")]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var report = await _sender.SendDueAsync(cancellationToken);
        if (report.Sent + report.Failed + report.Skipped + report.Retrying > 0)
        {
            _log.LogInformation("Messages: {Sent} sent, {Failed} failed, {Skipped} skipped, {Retrying} to try again.", report.Sent, report.Failed, report.Skipped, report.Retrying);
        }
    }

    public static void Register(IRecurringJobManager jobs) =>
        jobs.AddOrUpdate<NotificationSendJob>(JobId, job => job.RunAsync(CancellationToken.None), Cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
}
