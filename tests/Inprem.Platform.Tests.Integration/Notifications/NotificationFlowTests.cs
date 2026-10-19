using System.Net;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Infrastructure.Twilio;
using Inprem.Platform.Tests.Integration.Provider;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using InpremClockingApp.Tests.Support.TestData;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Notifications;

/// <summary>The real app with Twilio's report settings (a test auth token) and a movable clock; texts are captured.</summary>
public sealed class NotificationFixture : IAsyncLifetime
{
    public const string AuthToken = "test-twilio-auth-token";
    public const string WebhookBase = "https://localhost";

    private TestDatabase _db = null!;
    private InpremAppFactory _app = null!;

    public TestClock Clock { get; } = new();
    public SeedCatalog Data { get; private set; } = null!;
    public IServiceProvider Services => _app.Services;
    public CapturingSmsSender Sms => _app.Sms;
    public CapturingEmailSender Emails => _app.Emails;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(GetType().Name);
        _app = new InpremAppFactory(_db.ConnectionString, new Dictionary<string, string?>
        {
            ["Twilio:AuthToken"] = AuthToken,
            ["Twilio:WebhookBaseUrl"] = WebhookBase,
        }, configureServices: services => services.AddSingleton<IClock>(Clock));
        _ = _app.Server;
        Data = await TestDataSeeder.SeedAsync(_app.Services);
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    public HttpClient CreateClient() => _app.CreateClient();

    public Infrastructure.Persistence.PlatformDbContext CreatePlatformDbContext() => _db.CreatePlatformDbContext();
}

/// <summary>
/// Phase 5 step 5.1 end to end, with the database: the outbox, the send job, consent and STOP
/// (P5-2), the organization's limits (P5-3), Twilio's delivery reports (P5-4, simulated) and forged
/// reports refused.
/// </summary>
public class NotificationFlowTests : IClassFixture<NotificationFixture>
{
    private readonly NotificationFixture _app;

