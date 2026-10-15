using System.Net;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>
/// Phase 2 step 2.3: "Publish to PayPal" on a plan's page (pretend PayPal): one product, one
/// PayPal plan per active price, nothing twice, a new price later gets its own PayPal plan, a
/// failure halfway is finished by publishing again, one audit entry per publish.
/// </summary>
public class PlanPublishTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public PlanPublishTests(ProviderFixture app) => _app = app;

    private async Task<HttpClient> SignedInAsync(ProviderRole role)
    {
        var email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..20] + "@publish.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, role);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return client;
    }

    private async Task<Guid> NewPlanAsync(HttpClient client, decimal amount = 29m)
    {
        var created = await PostAsync(client, "/Provider/Plans/New", ("Code", $"pub-{Guid.NewGuid():N}"[..12]), ("Plan.Name", "Publish me"),
            ("Plan.Description", "For publishing"), ("Plan.TrialDays", "30"), ("Price.Cycle", "Monthly"), ("Price.Amount", amount.ToString("0.00")), ("Price.Currency", "USD"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var location = Location(created);
        return Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
    }

    private async Task<Plan> LoadAsync(Guid id)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.Plans.Include(p => p.Prices).AsNoTracking().SingleAsync(p => p.Id == id);
    }

    private async Task<List<string>> AuditAsync(Guid id)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.AuditEntries.Where(a => a.Target == id.ToString() && a.Action.StartsWith("plan.publish")).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync();
    }

    private static Task<HttpResponseMessage> PublishAsync(HttpClient client, Guid id) =>
        PostFormAsync(client, $"/Provider/Plans/Details/{id}", action => action.EndsWith("handler=Publish", StringComparison.Ordinal));

    [Fact]
    public async Task Publish_Creates_One_Product_And_A_PayPal_Plan_Per_Active_Price()
    {
        using var billing = await SignedInAsync(ProviderRole.Billing);
        var id = await NewPlanAsync(billing);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(billing, $"/Provider/Plans/NewPrice/{id}", ("Cycle", "Yearly"), ("Amount", "290.00"), ("Currency", "USD"))).StatusCode);

        var before = await GetDocumentAsync(billing, $"/Provider/Plans/Details/{id}");
        Assert.Equal("Not published", before.QuerySelector("[data-testid=paypal-product]")!.TextContent.Trim());
        Assert.NotNull(before.QuerySelector("[data-testid=publish]"));

        Assert.Equal(HttpStatusCode.Redirect, (await PublishAsync(billing, id)).StatusCode);
        var plan = await LoadAsync(id);
        Assert.NotNull(plan.PayPalProductId);
        Assert.All(plan.Prices, p => Assert.NotNull(p.PayPalPlanId));
        Assert.Equal(plan.Prices.Count, plan.Prices.Select(p => p.PayPalPlanId).Distinct().Count());

        // What PayPal got: our product and each price as a plan (the client adds the D23 rules, see PayPalClientTests).
        Assert.Equal(("Publish me", "For publishing"), _app.PayPal.Products[plan.PayPalProductId!]);
        var monthly = _app.PayPal.Plans[plan.Prices.Single(p => p.Cycle == BillingCycle.Monthly).PayPalPlanId!];
        Assert.Equal((plan.PayPalProductId, BillingCycle.Monthly, 29m, "USD", "Publish me (monthly)"), (monthly.ProductId, monthly.Cycle, monthly.Amount, monthly.Currency, monthly.Name));
        Assert.Equal(290m, _app.PayPal.Plans[plan.Prices.Single(p => p.Cycle == BillingCycle.Yearly).PayPalPlanId!].Amount);

        var after = await GetDocumentAsync(billing, $"/Provider/Plans/Details/{id}");
        Assert.Contains("Published to PayPal", after.QuerySelector("[data-testid=success]")!.TextContent);
        Assert.Equal(plan.PayPalProductId, after.QuerySelector("[data-testid=paypal-product]")!.TextContent.Trim());
        Assert.Null(after.QuerySelector("[data-testid=publish]")); // nothing left to publish
        Assert.Equal(new[] { "plan.published" }, await AuditAsync(id));

        // A new amount later = a new price (the old one retired): only it is published, under the same product.
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(billing, $"/Provider/Plans/Details/{id}", action => action.Contains("handler=RetirePrice", StringComparison.Ordinal)
            && action.Contains(plan.Prices.Single(p => p.Cycle == BillingCycle.Monthly).Id.ToString(), StringComparison.Ordinal))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostAsync(billing, $"/Provider/Plans/NewPrice/{id}", ("Cycle", "Monthly"), ("Amount", "35.00"), ("Currency", "USD"))).StatusCode);
        var products = _app.PayPal.Products.Count;
        Assert.Equal(HttpStatusCode.Redirect, (await PublishAsync(billing, id)).StatusCode);
        var again = await LoadAsync(id);
        Assert.Equal(plan.PayPalProductId, again.PayPalProductId);
        Assert.Equal(products, _app.PayPal.Products.Count);
        Assert.Equal(35m, _app.PayPal.Plans[again.Prices.Single(p => p.Amount == 35m).PayPalPlanId!].Amount);
        Assert.Equal(new[] { "plan.published", "plan.published" }, await AuditAsync(id));
    }

    [Fact]
    public async Task A_Failure_Halfway_Keeps_What_Was_Done_And_Publishing_Again_Finishes()
    {
        using var owner = await SignedInAsync(ProviderRole.Owner);
        var id = await NewPlanAsync(owner, 19m);
        await PostAsync(owner, $"/Provider/Plans/NewPrice/{id}", ("Cycle", "Yearly"), ("Amount", "190.00"), ("Currency", "USD"));

        // The product and the first plan get through; PayPal then fails (after the client's own retries).
        var calls = _app.PayPal.Calls.Count;
        _app.PayPal.FailAfter(2);
        Assert.Equal(HttpStatusCode.Redirect, (await PublishAsync(owner, id)).StatusCode);

        var page = await GetDocumentAsync(owner, $"/Provider/Plans/Details/{id}");
        Assert.Contains("publish again to finish", page.QuerySelector("[data-testid=error]")!.TextContent);
        var half = await LoadAsync(id);
        Assert.NotNull(half.PayPalProductId);
        Assert.Single(half.Prices, p => p.PayPalPlanId != null);
        Assert.Equal(new[] { "plan.publish-failed" }, await AuditAsync(id));

        Assert.Equal(HttpStatusCode.Redirect, (await PublishAsync(owner, id)).StatusCode);
        var full = await LoadAsync(id);
        Assert.Equal(half.PayPalProductId, full.PayPalProductId);
        Assert.All(full.Prices, p => Assert.NotNull(p.PayPalPlanId));
        Assert.Equal(1, _app.PayPal.Calls.Skip(calls).Count(c => c.StartsWith("CreateProduct PROD-")));
        Assert.Equal(new[] { "plan.publish-failed", "plan.published" }, await AuditAsync(id));
    }

    [Fact]
    public async Task Only_Billing_And_Owners_Publish_And_Not_Without_PayPal()
    {
        using var owner = await SignedInAsync(ProviderRole.Owner);
        var id = await NewPlanAsync(owner);

        using var support = await SignedInAsync(ProviderRole.Support);
        var page = await GetDocumentAsync(support, $"/Provider/Plans/Details/{id}");
        Assert.Null(page.QuerySelector("[data-testid=publish]"));
        var token = page.QuerySelector("input[name=__RequestVerificationToken]")?.GetAttribute("value") ?? "";
        var attempt = await support.PostAsync($"/Provider/Plans/Details/{id}?handler=Publish", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.DoesNotContain("/Plans/Details", attempt.Headers.Location?.ToString() ?? ""); // refused (access denied), not done
        Assert.Null((await LoadAsync(id)).PayPalProductId);

        _app.PayPal.IsConfigured = false;
        try
        {
            var noPayPal = await GetDocumentAsync(owner, $"/Provider/Plans/Details/{id}");
            Assert.Null(noPayPal.QuerySelector("[data-testid=publish]"));
            Assert.NotNull(noPayPal.QuerySelector("[data-testid=paypal-not-configured]"));
        }
        finally
        {
            _app.PayPal.IsConfigured = true;
        }
        Assert.Empty(await AuditAsync(id));
    }
}
