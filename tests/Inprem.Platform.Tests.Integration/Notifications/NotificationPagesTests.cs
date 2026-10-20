using System.Net;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Tests.Integration.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Notifications;

/// <summary>
/// Phase 5 step 5.3: the console's Notifications pages (Owners): the delivery log, message wording
/// (edit on its own page, switch off, back to the default), opt-outs, texts and spend, an
/// organization's texts settings, and the test message to oneself. Every change is audited.
/// </summary>
public class NotificationPagesTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public NotificationPagesTests(ProviderFixture app) => _app = app;

    private async Task<(HttpClient Client, string Email)> OwnerAsync()
    {
        var email = $"np-{Guid.NewGuid():N}"[..13] + "@console.test";
        var key = await _app.CreateEnrolledOwnerAsync(email);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return (client, email);
    }

    private async Task<bool> AuditedAsync(string action)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.AuditEntries.AnyAsync(a => a.Action == action);
    }

    [Fact]
    public async Task Every_Tab_Opens_For_An_Owner()
    {
        var (client, _) = await OwnerAsync();
        using var _ = client;
        var log = await GetDocumentAsync(client, "/Provider/Notifications");
        Assert.NotNull(log.QuerySelector("[data-testid=log]"));
        Assert.NotNull(log.QuerySelector("[data-testid=stand-in]")); // the test host's texts are captured, like the stand-in
        Assert.Equal(NotificationTemplates.All.Sum(t => (t.EmailBody != null ? 1 : 0) + (t.SmsBody != null ? 1 : 0)),
            (await GetDocumentAsync(client, "/Provider/Notifications/Templates")).QuerySelectorAll("[data-testid=templates] tbody tr").Length);
        Assert.NotNull((await GetDocumentAsync(client, "/Provider/Notifications/OptOuts")).QuerySelector("[data-testid=optouts]"));
        Assert.Contains("USD", (await GetDocumentAsync(client, "/Provider/Notifications/Spend")).QuerySelector("[data-testid=today]")!.TextContent);
    }

    [Fact]
    public async Task An_Edited_Wording_Is_Used_Then_Switched_Off_Then_Back_To_The_Default()
    {
        var (client, email) = await OwnerAsync();
        using var _ = client;
        const string page = "/Provider/Notifications/EditTemplate?key=platform.test&channel=Email";

        var saved = await PostAsync(client, page, ("Subject", "Edited test {firstName}"), ("Body", "Edited body for {firstName}."), ("Enabled", "true"));
        Assert.Equal("/Provider/Notifications/Templates", Location(saved));
        Assert.True(await AuditedAsync("notification.template-changed"));

        Assert.Equal("/Provider/Notifications", Location(await PostAsync(client, "/Provider/Notifications/SendTest")));
        Assert.True(await AuditedAsync("notification.test-sent"));
        using (var scope = _app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<NotificationSender>().SendDueAsync();
        Assert.Contains(_app.Emails.To(email), e => e.Subject.StartsWith("Edited test") && e.HtmlBody.Contains("Edited body for"));

        // Switched off: the next test message is recorded as skipped, with the reason.
        await PostAsync(client, page, ("Subject", "Edited test {firstName}"), ("Body", "Edited body for {firstName}."));
        await PostAsync(client, "/Provider/Notifications/SendTest");
        await using (var db = _app.CreatePlatformDbContext())
            Assert.Equal(SkipReasons.TemplateOff, (await db.Notifications.AsNoTracking().Where(n => n.Destination == email).OrderByDescending(n => n.CreatedUtc).FirstAsync()).SkipReason);

        // Back to the default.
        var reset = await PostFormAsync(client, page, "handler=Reset");
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.True(await AuditedAsync("notification.template-reset"));
        var edit = await GetDocumentAsync(client, page);
        Assert.Equal("Test message from Inprem Clock-in", edit.QuerySelector("#Subject")!.GetAttribute("value"));
        Assert.Null(edit.QuerySelector("[data-testid=reset]"));
    }

    [Fact]
    public async Task A_Wording_Without_A_Subject_Is_Refused()
    {
        var (client, _) = await OwnerAsync();
        using var _ = client;
        var refused = await PostAsync(client, "/Provider/Notifications/EditTemplate?key=platform.test&channel=Email", ("Subject", ""), ("Body", "x"), ("Enabled", "true"));
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("subject", (await BodyAsync(refused)).ToLowerInvariant());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Provider/Notifications/EditTemplate?key=billing.invoice-issued&channel=Email")).StatusCode); // a text-only message
    }

    [Fact]
    public async Task An_Organizations_Texts_Can_Be_Switched_Off_And_Limited()
    {
        var (client, _) = await OwnerAsync();
        using var _ = client;
        var beta = _app.Data.Beta.Id;
        try
        {
            var saved = await PostAsync(client, $"/Provider/Tenants/Texts/{beta}", ("MonthlyLimit", "7"));
            Assert.EndsWith($"/Provider/Tenants/Details/{beta}", Location(saved));
            Assert.True(await AuditedAsync("organization.texts-changed"));
            await using var db = _app.CreatePlatformDbContext();
            var settings = await db.TenantMessagingSettings.AsNoTracking().SingleAsync(s => s.TenantId == beta);
            Assert.Equal((false, 7), (settings.SmsEnabled, settings.MonthlySmsLimit));

            var refused = await PostAsync(client, $"/Provider/Tenants/Texts/{beta}", ("SmsEnabled", "true"), ("MonthlyLimit", "-1"));
            Assert.Contains("0 to 100000", await BodyAsync(refused));
        }
        finally
        {
            await using var db = _app.CreatePlatformDbContext();
            await db.TenantMessagingSettings.Where(s => s.TenantId == beta).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task A_Stop_Shows_On_The_Opt_Outs_Tab_Masked()
    {
        var (client, _) = await OwnerAsync();
        using var _ = client;
        var phone = "+1202555" + Random.Shared.Next(1000, 9999);
        using (var scope = _app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<TwilioReports>().InboundAsync(phone, "STOP");
        var text = (await GetDocumentAsync(client, "/Provider/Notifications/OptOuts")).QuerySelector("[data-testid=optouts]")!.TextContent;
        Assert.Contains(PhoneNumber.Mask(phone), text);
        Assert.DoesNotContain(phone, text);
    }
}
