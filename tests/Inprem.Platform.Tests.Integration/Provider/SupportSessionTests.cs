using System.Net;
using Inprem.Platform.Domain.Provider;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>Phase 1 step 1.5 (P1-5): support sessions - hand-off, banner, audit per request, billing blocked, expiry, history.</summary>
public class SupportSessionTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public SupportSessionTests(ProviderFixture app) => _app = app;

    private sealed record Provider(HttpClient Console, string Email, string UserId, string Key);

    private async Task<Provider> ProviderAsync(ProviderRole role)
    {
        var email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..18] + "@support.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, role);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return new Provider(client, email, (await _app.FindUserAsync(email)).Id, key);
    }

    /// <summary>Starts a session on Alpha from the console; returns the hand-off path (single use, 60 s).</summary>
    private async Task<string> StartAsync(Provider provider, string reason = "Help with the staff report", string minutes = "60")
    {
        var page = $"/Provider/Tenants/StartSupport/{_app.Data.Alpha.Id}";
        var started = await PostAsync(provider.Console, page, ("Reason", reason), ("Minutes", minutes));
        if (Location(started).Contains("/Provider/Reauthenticate"))
        {
            // Starting needs a code from the last 5 minutes (these tests move the clock on).
            await PostAsync(provider.Console, Location(started), ("Code", Totp.Code(provider.Key)));
            started = await PostAsync(provider.Console, page, ("Reason", reason), ("Minutes", minutes));
        }
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        var url = new Uri(Location(started));
        Assert.Equal("/Account/Support/Handoff", url.AbsolutePath);
        return url.PathAndQuery;
    }

    /// <summary>Uses a hand-off link in a new browser (the App site).</summary>
    private async Task<(HttpClient App, HttpResponseMessage Response)> HandoffAsync(string link)
    {
        var app = _app.CreateClient();
        var page = await app.GetAsync(link);
        var html = await page.Content.ReadAsStringAsync();
        if (!html.Contains("<form", StringComparison.OrdinalIgnoreCase)) return (app, page); // used or expired: "not valid", nothing to press
        return (app, await PostAsync(app, link));
    }

    private async Task<List<string>> AuditAsync(string action)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.AuditEntries.Where(a => a.Action == action && a.TenantId == _app.Data.Alpha.Id).Select(a => a.Details ?? "").ToListAsync();
    }

    [Fact]
    public async Task Support_Session_Works_As_Admin_With_Banner_Audit_And_No_Billing()
    {
        var support = await ProviderAsync(ProviderRole.Support);
        var email = support.Email;
        Assert.Contains("Give a reason", await BodyAsync(await PostAsync(support.Console, $"/Provider/Tenants/StartSupport/{_app.Data.Alpha.Id}", ("Reason", " "), ("Minutes", "60"))));
        var link = await StartAsync(support);

        // The link only shows what will happen until the button is pressed.
        using var preview = _app.CreateClient();
        Assert.NotNull((await GetDocumentAsync(preview, link)).QuerySelector("[data-testid=request]"));

        var (app, response) = await HandoffAsync(link);
        Assert.Equal("/BackOffice", Location(response));

        // Admin of Alpha: Alpha's people, with the banner.
        var staff = await GetDocumentAsync(app, "/Staff");
        Assert.NotNull(staff.QuerySelector("[data-testid=support-banner]"));
        Assert.Contains(email, staff.QuerySelector("[data-testid=support-banner]")!.TextContent);
        Assert.Contains(_app.Data.Alpha.Staff[0].LastName, staff.Body!.TextContent);
        Assert.DoesNotContain(_app.Data.Beta.Staff[0].LastName, staff.Body!.TextContent);

        // Billing is refused.
        var billing = await app.GetAsync("/Platform/Tenants");
        Assert.Equal(HttpStatusCode.Forbidden, billing.StatusCode);

        // Single use.
        var (again, reused) = await HandoffAsync(link);
        Assert.NotNull(new AngleSharp.Html.Parser.HtmlParser().ParseDocument(await BodyAsync(reused)).QuerySelector("[data-testid=invalid]"));
        again.Dispose();

        // Audited: start, hand-off, each request, the blocked one marked.
        Assert.Contains(await AuditAsync("support.started"), d => d.Contains("Help with the staff report"));
        Assert.NotEmpty(await AuditAsync("support.handoff"));
        Assert.Contains(await AuditAsync("support.request"), d => d.StartsWith("GET /Staff"));
        Assert.Contains(await AuditAsync("support.request-blocked"), d => d.StartsWith("GET /Platform/Tenants"));

        // Ending from the banner signs out of the organization.
        var end = await PostFormAsync(app, "/Staff", a => a.EndsWith("/Account/Support/End")); // the banner's own button
        Assert.Equal("/Account/Support/Ended", Location(end));
        Assert.Contains("/Identity/Account/Login", Location(await app.GetAsync("/Staff")));
        Assert.NotEmpty(await AuditAsync("support.ended"));
        app.Dispose();
    }

    [Fact]
    public async Task Support_Handoff_Lives_60_Seconds()
    {
        var owner = await ProviderAsync(ProviderRole.Owner);
        var link = await StartAsync(owner);
        _app.Clock.Advance(TimeSpan.FromSeconds(61));
        var (app, response) = await HandoffAsync(link);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-testid=\"invalid\"", await BodyAsync(response));
        Assert.Contains("/Identity/Account/Login", Location(await app.GetAsync("/Staff")));
    }

    [Fact]
    public async Task Support_Session_Ends_At_Its_Time_From_The_Console_And_When_Disabled()
    {
        var owner = await ProviderAsync(ProviderRole.Owner);
        var console = owner.Console;

        // Time up.
        var (timed, _) = await HandoffAsync(await StartAsync(owner, minutes: "15"));
        Assert.Equal(HttpStatusCode.OK, (await timed.GetAsync("/Staff")).StatusCode);
        _app.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal("/Account/Support/Ended", Location(await timed.GetAsync("/Staff")));
        Assert.Contains("/Identity/Account/Login", Location(await timed.GetAsync("/Staff")));

        // Ended from the console (the organization's page lists it with "End now").
        var (ended, _) = await HandoffAsync(await StartAsync(owner));
        var detail = await GetDocumentAsync(console, $"/Provider/Tenants/Details/{_app.Data.Alpha.Id}");
        Assert.NotNull(detail.QuerySelector("[data-testid=active-support]"));
        await PostFormAsync(console, $"/Provider/Tenants/Details/{_app.Data.Alpha.Id}", a => a.Contains("handler=EndSupport"));
        Assert.Equal("/Account/Support/Ended", Location(await ended.GetAsync("/Staff")));

        // The provider user is disabled meanwhile.
        var helper = await ProviderAsync(ProviderRole.Support);
        var (disabled, _) = await HandoffAsync(await StartAsync(helper));
        Assert.Equal(HttpStatusCode.OK, (await disabled.GetAsync("/Staff")).StatusCode);
        var disable = $"/Provider/Users/Disable/{helper.UserId}";
        var asked = await PostAsync(console, disable, ("Reason", "Left"));
        if (Location(asked).Contains("/Provider/Reauthenticate")) await PostAsync(console, Location(asked), ("Code", Totp.Code(owner.Key)));
        await PostAsync(console, disable, ("Reason", "Left"));
        Assert.Equal("/Account/Support/Ended", Location(await disabled.GetAsync("/Staff")));
    }

    [Fact]
    public async Task Support_Billing_Users_Cannot_Start_And_The_Organization_Sees_Its_History()
    {
        var billing = await ProviderAsync(ProviderRole.Billing);
        Assert.EndsWith("/Provider/AccessDenied", Location(await billing.Console.GetAsync($"/Provider/Tenants/StartSupport/{_app.Data.Alpha.Id}")));

        var support = await ProviderAsync(ProviderRole.Support);
        var email = support.Email;
        await StartAsync(support, reason: "Checking the kiosk setup");

        using var orgAdmin = _app.CreateClient();
        await SignIn.AsAsync(orgAdmin, _app.Data.Alpha.Owner!.Email, _app.Data.Password);
        var history = await GetDocumentAsync(orgAdmin, "/Account/SupportSessions");
        var table = history.QuerySelector("[data-testid=support-sessions]")!.TextContent;
        Assert.Contains(email, table);
        Assert.Contains("Checking the kiosk setup", table);

        // Another organization does not see it.
        using var betaAdmin = _app.CreateClient();
        await SignIn.AsAsync(betaAdmin, _app.Data.Beta.Owner!.Email, _app.Data.Password);
        Assert.DoesNotContain(email, (await GetDocumentAsync(betaAdmin, "/Account/SupportSessions")).Body!.TextContent);
    }
}
