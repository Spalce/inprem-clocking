using System.Net;
using System.Text;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Tests.Integration.Provider;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Billing;

/// <summary>
/// Phase 4 step 4.3 (P4-3 with the pretend PayPal): the monthly report matches the console's PayPal
/// payments and refunds to PayPal's transactions by id, lists every difference, shows offline money
/// apart, says when PayPal could not be asked, and exports the same as CSV.
/// </summary>
public class MonthlyReportTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public MonthlyReportTests(ProviderFixture app) => _app = app;

    private static DateTime At(int day, int hour = 10) => new(2026, 9, day, hour, 0, 0, DateTimeKind.Utc);

    private async Task<MonthlyReport> ReportAsync(int year, int month)
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MonthlyBillingReport>().BuildAsync(year, month);
    }

    [Fact]
    public async Task September_Matches_By_Id_And_Lists_Every_Difference()
    {
        var alpha = _app.Data.Alpha.Id;
        await using (var db = _app.CreatePlatformDbContext())
        {
            var matched = Payment.Record(alpha, null, PaymentSource.PayPal, "SEP-SALE-MATCH", 29m, "USD", At(3), "paypal:webhook");
            var refunded = Payment.Record(alpha, null, PaymentSource.PayPal, "SEP-SALE-REFUNDED", 29m, "USD", At(5), "paypal:webhook");
            var differs = Payment.Record(alpha, null, PaymentSource.PayPal, "SEP-SALE-DIFFERS", 29m, "USD", At(7), "paypal:webhook");
            var onlyHere = Payment.Record(alpha, null, PaymentSource.PayPal, "SEP-SALE-ONLY-HERE", 29m, "USD", At(9), "paypal:webhook");
            var offline = Payment.Record(alpha, null, PaymentSource.Offline, "SEP-GCB-001", 100m, "USD", At(11), "provider:test", null, OfflinePaymentMethod.BankTransfer);
            db.Payments.AddRange(matched, refunded, differs, onlyHere, offline);
            db.PaymentRefunds.Add(PaymentRefund.Record(refunded, "SEP-REFUND-1", 10m, RefundStatus.Completed, "Duplicate", At(6), "provider:test"));
            refunded.SetRefundedTotal(10m, At(6));
            await db.SaveChangesAsync();
        }
        foreach (var t in new[]
                 {
                     new PayPalTransaction("SEP-SALE-MATCH", "T0002", At(3), 29m, "USD", "S", null),
                     new PayPalTransaction("SEP-SALE-REFUNDED", "T0002", At(5), 29m, "USD", "S", null),
                     new PayPalTransaction("SEP-REFUND-1", "T1107", At(6), -10m, "USD", "S", null),
                     new PayPalTransaction("SEP-SALE-DIFFERS", "T0002", At(7), 25m, "USD", "S", null),
                     new PayPalTransaction("SEP-ONLY-PAYPAL", "T0006", At(12), 40m, "USD", "S", "INV2-ELSEWHERE"),
                     new PayPalTransaction("SEP-FEE-TRANSFER", "T0400", At(13), -50m, "USD", "S", null), // a withdrawal: not part of the report
                     new PayPalTransaction("SEP-CHARGEBACK", "T1106", At(14), -29m, "USD", "S", null),
                     new PayPalTransaction("SEP-DENIED", "T0002", At(15), 29m, "USD", "D", null),
                 })
            _app.Invoicing.Transactions.Enqueue(t);

        var report = await ReportAsync(2026, 9);

        Assert.True(report.PayPalChecked);
        Assert.Equal("September 2026", report.Label);
        ReportMatch Of(string id) => report.Lines.Single(l => l.ConsoleReference == id || l.PayPalId == id).Match;
        Assert.Equal(ReportMatch.Matched, Of("SEP-SALE-MATCH"));
        Assert.Equal(ReportMatch.Matched, Of("SEP-SALE-REFUNDED"));
        Assert.Equal(ReportMatch.Matched, Of("SEP-REFUND-1"));
        Assert.Equal(ReportMatch.AmountDiffers, Of("SEP-SALE-DIFFERS"));
        Assert.Equal(ReportMatch.OnlyInConsole, Of("SEP-SALE-ONLY-HERE"));
        Assert.Equal(ReportMatch.OnlyAtPayPal, Of("SEP-ONLY-PAYPAL"));
        Assert.Equal(ReportMatch.Offline, Of("SEP-GCB-001"));
        Assert.Equal(ReportMatch.Info, Of("SEP-CHARGEBACK"));
        Assert.DoesNotContain(report.Lines, l => l.PayPalId is "SEP-FEE-TRANSFER" or "SEP-DENIED");
        Assert.Equal(3, report.Differences.Count);
        Assert.All(report.Lines.Take(3), l => Assert.True(l.IsDifference)); // differences first

        var usd = report.Totals.Single(t => t.Currency == "USD");
        Assert.Equal((116m, 10m, 123m, 10m, 100m, 0m), (usd.ConsoleReceived, usd.ConsoleRefunded, usd.PayPalReceived, usd.PayPalRefunded, usd.OfflineReceived, usd.OfflineRefunded));
        Assert.False(usd.PayPalEqual);

        var csv = Encoding.UTF8.GetString(MonthlyBillingReport.Csv(report)).TrimStart('﻿');
        Assert.StartsWith("Kind,Date (UTC),Organization,Source,Console reference,PayPal id,Console amount,PayPal amount,Currency,Result,Note\r\n", csv);
        Assert.Contains("Payment,2026-09-07 10:00,Alpha,PayPal,SEP-SALE-DIFFERS,SEP-SALE-DIFFERS,29.00,25.00,USD,AmountDiffers,", csv);
        Assert.Contains("SEP-GCB-001,,100.00,,USD,Offline", csv);

        // The console page shows the same, for Owners and Billing, with the CSV.
        var email = $"rep-{Guid.NewGuid():N}"[..14] + "@console.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, ProviderRole.Billing);
        using var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        var page = await GetDocumentAsync(client, "/Provider/Billing/Reconciliation?Month=2026-09");
        Assert.Equal("Different", page.QuerySelector("[data-testid=equal]")!.TextContent.Trim());
        Assert.Equal(3, page.QuerySelectorAll("[data-testid=lines] tr[data-match=OnlyInConsole], [data-testid=lines] tr[data-match=OnlyAtPayPal], [data-testid=lines] tr[data-match=AmountDiffers]").Length);
        using var download = await client.GetAsync("/Provider/Billing/Reconciliation?handler=Csv&Month=2026-09");
        Assert.Equal("text/csv", download.Content.Headers.ContentType!.MediaType);
        Assert.Contains("inprem-paypal-reconciliation-2026-09.csv", download.Content.Headers.ContentDisposition!.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Provider/Billing/Reconciliation?Month=not-a-month")).StatusCode);
    }

    [Fact]
    public async Task When_PayPal_Cannot_Be_Asked_The_Report_Says_So()
    {
        await using (var db = _app.CreatePlatformDbContext())
        {
            db.Payments.Add(Payment.Record(_app.Data.Beta.Id, null, PaymentSource.PayPal, "AUG-SALE-1", 29m, "USD", new DateTime(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc), "paypal:webhook"));
            await db.SaveChangesAsync();
        }
        _app.Invoicing.FailNext = true;
        var report = await ReportAsync(2026, 8);

        Assert.False(report.PayPalChecked);
        Assert.Contains("could not be asked", report.PayPalError);
        Assert.Equal("PayPal not checked", report.Lines.Single(l => l.ConsoleReference == "AUG-SALE-1").Note);
    }
}
