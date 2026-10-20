using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Application.Time;
using Inprem.Platform.Domain.Notifications;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Notifications;

/// <summary>Phase 5 step 5.1: phone numbers, consent history, the outbox message's life and quiet hours.</summary>
public class NotificationDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 14, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("+1 202 555 0123", "+12025550123")]
    [InlineData("+1 (202) 555-0123", "+12025550123")]
    [InlineData("00233 20 123 4567", "+233201234567")]
    [InlineData("+44.20.7946.0958", "+442079460958")]
    [InlineData("+233201234567", "+233201234567")]
    public void Phone_Numbers_Become_E164(string input, string expected) => Assert.Equal(expected, PhoneNumber.ToE164(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("202 555 0123")] // no country code: which country?
    [InlineData("0201234567")]
    [InlineData("+0201234567")]
    [InlineData("+1202555")] // too short
    [InlineData("+1202555012345678")] // too long
    [InlineData("+1 202 555 01x3")]
    public void Numbers_That_Cannot_Be_Texted_Safely_Are_Refused(string? input) => Assert.Null(PhoneNumber.ToE164(input));

    [Fact]
    public void A_Masked_Number_Hides_The_Middle() => Assert.Equal("+1202•••0123", PhoneNumber.Mask("+12025550123"));

    // ------------------------------------------------------------------ consent

    private static ConsentRecord Grant(int? tenant, DateTime at) => ConsentRecord.Record("+12025550123", ConsentAction.Grant, tenant, "u1", ConsentSources.WebsiteSignup, "I agree to texts.", at, "test");
    private static ConsentRecord Revoke(int? tenant, DateTime at) => ConsentRecord.Record("+12025550123", ConsentAction.Revoke, tenant, null, ConsentSources.SmsStop, null, at, "twilio");
    private static ConsentRecord Start(DateTime at) => ConsentRecord.Record("+12025550123", ConsentAction.Resubscribe, null, null, ConsentSources.SmsStart, null, at, "twilio");

    [Fact]
    public void Consent_Is_Per_Organization()
    {
        var history = new[] { Grant(1, Now) };
        Assert.Equal(ConsentState.Given, ConsentRecord.StateFor(history, 1));
        Assert.Equal(ConsentState.NotGiven, ConsentRecord.StateFor(history, 2));
        Assert.Equal(ConsentState.NotGiven, ConsentRecord.StateFor(history, null));
        Assert.Equal(ConsentState.NotGiven, ConsentRecord.StateFor(Array.Empty<ConsentRecord>(), 1));
    }

    [Fact]
    public void Stop_Withdraws_Every_Organization_And_Start_Resumes_Only_What_Was_Agreed()
    {
        var history = new List<ConsentRecord> { Grant(1, Now), Revoke(null, Now.AddMinutes(1)) };
        Assert.Equal(ConsentState.Withdrawn, ConsentRecord.StateFor(history, 1));

        history.Add(Start(Now.AddMinutes(2)));
        Assert.Equal(ConsentState.Given, ConsentRecord.StateFor(history, 1));
        Assert.Equal(ConsentState.NotGiven, ConsentRecord.StateFor(history, 2)); // START grants nothing new

        history.Add(Revoke(null, Now.AddMinutes(3)));
        Assert.Equal(ConsentState.Withdrawn, ConsentRecord.StateFor(history, 1));
        history.Add(Grant(1, Now.AddMinutes(4))); // agreed again, e.g. on a form
        Assert.Equal(ConsentState.Given, ConsentRecord.StateFor(history, 1));
    }

    private static ConsentRecord Choice(int? tenant, DateTime at) => ConsentRecord.Record("+12025550123", ConsentAction.Revoke, tenant, "u1", ConsentSources.Console, null, at, "test");

    [Fact]
    public void A_Withdrawal_For_One_Organization_Leaves_The_Others()
    {
        var history = new[] { Grant(1, Now), Grant(2, Now), Choice(1, Now.AddMinutes(1)), Start(Now.AddMinutes(2)) };
        Assert.Equal(ConsentState.Withdrawn, ConsentRecord.StateFor(history, 1)); // START undoes a STOP, not a choice made for one organization
        Assert.Equal(ConsentState.Given, ConsentRecord.StateFor(history, 2));
    }

    [Fact]
    public void Switching_Off_Inprems_Texts_Leaves_The_Organizations_But_Stop_Ends_All()
    {
        var history = new List<ConsentRecord> { Grant(null, Now), Grant(1, Now), Choice(null, Now.AddMinutes(1)) };
        Assert.Equal(ConsentState.Withdrawn, ConsentRecord.StateFor(history, null));
        Assert.Equal(ConsentState.Given, ConsentRecord.StateFor(history, 1)); // Inprem's choice is not a STOP

        history.Add(Revoke(1, Now.AddMinutes(2))); // a STOP reply, whichever text it answered
        Assert.Equal(ConsentState.Withdrawn, ConsentRecord.StateFor(history, 1));
    }

    [Fact]
    public void A_Grant_Keeps_The_Wording_And_Needs_A_Proper_Number()
    {
        Assert.Throws<ArgumentException>(() => ConsentRecord.Record("+12025550123", ConsentAction.Grant, 1, null, ConsentSources.Console, null, Now, "x"));
        Assert.Throws<ArgumentException>(() => ConsentRecord.Record("202 555 0123", ConsentAction.Revoke, null, null, ConsentSources.SmsStop, null, Now, "x"));
        Assert.Equal("I agree to texts.", Grant(1, Now).Wording);
    }

    // ------------------------------------------------------------------ outbox message

    private static Notification Queued(NotificationChannel channel = NotificationChannel.Sms) =>
        Notification.Queue(1, RecipientType.OrganizationOwner, "u1", channel, channel == NotificationChannel.Sms ? "+12025550123" : "o@x.test",
            NotificationTemplates.TestMessage, "Hi", "Body", NotificationUrgency.Normal, "ev-1", 0.02m, Now, Now);

    [Fact]
    public void A_Text_Goes_Only_To_An_E164_Number() =>
        Assert.Throws<ArgumentException>(() => Notification.Queue(1, RecipientType.OrganizationOwner, "u1", NotificationChannel.Sms, "202 555 0123",
            "k", null, "Body", NotificationUrgency.Normal, null, 0.02m, Now, Now));

    [Fact]
    public void Temporary_Failures_Wait_And_Then_Give_Up()
    {
        var message = Queued();
        message.AttemptFailed("Twilio could not be reached.", temporary: true, TimeSpan.FromMinutes(2), maxAttempts: 3, Now);
        Assert.Equal((NotificationStatus.Queued, 1, Now.AddMinutes(2)), (message.Status, message.Attempts, message.NotBeforeUtc));
        message.AttemptFailed("again", temporary: true, TimeSpan.FromMinutes(4), maxAttempts: 3, Now);
        message.AttemptFailed("again", temporary: true, TimeSpan.FromMinutes(6), maxAttempts: 3, Now);
        Assert.Equal((NotificationStatus.Failed, 3), (message.Status, message.Attempts));

        var refused = Queued();
        refused.AttemptFailed("Wrong number.", temporary: false, TimeSpan.FromMinutes(2), maxAttempts: 3, Now);
        Assert.Equal(NotificationStatus.Failed, refused.Status); // a permanent refusal is not tried again
    }

    [Fact]
    public void Delivery_Reports_Never_Reopen_A_Finished_Message()
    {
        var message = Queued();
        message.Sent("SM1", Now);
        Assert.True(message.ApplyDeliveryReport(DeliveryState.Delivered, null, null, Now));
        Assert.False(message.ApplyDeliveryReport(DeliveryState.InProgress, null, null, Now)); // a late "sent"
        Assert.Equal(NotificationStatus.Delivered, message.Status);
        Assert.True(message.ApplyDeliveryReport(DeliveryState.Delivered, null, 0.0079m, Now)); // the price comes later
        Assert.Equal(0.0079m, message.Cost);

        var failed = Queued();
        failed.Sent("SM2", Now);
        failed.ApplyDeliveryReport(DeliveryState.Failed, "30003", null, Now);
        Assert.Equal(NotificationStatus.Failed, failed.Status);
        Assert.Contains("30003", failed.Error);
    }

    [Fact]
    public void Only_A_Queued_Message_Is_Sent_Or_Skipped()
    {
        var message = Queued();
        message.SkipQueued(SkipReasons.OptedOut, Now);
        Assert.Throws<InvalidOperationException>(() => message.Sent("SM1", Now));
        Assert.False(message.ApplyDeliveryReport(DeliveryState.Delivered, null, null, Now));
        Assert.Equal(NotificationStatus.Skipped, message.Status);
    }

    // ------------------------------------------------------------------ quiet hours

    private static readonly TimeOnly Nine = new(21, 0), Eight = new(8, 0);

    [Fact]
    public void Quiet_Hours_Follow_The_Organizations_Time_Zone()
    {
        var newYork = TimeZoneResolver.Resolve("America/New_York");
        var accra = TimeZoneResolver.Resolve("Africa/Accra");
        var at = new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc); // 23:00 in New York, 03:00 in Accra

        // New York: 23:00 → 08:00 the next morning local (12:00 UTC, daylight time).
        Assert.Equal(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc), QuietHours.NextAllowedUtc(at, newYork, Nine, Eight));
        // Accra: 03:00 → 08:00 the same morning local (UTC+0).
        Assert.Equal(new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), QuietHours.NextAllowedUtc(at, accra, Nine, Eight));
        // Midday is never quiet.
        var noon = new DateTime(2026, 10, 20, 16, 0, 0, DateTimeKind.Utc);
        Assert.Equal(noon, QuietHours.NextAllowedUtc(noon, newYork, Nine, Eight));
    }

    [Fact]
    public void Quiet_Hours_Survive_The_Clock_Change()
    {
        var newYork = TimeZoneResolver.Resolve("America/New_York");
        // 22:00 local on Saturday 7 March 2026 (EST, UTC-5); clocks go forward at 02:00 on the 8th.
        var at = new DateTime(2026, 3, 8, 3, 0, 0, DateTimeKind.Utc);
        // 08:00 EDT on the 8th is 12:00 UTC.
        Assert.Equal(new DateTime(2026, 3, 8, 12, 0, 0, DateTimeKind.Utc), QuietHours.NextAllowedUtc(at, newYork, Nine, Eight));
    }

    [Fact]
    public void The_Month_Starts_In_The_Organizations_Time_Zone()
    {
        var newYork = TimeZoneResolver.Resolve("America/New_York");
        // 1 November 2026, 02:00 UTC is still 31 October in New York.
        Assert.Equal(new DateTime(2026, 10, 1, 4, 0, 0, DateTimeKind.Utc), QuietHours.MonthStartUtc(new DateTime(2026, 11, 1, 2, 0, 0, DateTimeKind.Utc), newYork));
    }

    [Fact]
    public void Templates_Fill_Their_Placeholders_And_Emails_Are_Encoded()
    {
        Assert.Equal("Hi Ama, from Alpha. {unknown}", NotificationTemplates.Render("Hi {firstName}, from {organization}. {unknown}",
            new Dictionary<string, string> { ["firstName"] = "Ama", ["organization"] = "Alpha" }));
        Assert.Equal("<p>A &amp; B<br>line</p><p>next</p>", NotificationTemplates.ToHtml("A & B\nline\n\nnext"));
    }

    [Fact]
    public void Default_Settings_Are_Valid_And_Bad_Ones_Are_Named()
    {
        Assert.Empty(new NotificationOptions().Problems());
        var bad = new NotificationOptions { QuietHoursStart = "nine", DailySpendCapUsd = -1, ProviderTimeZoneId = "Mars/Base" };
        Assert.Equal(3, bad.Problems().Count);
    }
}
