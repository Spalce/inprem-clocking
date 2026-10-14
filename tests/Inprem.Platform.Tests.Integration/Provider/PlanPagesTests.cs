using System.Net;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>Phase 1 step 1.3: the Plans screens end to end; one audit entry per change (P1-4).</summary>
public class PlanPagesTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public PlanPagesTests(ProviderFixture app) => _app = app;

    private async Task<HttpClient> SignedInAsync(ProviderRole role)
    {
        var email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..20] + "@plans.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, role);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return client;
    }

    private async Task<List<string>> AuditActionsAsync(Guid planId)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.AuditEntries.Where(a => a.Target == planId.ToString()).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync();
    }

    private static Guid IdFrom(string location) => Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);

    [Fact]
    public async Task Plans_Create_Edit_Price_And_Retire_Through_The_Pages()
    {
        using var billing = await SignedInAsync(ProviderRole.Billing);
        var code = $"std-{Guid.NewGuid():N}"[..12];

        // Create: refused values come back with a message; the right ones open the new plan.
        var bad = await PostAsync(billing, "/Provider/Plans/New", ("Code", "Bad Code"), ("Plan.Name", "Standard"), ("Plan.TrialDays", "30"),
            ("Price.Cycle", "Monthly"), ("Price.Amount", "29.99"), ("Price.Currency", "USD"));
        Assert.Contains("lower-case", await BodyAsync(bad));

        var created = await PostAsync(billing, "/Provider/Plans/New", ("Code", code), ("Plan.Name", "Standard"), ("Plan.Description", "For most centres"),
            ("Plan.Features", "Kiosk\nReports"), ("Plan.TrialDays", "30"), ("Plan.MaxPeople", "100"), ("Plan.IsPublic", "true"),
            ("Price.Cycle", "Monthly"), ("Price.Amount", "29.99"), ("Price.Currency", "USD"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var id = IdFrom(Location(created));
        var detail = await GetDocumentAsync(billing, Location(created));
        Assert.Contains("created", detail.QuerySelector("[data-testid=success]")!.TextContent);
        Assert.Contains("29.99 USD", detail.QuerySelector("[data-testid=prices]")!.TextContent);

        // The same code twice is refused.
        var duplicate = await PostAsync(billing, "/Provider/Plans/New", ("Code", code), ("Plan.Name", "Again"), ("Plan.TrialDays", "30"),
            ("Price.Cycle", "Monthly"), ("Price.Amount", "10"), ("Price.Currency", "USD"));
        Assert.Contains("already used", await BodyAsync(duplicate));

        // Edit (separate page, back to the plan).
        var edited = await PostAsync(billing, $"/Provider/Plans/Edit/{id}", ("Plan.Name", "Standard plan"), ("Plan.TrialDays", "14"), ("Plan.MaxPeople", "100"), ("Plan.IsPublic", "false"));
        Assert.Equal($"/Provider/Plans/Details/{id}", Location(edited));

        // A second monthly price is refused; a yearly one is added; then retired.
        Assert.Contains("already has an active monthly price", await BodyAsync(await PostAsync(billing, $"/Provider/Plans/NewPrice/{id}", ("Price.Cycle", "Monthly"), ("Price.Amount", "39"), ("Price.Currency", "USD"))));
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(billing, $"/Provider/Plans/NewPrice/{id}", ("Price.Cycle", "Yearly"), ("Price.Amount", "299"), ("Price.Currency", "USD"))).StatusCode);

        Guid yearly;
        await using (var db = _app.CreatePlatformDbContext())
        {
            var plan = await db.Plans.Include(p => p.Prices).SingleAsync(p => p.Id == id);
            Assert.Equal(("Standard plan", 14, false, 100), (plan.Name, plan.TrialDays, plan.IsPublic, plan.MaxPeople ?? 0));
            yearly = plan.Prices.Single(p => p.Cycle == BillingCycle.Yearly).Id;
        }

        // Retire the yearly price, then the plan (buttons on the plan's page).
        var page = await GetDocumentAsync(billing, $"/Provider/Plans/Details/{id}");
        Assert.Equal(2, page.QuerySelectorAll("[data-testid=retire-price]").Length);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(billing, $"/Provider/Plans/Details/{id}", yearly.ToString())).StatusCode);
        var retired = await PostFormAsync(billing, $"/Provider/Plans/Details/{id}", action => action.EndsWith("handler=Retire", StringComparison.Ordinal));
        Assert.Equal($"/Provider/Plans/Details/{id}", Location(retired));
        Assert.Contains("Plan retired", (await GetDocumentAsync(billing, Location(retired))).QuerySelector("[data-testid=success]")!.TextContent);

        await using (var db = _app.CreatePlatformDbContext())
        {
            var plan = await db.Plans.Include(p => p.Prices).SingleAsync(p => p.Id == id);
            Assert.Equal(PlanStatus.Retired, plan.Status);
            Assert.All(plan.Prices, p => Assert.Equal(PlanStatus.Retired, p.Status));
        }

        // One audit entry per change, none for the refusals.
        Assert.Equal(new[] { "plan.created", "plan.updated", "plan.price-added", "plan.price-retired", "plan.retired" }, await AuditActionsAsync(id));

        // The list shows it, with search and status filter.
        var list = await GetDocumentAsync(billing, $"/Provider/Plans?Search={code}&Status=Retired");
        Assert.Contains("Standard plan", list.QuerySelector("[data-testid=plans]")!.TextContent);
        var none = await GetDocumentAsync(billing, $"/Provider/Plans?Search={code}&Status=Active");
        Assert.Contains("No plans match", none.QuerySelector("[data-testid=plans]")!.TextContent);
    }

    [Fact]
    public async Task Plans_Support_Can_View_But_Not_Change()
    {
        using var owner = await SignedInAsync(ProviderRole.Owner);
        var code = $"sup-{Guid.NewGuid():N}"[..12];
        var created = await PostAsync(owner, "/Provider/Plans/New", ("Code", code), ("Plan.Name", "Support check"), ("Plan.TrialDays", "30"),
            ("Price.Cycle", "Monthly"), ("Price.Amount", "9"), ("Price.Currency", "USD"));
        var id = IdFrom(Location(created));

        using var support = await SignedInAsync(ProviderRole.Support);
        var list = await GetDocumentAsync(support, "/Provider/Plans");
        Assert.Null(list.QuerySelector("[data-testid=new]"));
        var detail = await GetDocumentAsync(support, $"/Provider/Plans/Details/{id}");
        Assert.Null(detail.QuerySelector("[data-testid=edit]"));
        Assert.Null(detail.QuerySelector("[data-testid=retire]"));

        // Even a hand-made request to retire is refused (403 via the access-denied page) and changes nothing.
        var token = detail.QuerySelector("input[name=__RequestVerificationToken]")?.GetAttribute("value")
            ?? list.QuerySelector("input[name=__RequestVerificationToken]")?.GetAttribute("value");
        var form = new Dictionary<string, string>();
        if (token != null) form["__RequestVerificationToken"] = token;
        var attempt = await support.PostAsync($"/Provider/Plans/Details/{id}?handler=Retire", new FormUrlEncodedContent(form));
        Assert.True(attempt.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden);
        Assert.False(Location(attempt).EndsWith($"/Details/{id}"), "Support's retire request was carried out.");

        await using var db = _app.CreatePlatformDbContext();
        Assert.Equal(PlanStatus.Active, (await db.Plans.SingleAsync(p => p.Id == id)).Status);
        Assert.Single(await db.AuditEntries.Where(a => a.Target == id.ToString()).ToListAsync()); // just "created"
    }
}