    public NotificationFlowTests(NotificationFixture app) => _app = app;

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        using var scope = _app.Services.CreateScope();
        return await work(scope.ServiceProvider);
    }

    /// <summary>Gives the organization's owner a phone of their own (unique per test) and, optionally, consent.</summary>
    private async Task<string> OwnerPhoneAsync(TenantSeed tenant, bool consent)
    {
        var phone = "+1202555" + Random.Shared.Next(1000, 9999);
        await InScopeAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<AppUser>>();
            var owner = await users.FindByIdAsync(tenant.Owner!.UserId);
            await users.SetPhoneNumberAsync(owner!, phone);
            if (consent) await sp.GetRequiredService<NotificationService>().RecordConsentAsync(phone, tenant.Id, owner!.Id, ConsentSources.Console, "Inprem Clock-in may text me about my account. Msg & data rates may apply. Reply STOP to opt out.", "test");
            return 0;
        });
        return phone;
    }

    private Task<NotificationRaiseResult> RaiseAsync(TenantSeed tenant, string? key = null) =>
        InScopeAsync(sp => sp.GetRequiredService<NotificationService>().RaiseAsync(new NotificationEvent(NotificationTemplates.TestMessage, tenant.Id, Audience.OrganizationOwners, EventKey: key ?? Guid.NewGuid().ToString("N"))));

    private Task<SendReport> SendAsync() => InScopeAsync(sp => sp.GetRequiredService<NotificationSender>().SendDueAsync());

    private async Task<Notification> TextToAsync(string phone)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.Notifications.AsNoTracking().Where(n => n.Destination == phone).OrderByDescending(n => n.CreatedUtc).FirstAsync();
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> fields) => new(fields);

    private Task<HttpResponseMessage> PostSignedAsync(string path, Dictionary<string, string> fields, string? signature = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = Form(fields) };
        request.Headers.Add("X-Twilio-Signature", signature ?? TwilioSignature.Compute(NotificationFixture.AuthToken, NotificationFixture.WebhookBase + path, fields));
        return _app.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task An_Owner_Who_Agreed_Gets_The_Email_And_The_Text()
    {
        var phone = await OwnerPhoneAsync(_app.Data.Alpha, consent: true);

        var raised = await RaiseAsync(_app.Data.Alpha);
        await SendAsync();

        Assert.Equal(2, raised.Queued);
        var text = Assert.Single(_app.Sms.To(phone));
        Assert.Contains("test message", text.Body);
        Assert.Contains(_app.Emails.To(_app.Data.Alpha.Owner!.Email), e => e.Subject == "Test message from Inprem Clock-in");
        var row = await TextToAsync(phone);
        Assert.Equal((NotificationStatus.Sent, text.ExternalId), (row.Status, row.ExternalId));
    }

    [Fact]
    public async Task Without_Consent_The_Text_Is_Skipped_With_Its_Reason()
    {
        var phone = await OwnerPhoneAsync(_app.Data.Beta, consent: false);
        await RaiseAsync(_app.Data.Beta);
        await SendAsync();
        Assert.Empty(_app.Sms.To(phone));
        var row = await TextToAsync(phone);
        Assert.Equal((NotificationStatus.Skipped, SkipReasons.NoConsent), (row.Status, row.SkipReason));
    }

    [Fact]
    public async Task Stop_Ends_Every_Text_At_Once_And_Start_Brings_Them_Back()
    {
        var phone = await OwnerPhoneAsync(_app.Data.Alpha, consent: true);
        await RaiseAsync(_app.Data.Alpha); // queued before the STOP

        var stop = await PostSignedAsync("/webhooks/twilio/inbound", new() { ["From"] = phone, ["To"] = "+18005550000", ["Body"] = "STOP", ["MessageSid"] = "SMin1" });
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.Contains("<Response>", await stop.Content.ReadAsStringAsync());

        await SendAsync();
        Assert.Empty(_app.Sms.To(phone)); // P5-2: nothing to a number that replied STOP
        Assert.Equal(SkipReasons.OptedOut, (await TextToAsync(phone)).SkipReason);
        await RaiseAsync(_app.Data.Alpha);
        Assert.Equal(SkipReasons.OptedOut, (await TextToAsync(phone)).SkipReason);

        _app.Clock.Advance(TimeSpan.FromSeconds(1));
        await PostSignedAsync("/webhooks/twilio/inbound", new() { ["From"] = phone, ["To"] = "+18005550000", ["Body"] = "start" });
        _app.Clock.Advance(TimeSpan.FromSeconds(1));
        await RaiseAsync(_app.Data.Alpha);
        await SendAsync();
        Assert.Single(_app.Sms.To(phone));

        await using var db = _app.CreatePlatformDbContext();
        Assert.Contains(await db.AuditEntries.AsNoTracking().Where(a => a.Action == "notifications.opted-out").ToListAsync(), a => a.Target == PhoneNumber.Mask(phone));
    }

    [Fact]
    public async Task Delivery_Reports_Record_The_Status_And_The_Price()
    {
        var phone = await OwnerPhoneAsync(_app.Data.Alpha, consent: true);
        await RaiseAsync(_app.Data.Alpha);
        await SendAsync();
        var sid = Assert.Single(_app.Sms.To(phone)).ExternalId;

        Assert.Equal(HttpStatusCode.NoContent, (await PostSignedAsync("/webhooks/twilio/status", new() { ["MessageSid"] = sid, ["MessageStatus"] = "sent" })).StatusCode);
        Assert.Equal(NotificationStatus.Sent, (await TextToAsync(phone)).Status);
        await PostSignedAsync("/webhooks/twilio/status", new() { ["MessageSid"] = sid, ["MessageStatus"] = "delivered", ["Price"] = "-0.00790", ["PriceUnit"] = "USD" });
        await PostSignedAsync("/webhooks/twilio/status", new() { ["MessageSid"] = sid, ["MessageStatus"] = "sent" }); // late, out of order

        var row = await TextToAsync(phone);
        Assert.Equal((NotificationStatus.Delivered, 0.0079m), (row.Status, row.Cost));
        Assert.NotNull(row.FinishedUtc);
    }

    [Fact]
    public async Task Forged_Reports_Are_Refused_And_Change_Nothing()
    {
        var phone = await OwnerPhoneAsync(_app.Data.Alpha, consent: true);
        await RaiseAsync(_app.Data.Alpha);
        await SendAsync();
        var sid = Assert.Single(_app.Sms.To(phone)).ExternalId;
        var stop = new Dictionary<string, string> { ["From"] = phone, ["Body"] = "STOP" };

        var unsigned = await _app.CreateClient().PostAsync("/webhooks/twilio/inbound", Form(stop));
        var wrong = await PostSignedAsync("/webhooks/twilio/inbound", stop, signature: TwilioSignature.Compute("another-token", NotificationFixture.WebhookBase + "/webhooks/twilio/inbound", stop));
        var otherPath = await PostSignedAsync("/webhooks/twilio/status", new() { ["MessageSid"] = sid, ["MessageStatus"] = "failed" },
            signature: TwilioSignature.Compute(NotificationFixture.AuthToken, NotificationFixture.WebhookBase + "/webhooks/twilio/inbound", new Dictionary<string, string> { ["MessageSid"] = sid, ["MessageStatus"] = "failed" }));

        Assert.Equal(new[] { HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden }, new[] { unsigned.StatusCode, wrong.StatusCode, otherPath.StatusCode });
        await using var db = _app.CreatePlatformDbContext();
        Assert.False(await db.ConsentRecords.AnyAsync(c => c.Phone == phone && c.Action == ConsentAction.Revoke));
        Assert.Equal(NotificationStatus.Sent, (await TextToAsync(phone)).Status);
    }

    [Fact]
    public async Task The_Organizations_Switch_And_Monthly_Limit_Are_Kept()
    {
        var beta = _app.Data.Beta;
        var phone = await OwnerPhoneAsync(beta, consent: true);
        await using (var db = _app.CreatePlatformDbContext())
        {
            db.TenantMessagingSettings.Add(TenantMessagingSettings.Create(beta.Id, smsEnabled: true, monthlySmsLimit: 1, DateTime.UtcNow, "test"));
            await db.SaveChangesAsync();
        }
        try
        {
            await RaiseAsync(beta);
            await RaiseAsync(beta);
            Assert.Equal(SkipReasons.MonthlyLimit, (await TextToAsync(phone)).SkipReason);

            await using (var db = _app.CreatePlatformDbContext())
            {
                (await db.TenantMessagingSettings.SingleAsync(s => s.TenantId == beta.Id)).Change(smsEnabled: false, 100, DateTime.UtcNow, "test");
                await db.SaveChangesAsync();
            }
            await RaiseAsync(beta);
            Assert.Equal(SkipReasons.SmsOff, (await TextToAsync(phone)).SkipReason);
        }
        finally
        {
            await using var db = _app.CreatePlatformDbContext();
            await db.TenantMessagingSettings.Where(s => s.TenantId == beta.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task The_Same_Event_Reaches_Nobody_Twice()
    {
        await OwnerPhoneAsync(_app.Data.Alpha, consent: true);
        var key = "test-event:" + Guid.NewGuid().ToString("N");
        Assert.Equal(2, (await RaiseAsync(_app.Data.Alpha, key)).Queued);
        Assert.Equal(0, (await RaiseAsync(_app.Data.Alpha, key)).Queued);
        await using var db = _app.CreatePlatformDbContext();
        Assert.Equal(2, await db.Notifications.CountAsync(n => n.EventKey == key));
    }
}
