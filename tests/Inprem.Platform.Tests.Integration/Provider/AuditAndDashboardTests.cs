using System.Net;
using System.Text;
using Inprem.Platform.Domain.Provider;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>Phase 1 step 1.6: the audit log (filters, pages, CSV) and the dashboard figures.</summary>
public class AuditAndDashboardTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public AuditAndDashboardTests(ProviderFixture app) => _app = app;

    private async Task<(HttpClient Client, string UserId)> OwnerAsync()
    {
        var email = $"aud-{Guid.NewGuid():N}"[..14] + "@audit.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, ProviderRole.Owner);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return (client, (await _app.FindUserAsync(email)).Id);
    }

    [Fact]
    public async Task Audit_Log_Filters_Pages_And_Exports_Safely()
    {
        var (owner, ownerId) = await OwnerAsync();
        var alpha = _app.Data.Alpha.Id;

        // Some activity on Alpha: notes (one that would be a formula in a spreadsheet).
        for (var i = 0; i < 3; i++) await PostAsync(owner, $"/Provider/Tenants/AddNote/{alpha}", ("Text", $"Audit test note {i}"));
        await PostAsync(owner, $"/Provider/Tenants/AddNote/{alpha}", ("Text", "=HYPERLINK(\"http://evil.example\",\"click\")"));

        // Filter by actor, organization and action.
        var page = await GetDocumentAsync(owner, $"/Provider/Audit?Actor=provider:{ownerId}&TenantId={alpha}&Action=organization.note");
        var rows = page.QuerySelectorAll("[data-testid=audit] tbody tr");
        Assert.Equal(4, rows.Length);
        Assert.All(rows, r => Assert.Contains("organization.note-added", r.TextContent));
        Assert.Contains("@audit.test", rows[0].TextContent); // actor shown as email

        // A date range in the future finds nothing.
        var future = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd");
        Assert.Contains("Nothing matches", (await GetDocumentAsync(owner, $"/Provider/Audit?From={future}")).QuerySelector("[data-testid=audit]")!.TextContent);

        // Pages of 50.
        Assert.Contains("page 1 of", (await GetDocumentAsync(owner, "/Provider/Audit")).QuerySelector("[data-testid=pager]")!.TextContent);

        // CSV of the same filter: header, 4 rows, the formula neutralised.
        var csv = await owner.GetAsync($"/Provider/Audit?handler=Csv&Actor=provider:{ownerId}&TenantId={alpha}&Action=organization.note");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("When (UTC),Actor,Actor email,Action", lines[0]);
        Assert.Equal(5, lines.Length);
        Assert.Contains("\"'=HYPERLINK(", text);
        Assert.DoesNotContain(",=HYPERLINK", text);
    }

    [Fact]
    public async Task Dashboard_Shows_The_Figures()
    {
        var (owner, _) = await OwnerAsync();
        var gamma = _app.Data.Gamma.Id;

        var before = await GetDocumentAsync(owner, "/Provider");
        var suspendedBefore = int.Parse(before.QuerySelector("[data-testid=tile-suspended] .pc-tile-number")!.TextContent);
        Assert.NotNull(before.QuerySelector("[data-testid=tile-jobs]"));
        Assert.Equal("0", before.QuerySelector("[data-testid=tile-jobs] .pc-tile-number")!.TextContent.Trim());

        Assert.Contains("/Tenants/Details", Location(await PostAsync(owner, $"/Provider/Tenants/Suspend/{gamma}", ("Reason", "Dashboard test"))));
        var after = await GetDocumentAsync(owner, "/Provider");
        Assert.Equal(suspendedBefore + 1, int.Parse(after.QuerySelector("[data-testid=tile-suspended] .pc-tile-number")!.TextContent));
        Assert.Contains("organization.suspended", after.QuerySelector("[data-testid=recent]")!.TextContent);
        await PostAsync(owner, $"/Provider/Tenants/Reactivate/{gamma}", ("Reason", "Done"));

        // A trial ending soon is listed (a 7-day trial created through the wizard's service).
        using (var scope = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateScope(_app.Services))
        {
            var plans = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Inprem.Platform.Application.Billing.PlanService>(scope.ServiceProvider);
            var planId = (await plans.CreateAsync($"dash-{Guid.NewGuid():N}"[..14], new("Dash plan", null, null, null, 7, true), Inprem.Platform.Domain.Billing.BillingCycle.Monthly, 5m, "USD", "test")).PlanId!.Value;
            var priceId = (await plans.FindAsync(planId))!.Prices[0].Id;
            var orgs = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Inprem.Platform.Application.Organizations.OrganizationService>(scope.ServiceProvider);
            var name = $"Trial Org {Guid.NewGuid():N}"[..20];
            var created = await orgs.CreateAsync(new(name, $"trial-{Guid.NewGuid():N}"[..14], "UTC", null, null, "T", "Owner", $"t-{Guid.NewGuid():N}"[..10] + "@trial.test", null,
                planId, priceId, Inprem.Platform.Domain.Billing.BillingMode.PayPal, 7), "test", "https://localhost");
            Assert.True(created.Succeeded, created.Error);
            Assert.Contains(name, (await GetDocumentAsync(owner, "/Provider")).QuerySelector("[data-testid=trials]")!.TextContent);
        }
    }
}
