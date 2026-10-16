using System.Net;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Tests.Integration.Provider;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Billing;

/// <summary>
/// Phase 4 step 4.4: an organization's owner sees its own invoices on the Billing page (with the
/// PayPal payment link while something is owed, and a receipt once paid); drafts and other
/// organizations' invoices are never shown; admins who are not the owner cannot open them.
/// </summary>
public class OrganizationInvoiceTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public OrganizationInvoiceTests(ProviderFixture app) => _app = app;

    private DateOnly Today => DateOnly.FromDateTime(_app.Clock.UtcNow);

    private async Task<Invoice> AddAsync(int tenantId, InvoiceSource source, DateOnly due, Action<Invoice>? shape = null, bool send = true)
    {
        await using var db = _app.CreatePlatformDbContext();
        var invoice = Invoice.Create(tenantId, null, source, $"INV-{Random.Shared.Next(700000, 799999)}", "USD", new[] { ("Setup", 1m, 40m) },
            due.AddDays(-14) < Today ? due.AddDays(-14) : Today, due, null, null, "Thank you", "provider:test", _app.Clock.UtcNow);
        if (send)
        {
            if (source != InvoiceSource.Manual) invoice.CreatedAtPayPal("INV2-ORG-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant());
            invoice.MarkSent(source == InvoiceSource.Manual ? null : "https://www.sandbox.paypal.com/invoice/p/#ORG" + invoice.Number, _app.Clock.UtcNow);
        }
        shape?.Invoke(invoice);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice;
    }

    private async Task<HttpClient> SignedInAsync(string email)
    {
        var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);
        return client;
    }

    [Fact]
    public async Task The_Owner_Sees_Its_Invoices_With_Pay_Links_Drafts_And_Other_Organizations_Never()
    {
        var payable = await AddAsync(_app.Data.Alpha.Id, InvoiceSource.PayPalInvoice, Today.AddDays(14));
        var overdue = await AddAsync(_app.Data.Alpha.Id, InvoiceSource.Manual, Today.AddDays(-3));
        var paid = await AddAsync(_app.Data.Alpha.Id, InvoiceSource.PayPalInvoice, Today.AddDays(7), i => i.ApplyAmounts(40m, 10m, _app.Clock.UtcNow));
        var draft = await AddAsync(_app.Data.Alpha.Id, InvoiceSource.PayPalInvoice, Today.AddDays(7), send: false);
        var beta = await AddAsync(_app.Data.Beta.Id, InvoiceSource.PayPalInvoice, Today.AddDays(7));

        using var owner = await SignedInAsync(_app.Data.Alpha.Owner!.Email);
        var page = await GetDocumentAsync(owner, "/Billing");
        var rows = page.QuerySelectorAll("[data-testid=invoices] tr[data-number]").ToDictionary(r => r.GetAttribute("data-number")!);
        Assert.Contains(payable.Number, rows.Keys);
        Assert.DoesNotContain(draft.Number, rows.Keys);
        Assert.DoesNotContain(beta.Number, rows.Keys);
        Assert.Equal(payable.PaymentUrl, rows[payable.Number].QuerySelector("[data-testid=pay]")!.GetAttribute("href"));
        Assert.Equal("Overdue", rows[overdue.Number].QuerySelector("[data-testid=invoice-status]")!.TextContent.Trim());
        Assert.Null(rows[overdue.Number].QuerySelector("[data-testid=pay]")); // offline: no PayPal link
        Assert.Equal("Paid (10.00 USD back)", rows[paid.Number].QuerySelector("[data-testid=invoice-status]")!.TextContent.Trim());
        Assert.Null(rows[paid.Number].QuerySelector("[data-testid=pay]"));

        // The invoice page: a receipt once paid; the offline invoice says how to pay.
        var receipt = await GetDocumentAsync(owner, $"/Billing/Invoices/{paid.Id}");
        Assert.StartsWith("Receipt", receipt.QuerySelector("h1")!.TextContent.Trim());
        Assert.Contains("quote", (await GetDocumentAsync(owner, $"/Billing/Invoices/{overdue.Id}")).QuerySelector("[data-testid=pay-offline]")!.TextContent);

        // Another organization's invoice, or a draft: not found.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/Billing/Invoices/{beta.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/Billing/Invoices/{draft.Id}")).StatusCode);

        // An admin who is not the owner cannot open them.
        using var admin = await SignedInAsync(_app.Data.Alpha.Admin!.Email);
        Assert.NotEqual(HttpStatusCode.OK, (await admin.GetAsync($"/Billing/Invoices/{payable.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_Website_Lists_The_Same_For_A_Billing_Link()
    {
        var payable = await AddAsync(_app.Data.Beta.Id, InvoiceSource.PayPalInvoice, Today.AddDays(10));
        var draft = await AddAsync(_app.Data.Beta.Id, InvoiceSource.PayPalInvoice, Today.AddDays(10), send: false);
        var alpha = await AddAsync(_app.Data.Alpha.Id, InvoiceSource.PayPalInvoice, Today.AddDays(10));

        using var scope = _app.Services.CreateScope();
        var token = (await scope.ServiceProvider.GetRequiredService<Inprem.Platform.Application.PublicApi.PublicApiTokens>().IssueBillingAccessAsync(_app.Data.Beta.Id)).Value;
        using var website = _app.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/public/v1/account/invoices");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await website.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var numbers = list.EnumerateArray().Select(i => i.GetProperty("number").GetString()).ToList();
        Assert.Contains(payable.Number, numbers);
        Assert.DoesNotContain(draft.Number, numbers);
        Assert.DoesNotContain(alpha.Number, numbers);
        var item = list.EnumerateArray().Single(i => i.GetProperty("number").GetString() == payable.Number);
        Assert.Equal(("Sent", "40.00", "40.00", "USD", payable.PaymentUrl), (item.GetProperty("status").GetString(), item.GetProperty("total").GetProperty("amount").GetString(),
            item.GetProperty("amountDue").GetProperty("amount").GetString(), item.GetProperty("total").GetProperty("currency").GetString(), item.GetProperty("paymentUrl").GetString()));
        Assert.Equal(payable.DueDate.ToString("yyyy-MM-dd"), item.GetProperty("dueDate").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, item.GetProperty("amountRefunded").ValueKind);
    }
}
