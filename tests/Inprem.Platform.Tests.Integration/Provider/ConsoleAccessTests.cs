using System.Net;
using AngleSharp.Html.Parser;
using Inprem.Platform.Domain.Provider;
using InpremClockingApp.Provider;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>
/// Phase 1 step 1.1: the console's role matrix (P1-2), navigation per role, page pattern (P1-1),
/// re-authentication and session limits (design 5.5).
/// </summary>
public class ConsoleAccessTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public ConsoleAccessTests(ProviderFixture app) => _app = app;

    /// <summary>Signed-in clients per role (each test class instance signs in afresh).</summary>
    private async Task<HttpClient> ProviderClientAsync(ProviderRole role)
    {
        var email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..20] + "@console.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, role);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return client;
    }

    /// <summary>Every console page that needs a sign-in, with the policy it requires beyond Support.</summary>
    private static readonly Dictionary<string, string> ConsolePages = ProviderNavigation.Items
        .Where(i => i.Page != null)
        .ToDictionary(i => i.Page!, i => i.Policy);

    /// <summary>Signed-in pages that are not in the menu and not GET-checked (they redirect by design).</summary>
    private static readonly string[] OtherSignedInPages = { "/EnrolTwoFactor", "/Reauthenticate" };

    /// <summary>
    /// Pages below the menu: who may open them (written out independently), and their address for
    /// a plan id. Plans: everyone views; Billing and Owners change.
    /// </summary>
    private static readonly Dictionary<string, (ProviderRole[] Roles, Func<Guid, string> Url)> SubPages = new()
    {
        ["/Plans/Details"] = (new[] { ProviderRole.Support, ProviderRole.Billing, ProviderRole.Owner }, id => $"/Provider/Plans/Details/{id}"),
        ["/Plans/New"] = (new[] { ProviderRole.Billing, ProviderRole.Owner }, _ => "/Provider/Plans/New"),
        ["/Plans/Edit"] = (new[] { ProviderRole.Billing, ProviderRole.Owner }, id => $"/Provider/Plans/Edit/{id}"),
        ["/Plans/NewPrice"] = (new[] { ProviderRole.Billing, ProviderRole.Owner }, id => $"/Provider/Plans/NewPrice/{id}"),
    };

    private async Task<Guid> PlanIdAsync()
    {
        using var scope = _app.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<Inprem.Platform.Application.Billing.PlanService>();
        var code = $"matrix-{Guid.NewGuid():N}"[..20];
        var result = await plans.CreateAsync(code, new("Matrix plan", null, null, null, 30, true), Inprem.Platform.Domain.Billing.BillingCycle.Monthly, 10m, "USD", "test");
        return result.PlanId!.Value;
    }

    /// <summary>
    /// The agreed permissions, written out independently of the app's own tables (so changing those
    /// tables fails this test): which menu items each role may open.
    /// </summary>
    private static readonly Dictionary<ProviderRole, string[]> Allowed = new()
    {
        [ProviderRole.Support] = new[] { "Dashboard", "Organizations", "Plans" },
        [ProviderRole.Billing] = new[] { "Dashboard", "Organizations", "Plans", "Billing", "Webhook events" },
        [ProviderRole.Owner] = new[] { "Dashboard", "Organizations", "Plans", "Billing", "Webhook events", "Notifications", "Jobs", "Audit log", "Provider users", "Settings" },
    };

    private static bool MayOpen(ProviderRole role, string page) =>
        Allowed[role].Contains(ProviderNavigation.Items.Single(i => i.Page == page).Title);

    private static string Url(string page) => "/Provider" + (page == "/Index" ? "" : page.Replace("/Index", ""));

    [Fact]
    public void Console_Every_Signed_In_Page_Is_In_The_Matrix()
    {
        // A page added later without a place in this matrix fails here.
        var pages = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Select(e => e.Metadata.GetMetadata<PageActionDescriptor>())
            .Where(d => d is { AreaName: "Provider" })
            .Where(d => !d!.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            .Select(d => d!.ViewEnginePath)
            .Distinct().ToList();

        Assert.NotEmpty(pages);
        Assert.All(pages, p => Assert.True(ConsolePages.ContainsKey(p) || SubPages.ContainsKey(p) || OtherSignedInPages.Contains(p), $"{p} is not in the role matrix."));
        Assert.Equal(9, ConsolePages.Count);
    }

    [Fact]
    public async Task Console_Role_Matrix()
    {
        using var anonymous = _app.CreateClient();
        using var orgAdmin = _app.CreateClient();
        await SignIn.AsAsync(orgAdmin, _app.Data.Alpha.Owner!.Email, _app.Data.Password);
        var clients = new Dictionary<ProviderRole, HttpClient>
        {
            [ProviderRole.Support] = await ProviderClientAsync(ProviderRole.Support),
            [ProviderRole.Billing] = await ProviderClientAsync(ProviderRole.Billing),
            [ProviderRole.Owner] = await ProviderClientAsync(ProviderRole.Owner),
        };

        foreach (var page in ConsolePages.Keys)
        {
            var url = Url(page);
            Assert.EndsWith("/Provider/Login", Location(await anonymous.GetAsync(url)));
            Assert.EndsWith("/Provider/AccessDenied", Location(await orgAdmin.GetAsync(url)));

            foreach (var (role, client) in clients)
            {
                using var response = await client.GetAsync(url);
                if (MayOpen(role, page))
                    Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role} should open {url}: {(int)response.StatusCode} {Location(response)}");
                else
                    Assert.True(Location(response).EndsWith("/Provider/AccessDenied"), $"{role} should not open {url}: {(int)response.StatusCode} {Location(response)}");
            }
        }

        var planId = await PlanIdAsync();
        foreach (var (page, (roles, url)) in SubPages)
        {
            Assert.EndsWith("/Provider/Login", Location(await anonymous.GetAsync(url(planId))));
            Assert.EndsWith("/Provider/AccessDenied", Location(await orgAdmin.GetAsync(url(planId))));
            foreach (var (role, client) in clients)
            {
                using var response = await client.GetAsync(url(planId));
                if (roles.Contains(role))
                    Assert.True(response.StatusCode == HttpStatusCode.OK, $"{role} should open {page}: {(int)response.StatusCode} {Location(response)}");
                else
                    Assert.True(Location(response).EndsWith("/Provider/AccessDenied"), $"{role} should not open {page}: {(int)response.StatusCode} {Location(response)}");
            }
        }

        // The jobs dashboard: owners only.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/Provider/jobs")).StatusCode);
        Assert.False((await clients[ProviderRole.Billing].GetAsync("/Provider/jobs")).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clients[ProviderRole.Owner].GetAsync("/Provider/jobs")).StatusCode);

        // The access-denied page says so, with status 403.
        var denied = await clients[ProviderRole.Support].GetAsync("/Provider/AccessDenied");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        foreach (var client in clients.Values) client.Dispose();
    }

    [Fact]
    public async Task Console_Menu_Shows_Only_What_The_Role_May_Open()
    {
        foreach (var role in new[] { ProviderRole.Support, ProviderRole.Billing, ProviderRole.Owner })
        {
            using var client = await ProviderClientAsync(role);
            var nav = (await GetDocumentAsync(client, "/Provider")).QuerySelector("[data-testid=console-nav]")!;
            var shown = nav.QuerySelectorAll("a[data-nav]").Select(a => a.GetAttribute("data-nav") ?? "").ToList();
            Assert.Equal(Allowed[role], shown);
        }
    }

    [Fact]
    public async Task Console_List_Pages_Have_No_Forms_Except_Sign_Out()
    {
        using var owner = await ProviderClientAsync(ProviderRole.Owner);
        foreach (var page in ConsolePages.Keys)
        {
            var html = await (await owner.GetAsync(Url(page))).Content.ReadAsStringAsync();
            var forms = new HtmlParser().ParseDocument(html).Forms
                .Where(f => f.Method.Equals("post", StringComparison.OrdinalIgnoreCase) && f.GetAttribute("data-role") != "sign-out").ToList();
            Assert.True(forms.Count == 0, $"{page} has a POST form on a list page.");
        }
    }

    [Fact]
    public async Task Console_Reauthentication_Takes_A_Current_Code_And_Returns()
    {
        var email = $"reauth-{Guid.NewGuid():N}"[..16] + "@console.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, ProviderRole.Owner);
        using var client = _app.CreateClient();
        await SignInAsync(client, email, key);

        const string target = "/Provider/Settings";
        var page = $"/Provider/Reauthenticate?returnUrl={Uri.EscapeDataString(target)}";
        Assert.Contains("not valid", await BodyAsync(await PostAsync(client, page, ("Code", "000000"))));
        Assert.Equal(target, Location(await PostAsync(client, page, ("Code", Totp.Code(key)))));

        // Only local addresses are followed back.
        var outside = $"/Provider/Reauthenticate?returnUrl={Uri.EscapeDataString("https://evil.example/")}";
        Assert.Equal("/Provider", Location(await PostAsync(client, outside, ("Code", Totp.Code(key)))));
    }

    [Fact]
    public async Task Console_Session_Ends_After_30_Idle_Minutes()
    {
        using var client = await ProviderClientAsync(ProviderRole.Owner);
        _app.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Provider")).StatusCode); // activity restarts the idle time

        _app.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Provider")).StatusCode);

        _app.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.EndsWith("/Provider/Login", Location(await client.GetAsync("/Provider")));
        Assert.EndsWith("/Provider/Login", Location(await client.GetAsync("/Provider"))); // signed out, not just refused once
    }

    [Fact]
    public async Task Console_Session_Ends_After_8_Hours_Even_When_Active()
    {
        using var client = await ProviderClientAsync(ProviderRole.Owner);
        for (var i = 0; i < 23; i++) // 23 x 20 minutes = 7 h 40 of steady activity
        {
            _app.Clock.Advance(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Provider")).StatusCode);
        }

        _app.Clock.Advance(TimeSpan.FromMinutes(25)); // 8 h 05: over the limit, though never idle
        Assert.EndsWith("/Provider/Login", Location(await client.GetAsync("/Provider")));
    }
}
