using System.Net;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Tests.Integration.Provider;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Notifications;

/// <summary>
/// Phase 6 step 6.1 (C3-8, D32): the organization's Owner sets the staff alerts on the back office's
/// settings page; off by default with 21:00 and 60 minutes; other admins and support sessions cannot.
/// </summary>
public class StaffAlertSettingsTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public StaffAlertSettingsTests(ProviderFixture app) => _app = app;

    private async Task<HttpClient> SignedInAsync(string email)
    {
        var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);
        return client;
    }

    // The back-office pages also hold the top menu's Sign out form: every post picks the settings form by its address.
    [Fact]
    public async Task The_Owner_Switches_Staff_Alerts_On_With_Their_Times()
    {
        var gamma = _app.Data.Beta; // Beta's owner; reset at the end
        using var owner = await SignedInAsync(gamma.Owner!.Email);
        Assert.NotNull((await GetDocumentAsync(owner, "/BackOffice")).QuerySelector("[data-testid=settings-link]"));

        var page = await GetDocumentAsync(owner, "/Settings/StaffAlerts");
        Assert.Null(page.QuerySelector("[data-testid=enabled]")!.GetAttribute("checked")); // off by default
        Assert.Equal("21:00", page.QuerySelector("[data-testid=missed-clock-out]")!.GetAttribute("value"));
        Assert.Equal("60", page.QuerySelector("[data-testid=long-break]")!.GetAttribute("value"));
        // Step 6.3: staff texts in quiet hours are not sent, and the page says so.
        Assert.Contains("21:00 to 08:00", page.QuerySelector("[data-testid=quiet-hours]")!.TextContent);
        // Linked from the back-office menu, in the back-office layout.
        Assert.Contains(page.QuerySelectorAll("a"), a => a.TextContent.Trim() == "Manage Staff");

        var saved = await PostFormAsync(owner, "/Settings/StaffAlerts", "/Settings/StaffAlerts", ("Enabled", "true"), ("MissedClockOut", "20:30"), ("LongBreakMinutes", "45"), ("AdminSummary", "true"));
        Assert.True(Location(saved) == "/Settings/StaffAlerts", $"{(int)saved.StatusCode}: {new AngleSharp.Html.Parser.HtmlParser().ParseDocument(await BodyAsync(saved)).QuerySelector("[data-testid=error]")?.TextContent}");
        Assert.Contains("Staff alerts are on", (await GetDocumentAsync(owner, "/Settings/StaffAlerts")).QuerySelector("[data-testid=message]")!.TextContent);

        using (var scope = _app.Services.CreateScope())
            Assert.Equal(new StaffAlertChoices(true, new TimeOnly(20, 30), 45, true), await scope.ServiceProvider.GetRequiredService<StaffAlertSettings>().GetAsync(gamma.Id));
        await using (var db = _app.CreatePlatformDbContext())
            Assert.True(await db.AuditEntries.AnyAsync(a => a.TenantId == gamma.Id && a.Action == "organization.staff-alerts-changed"));

        var refused = await PostFormAsync(owner, "/Settings/StaffAlerts", "/Settings/StaffAlerts", ("Enabled", "true"), ("MissedClockOut", "20:30"), ("LongBreakMinutes", "5"));
        Assert.Contains("10 to 720 minutes", await BodyAsync(refused));
        var badTime = await PostFormAsync(owner, "/Settings/StaffAlerts", "/Settings/StaffAlerts", ("Enabled", "true"), ("MissedClockOut", "9pm"), ("LongBreakMinutes", "60"));
        Assert.Contains("hours and minutes", await BodyAsync(badTime));

        // Back to off for the other tests of this fixture.
        await PostFormAsync(owner, "/Settings/StaffAlerts", "/Settings/StaffAlerts", ("MissedClockOut", "21:00"), ("LongBreakMinutes", "60"));
    }

    [Fact]
    public async Task Other_Admins_Cannot_Open_It()
    {
        using var admin = await SignedInAsync(_app.Data.Alpha.Admin!.Email);
        Assert.Null((await GetDocumentAsync(admin, "/BackOffice")).QuerySelector("[data-testid=settings-link]"));
        var response = await admin.GetAsync("/Settings/StaffAlerts");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location?.ToString() ?? "");
    }
}
