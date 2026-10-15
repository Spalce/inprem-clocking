using System.Net;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.PublicApi;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.PublicApi;

/// <summary>
/// Phase 3 step 3.2 (P3-8): the public read endpoints: plans on sale (cached 5 minutes), time
/// zones, slug availability with a suggestion. No credential, no personal data.
/// </summary>
public class PublicReadTests : IClassFixture<PublicApiFixture>
{
    private const string Api = "/api/public/v1";
    private readonly PublicApiFixture _app;

    public PublicReadTests(PublicApiFixture app) => _app = app;

    private async Task<Guid> PlanAsync(string name, bool isPublic, bool retire = false, decimal amount = 29m)
    {
        using var scope = _app.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
        var id = (await plans.CreateAsync($"pr-{Guid.NewGuid():N}"[..12], new(name, $"{name} plan", "Kiosk\nReports\n", 50, 30, isPublic), BillingCycle.Monthly, amount, "USD", "test")).PlanId!.Value;
        await plans.AddPriceAsync(id, BillingCycle.Yearly, amount * 10, "USD", "test");
        if (retire) await plans.RetireAsync(id, "test");
        return id;
    }

    [Fact]
    public async Task Plans_Lists_Public_Offered_Plans_With_Prices_And_Caches_Them()
    {
        var shown = await PlanAsync("Shown", isPublic: true, amount: 19.5m);
        var consoleOnly = await PlanAsync("Console only", isPublic: false);
        var retired = await PlanAsync("Retired", isPublic: true, retire: true);

        using var client = _app.CreateClient();
        var response = await client.GetAsync(Api + "/plans");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=300", response.Headers.CacheControl!.ToString());
        var plans = (await PublicApiFixture.JsonAsync(response)).EnumerateArray().ToList();

        var plan = Assert.Single(plans, p => p.GetProperty("planId").GetGuid() == shown);
        Assert.DoesNotContain(plans, p => p.GetProperty("planId").GetGuid() == consoleOnly || p.GetProperty("planId").GetGuid() == retired);
        Assert.Equal(("Shown", 30, 50), (plan.GetProperty("name").GetString(), plan.GetProperty("trialDays").GetInt32(), plan.GetProperty("limits").GetProperty("maxPeople").GetInt32()));
        Assert.Equal(new[] { "Kiosk", "Reports" }, plan.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        var prices = plan.GetProperty("prices").EnumerateArray().ToList();
        Assert.Equal(("Monthly", "19.50", "USD"), (prices[0].GetProperty("billingCycle").GetString(), prices[0].GetProperty("price").GetProperty("amount").GetString(), prices[0].GetProperty("price").GetProperty("currency").GetString()));
        Assert.Equal(("Yearly", "195.00"), (prices[1].GetProperty("billingCycle").GetString(), prices[1].GetProperty("price").GetProperty("amount").GetString()));

        // Cached: a plan added now appears only after the 5 minutes.
        var later = await PlanAsync("Later", isPublic: true);
        var again = (await PublicApiFixture.JsonAsync(await client.GetAsync(Api + "/plans"))).EnumerateArray();
        Assert.DoesNotContain(again, p => p.GetProperty("planId").GetGuid() == later);
    }

    [Fact]
    public async Task Time_Zones_Are_Iana_Ids_With_Their_Offset()
    {
        using var client = _app.CreateClient();
        var zones = (await PublicApiFixture.JsonAsync(await client.GetAsync(Api + "/timezones"))).EnumerateArray().ToList();
        Assert.Equal("America/New_York", zones[0].GetProperty("id").GetString());
        Assert.Contains(zones, z => z.GetProperty("id").GetString() == "Africa/Accra" && z.GetProperty("displayName").GetString()!.Contains("UTC+00:00"));
        Assert.True(zones.Count > 50);
        Assert.All(zones, z => Assert.False(z.GetProperty("id").GetString()!.Contains(' ')));
    }

    [Fact]
    public async Task Slug_Availability_With_A_Free_Suggestion()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<OrganizationService>().CreateAsync(new NewOrganization("Akim Asafo", "akim-asafo", "UTC", null, null,
                "A", "Owner", $"o-{Guid.NewGuid():N}"[..10] + "@slug.test", null, null, null, BillingMode.Free, 0), "test", "https://localhost");
            Assert.True(created.Succeeded, created.Error);
        }

        using var client = _app.CreateClient();
        async Task<System.Text.Json.JsonElement> CheckAsync(string slug) => await PublicApiFixture.JsonAsync(await client.GetAsync($"{Api}/slugs/{slug}/availability"));

        var free = await CheckAsync("free-centre-" + Guid.NewGuid().ToString("N")[..6]);
        Assert.True(free.GetProperty("available").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, free.GetProperty("suggestion").ValueKind);

        var taken = await CheckAsync("akim-asafo");
        Assert.False(taken.GetProperty("available").GetBoolean());
        var suggestion = taken.GetProperty("suggestion").GetString()!;
        Assert.StartsWith("akim-asafo-", suggestion);
        Assert.True((await CheckAsync(suggestion)).GetProperty("available").GetBoolean());

        var reserved = await CheckAsync("admin");
        Assert.False(reserved.GetProperty("available").GetBoolean());
        Assert.False(string.IsNullOrEmpty(reserved.GetProperty("suggestion").GetString()));

        var bad = await client.GetAsync($"{Api}/slugs/Bad_Slug!/availability");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var problem = await PublicApiFixture.JsonAsync(bad);
        Assert.Equal(Problems.ValidationFailed, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("slug", out _));
        Assert.DoesNotContain("Bad_Slug", await bad.Content.ReadAsStringAsync()); // the input is not echoed
    }

    [Fact]
    public async Task Public_Answers_Contain_No_Personal_Data()
    {
        await PlanAsync("Privacy", isPublic: true);
        using var client = _app.CreateClient();
        foreach (var path in new[] { "/plans", "/timezones", "/slugs/alpha/availability", "/health" })
        {
            var body = await (await client.GetAsync(Api + path)).Content.ReadAsStringAsync();
            Assert.DoesNotContain("@", body);
            Assert.DoesNotContain(_app.Data.Alpha.Admin!.LastName, body);
            Assert.DoesNotContain(_app.Data.Alpha.Staff[0].Email, body);
        }
    }
}
