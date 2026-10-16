using Inprem.Platform.Application.Billing;
using Inprem.Platform.Application.Organizations;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Tests.Integration.Provider;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Billing;

/// <summary>
/// Phase 4 step 4.1 (migration M1k): invoices and their lines are stored; numbers come from a
/// database sequence and are never handed out twice; one invoice that is not cancelled per
/// subscription period; invoices and payments belong to a real organization.
/// </summary>
public class InvoiceStoreTests : IClassFixture<ProviderFixture>
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private readonly ProviderFixture _app;

    public InvoiceStoreTests(ProviderFixture app) => _app = app;

    private async Task<(int TenantId, Guid SubscriptionId)> OrganizationAsync()
    {
        using var scope = _app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var plans = sp.GetRequiredService<PlanService>();
        var planId = (await plans.CreateAsync($"iv-{Guid.NewGuid():N}"[..12], new("Invoice plan", null, null, null, 0, true), BillingCycle.Monthly, 30m, "USD", "test")).PlanId!.Value;
        var price = (await plans.FindAsync(planId))!.Prices[0];
        var tenant = (await sp.GetRequiredService<OrganizationService>().CreateAsync(new NewOrganization($"Invoice Org {Guid.NewGuid():N}"[..21], $"iv-{Guid.NewGuid():N}"[..14],
            "UTC", null, null, "I", "Owner", $"i-{Guid.NewGuid():N}"[..10] + "@invoice.test", null, planId, price.Id, BillingMode.Invoiced, 0), "test", "https://localhost")).TenantId!.Value;
        await using var db = _app.CreatePlatformDbContext();
        return (tenant, (await db.Subscriptions.SingleAsync(s => s.TenantId == tenant)).Id);
    }

    private static Invoice New(int tenantId, Guid? subscriptionId, string number, DateOnly? period = null) => Invoice.Create(
        tenantId, subscriptionId, period == null ? InvoiceSource.PayPalInvoice : InvoiceSource.Subscription, number, "USD",
        new[] { ("Monthly subscription", 1m, 30m), ("Extra kiosk", 2m, 5m) }, Today, Today.AddDays(14),
        period, period?.AddMonths(1), null, "test", DateTime.UtcNow);

    [Fact]
    public async Task Numbers_Come_From_The_Sequence_Above_The_Old_Ones_And_Never_Repeat()
    {
        var numbers = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            using var scope = _app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IInvoiceStore>().NextNumberAsync();
        }));
        Assert.Equal(20, numbers.Distinct().Count());
        Assert.All(numbers, n => Assert.Matches(@"^INV-\d{6}$", n));
        Assert.All(numbers, n => Assert.True(long.Parse(n[4..]) >= 100001));
    }

    [Fact]
    public async Task An_Invoice_Is_Stored_With_Its_Lines_And_Payments_Link_To_It()
    {
        var (tenant, _) = await OrganizationAsync();
        Guid id;
        using (var scope = _app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInvoiceStore>();
            var invoice = New(tenant, null, await store.NextNumberAsync());
            invoice.CreatedAtPayPal("INV2-STORE-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant());
            invoice.MarkSent("https://www.sandbox.paypal.com/invoice/p/#x", DateTime.UtcNow);
            await store.AddAsync(invoice);
            Assert.Equal(InvoiceSaveResult.Saved, await store.SaveAsync());
            id = invoice.Id;
        }

        using (var scope = _app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInvoiceStore>();
            var loaded = (await store.FindAsync(id))!;
            Assert.Equal(new[] { ("Monthly subscription", 1m, 30m), ("Extra kiosk", 2m, 5m) }, loaded.Lines.Select(l => (l.Description, l.Quantity, l.UnitAmount)));
            Assert.Equal((40m, InvoiceStatus.Sent), (loaded.Total, loaded.Status));
            Assert.Same(loaded, await store.FindByExternalIdAsync(loaded.ExternalInvoiceId!));
            Assert.Single(await store.ListForTenantAsync(tenant, 10));

            var db = scope.ServiceProvider.GetRequiredService<Inprem.Platform.Infrastructure.Persistence.PlatformDbContext>();
            db.Payments.Add(Payment.Record(tenant, null, PaymentSource.Offline, "BANK-1", 40m, "USD", DateTime.UtcNow, "test", id, OfflinePaymentMethod.BankTransfer));
            loaded.ApplyAmounts(40m, 0m, DateTime.UtcNow);
            Assert.Equal(InvoiceSaveResult.Saved, await store.SaveAsync());
        }

        await using var check = _app.CreatePlatformDbContext();
        Assert.Equal(InvoiceStatus.Paid, (await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == id)).Status);
        Assert.Equal(OfflinePaymentMethod.BankTransfer, (await check.Payments.AsNoTracking().SingleAsync(p => p.InvoiceId == id)).Method);
    }

    [Fact]
    public async Task One_Invoice_Per_Subscription_Period_Unless_Cancelled()
    {
        var (tenant, subscription) = await OrganizationAsync();
        var period = new DateOnly(2026, 11, 1);
        Guid first;
        using (var scope = _app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInvoiceStore>();
            var invoice = New(tenant, subscription, await store.NextNumberAsync(), period);
            await store.AddAsync(invoice);
            Assert.Equal(InvoiceSaveResult.Saved, await store.SaveAsync());
            first = invoice.Id;

            await store.AddAsync(New(tenant, subscription, await store.NextNumberAsync(), period));
            Assert.Equal(InvoiceSaveResult.DuplicatePeriod, await store.SaveAsync()); // refused, nothing saved

            await store.AddAsync(New(tenant, subscription, await store.NextNumberAsync(), period.AddMonths(1)));
            Assert.Equal(InvoiceSaveResult.Saved, await store.SaveAsync()); // the next period is fine
        }

        using (var scope = _app.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IInvoiceStore>();
            (await store.FindAsync(first))!.Cancel("Wrong amount", DateTime.UtcNow);
            Assert.Equal(InvoiceSaveResult.Saved, await store.SaveAsync());
            await store.AddAsync(New(tenant, subscription, await store.NextNumberAsync(), period));
            Assert.Equal(InvoiceSaveResult.Saved, await store.SaveAsync()); // a replacement after cancelling
        }

        await using var db = _app.CreatePlatformDbContext();
        Assert.Equal(3, await db.Invoices.CountAsync(i => i.TenantId == tenant));
    }

    [Fact]
    public async Task An_Invoice_Needs_A_Real_Organization()
    {
        using var scope = _app.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInvoiceStore>();
        await store.AddAsync(New(987654, null, await store.NextNumberAsync()));
        var e = await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync());
        Assert.Contains("FK_Invoices_Tenants_TenantId", ((SqlException)e.InnerException!).Message);
    }
}
