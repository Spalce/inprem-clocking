using System.Globalization;
using System.Text;
using Inprem.Platform.Application.Audit;
using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

public enum ReportMatch
{
    /// <summary>The same id and amount in the console and at PayPal.</summary>
    Matched,

    /// <summary>The same id, a different amount.</summary>
    AmountDiffers,

    /// <summary>Recorded in the console, not found at PayPal for the month.</summary>
    OnlyInConsole,

    /// <summary>At PayPal, not recorded in the console.</summary>
    OnlyAtPayPal,

    /// <summary>Paid or returned offline: not at PayPal by nature.</summary>
    Offline,

    /// <summary>At PayPal, listed for information (e.g. a chargeback reversal): not a difference.</summary>
    Info,
}

/// <summary>One line of the monthly report: a payment or refund, from the console, PayPal or both.</summary>
public sealed record ReportLine(string Kind, DateTime DateUtc, string Organization, string Source, string? ConsoleReference, string? PayPalId,
    decimal? ConsoleAmount, decimal? PayPalAmount, string Currency, ReportMatch Match, string? Note)
{
    public bool IsDifference => Match is ReportMatch.AmountDiffers or ReportMatch.OnlyInConsole or ReportMatch.OnlyAtPayPal;
}

/// <summary>The month's totals in one currency.</summary>
public sealed record ReportTotals(string Currency, decimal ConsoleReceived, decimal ConsoleRefunded, decimal PayPalReceived, decimal PayPalRefunded, decimal OfflineReceived, decimal OfflineRefunded)
{
    public bool PayPalEqual => ConsoleReceived == PayPalReceived && ConsoleRefunded == PayPalRefunded;
}

public sealed record MonthlyReport(int Year, int Month, DateTime FromUtc, DateTime ToUtc, bool PayPalChecked, string? PayPalError,
    IReadOnlyList<ReportTotals> Totals, IReadOnlyList<ReportLine> Lines, int OtherPayPalTransactions)
{
    public IReadOnlyList<ReportLine> Differences => Lines.Where(l => l.IsDifference).ToList();
    public string Label => new DateTime(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);
}

/// <summary>
/// The monthly reconciliation report (Phase 4 step 4.3, P4-3): the console's PayPal payments and
/// refunds for a calendar month (UTC) against PayPal's transaction search for the same month,
/// matched by PayPal's ids (sale, capture, refund). Differences are listed; offline payments are
/// shown apart; a CSV is exported for accounting. Read only.
/// </summary>
public sealed class MonthlyBillingReport
{
    private readonly IInvoiceStore _store;
    private readonly IInvoiceProvider _paypal;

    public MonthlyBillingReport(IInvoiceStore store, IInvoiceProvider paypal)
    {
        _store = store;
        _paypal = paypal;
    }

    public async Task<MonthlyReport> BuildAsync(int year, int month, CancellationToken ct = default)
    {
        var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);
        var payments = await _store.PaymentsBetweenAsync(from, to, ct).ConfigureAwait(false);
        var refunds = await _store.RefundsBetweenAsync(from, to, ct).ConfigureAwait(false);

        IReadOnlyList<PayPalTransaction> transactions = Array.Empty<PayPalTransaction>();
        string? error = null;
        if (!_paypal.IsConfigured) error = "PayPal is not configured on this site: only the console's side is shown.";
        else
        {
            try
            {
                transactions = await _paypal.SearchTransactionsAsync(from, to, ct).ConfigureAwait(false);
            }
            catch (PaymentProviderException e)
            {
                error = $"PayPal could not be asked ({e.Message}). Only the console's side is shown; try again later.";
            }
        }

        var counted = transactions.Where(t => t.Status is not "D").ToList();
        var paypalPayments = counted.Where(t => t.Amount > 0 && t.EventCode?.StartsWith("T00", StringComparison.Ordinal) == true).ToDictionary(t => t.Id);
        var paypalRefunds = counted.Where(t => t.Amount < 0 && t.EventCode is "T1107" or "T1105" or "T1108").ToDictionary(t => t.Id);
        var paypalInfo = counted.Where(t => t.Amount < 0 && t.EventCode?.StartsWith("T11", StringComparison.Ordinal) == true && !paypalRefunds.ContainsKey(t.Id)).ToList();
        var other = transactions.Count - paypalPayments.Count - paypalRefunds.Count - paypalInfo.Count;
        var checkedAtPayPal = error == null;

        var lines = new List<ReportLine>();
        foreach (var (payment, tenant) in payments)
        {
            if (payment.Source == PaymentSource.Offline)
            {
                lines.Add(new ReportLine("Payment", payment.PaidUtc, tenant, payment.Method?.ToString() ?? "Offline", payment.ExternalId, null, payment.Amount, null, payment.Currency, ReportMatch.Offline, null));
                continue;
            }
            paypalPayments.Remove(payment.ExternalId ?? "", out var match);
            lines.Add(new ReportLine("Payment", payment.PaidUtc, tenant, "PayPal", payment.ExternalId, match?.Id, payment.Amount, match?.Amount, payment.Currency,
                !checkedAtPayPal ? ReportMatch.OnlyInConsole : match == null ? ReportMatch.OnlyInConsole : match.Amount == payment.Amount ? ReportMatch.Matched : ReportMatch.AmountDiffers,
                !checkedAtPayPal ? "PayPal not checked" : match == null ? "Not at PayPal this month: check the neighbouring month, or PayPal's site" : match.Status == "P" ? "Pending at PayPal" : null));
        }
        foreach (var (refund, payment, tenant) in refunds)
        {
            if (payment.Source == PaymentSource.Offline)
            {
                lines.Add(new ReportLine("Refund", refund.RefundedUtc, tenant, payment.Method?.ToString() ?? "Offline", payment.ExternalId, null, refund.Amount, null, refund.Currency, ReportMatch.Offline, refund.Reason));
                continue;
            }
            PayPalTransaction? match = null;
            if (refund.ExternalRefundId != null) paypalRefunds.Remove(refund.ExternalRefundId, out match);
            var paypalAmount = match == null ? (decimal?)null : -match.Amount;
            lines.Add(new ReportLine("Refund", refund.RefundedUtc, tenant, "PayPal", refund.ExternalRefundId ?? payment.ExternalId, match?.Id, refund.Amount, paypalAmount, refund.Currency,
                !checkedAtPayPal || match == null ? ReportMatch.OnlyInConsole : paypalAmount == refund.Amount ? ReportMatch.Matched : ReportMatch.AmountDiffers,
                !checkedAtPayPal ? "PayPal not checked"
                    : refund.ExternalRefundId == null ? "Recorded without a PayPal refund id (before refunds were recorded one by one)"
                    : match == null ? (refund.Status == RefundStatus.Failed ? "Failed at PayPal" : "Not at PayPal this month") : null));
        }
        foreach (var t in paypalPayments.Values)
            lines.Add(new ReportLine("Payment", t.DateUtc, "", "PayPal", null, t.Id, null, t.Amount, t.Currency, ReportMatch.OnlyAtPayPal, $"Not recorded in the console (PayPal code {t.EventCode}{(t.InvoiceId == null ? "" : $", invoice {t.InvoiceId}")})"));
        foreach (var t in paypalRefunds.Values)
            lines.Add(new ReportLine("Refund", t.DateUtc, "", "PayPal", null, t.Id, null, -t.Amount, t.Currency, ReportMatch.OnlyAtPayPal, $"Not recorded in the console (PayPal code {t.EventCode})"));
        foreach (var t in paypalInfo)
            lines.Add(new ReportLine("Reversal", t.DateUtc, "", "PayPal", null, t.Id, null, -t.Amount, t.Currency, ReportMatch.Info, $"PayPal code {t.EventCode} (chargeback or reversal)"));

        var currencies = lines.Select(l => l.Currency).Distinct().OrderBy(c => c);
        var totals = currencies.Select(c =>
        {
            var inCurrency = lines.Where(l => l.Currency == c).ToList();
            decimal Sum(string kind, bool offline, Func<ReportLine, decimal?> amount) =>
                inCurrency.Where(l => l.Kind == kind && (l.Match == ReportMatch.Offline) == offline).Sum(l => amount(l) ?? 0);
            return new ReportTotals(c,
                Sum("Payment", false, l => l.ConsoleAmount), Sum("Refund", false, l => l.ConsoleAmount),
                Sum("Payment", false, l => l.PayPalAmount), Sum("Refund", false, l => l.PayPalAmount),
                Sum("Payment", true, l => l.ConsoleAmount), Sum("Refund", true, l => l.ConsoleAmount));
        }).ToList();

        return new MonthlyReport(year, month, from, to, checkedAtPayPal, error, totals,
            lines.OrderBy(l => l.IsDifference ? 0 : 1).ThenBy(l => l.DateUtc).ToList(), other);
    }

    /// <summary>The report as CSV for accounting (UTF-8 with BOM; text cells protected against spreadsheet formulas).</summary>
    public static byte[] Csv(MonthlyReport report)
    {
        var csv = new StringBuilder("Kind,Date (UTC),Organization,Source,Console reference,PayPal id,Console amount,PayPal amount,Currency,Result,Note\r\n");
        foreach (var l in report.Lines)
        {
            csv.AppendJoin(',',
                AuditLogService.Cell(l.Kind), AuditLogService.Cell(l.DateUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), AuditLogService.Cell(l.Organization),
                AuditLogService.Cell(l.Source), AuditLogService.Cell(l.ConsoleReference), AuditLogService.Cell(l.PayPalId),
                Number(l.ConsoleAmount), Number(l.PayPalAmount), AuditLogService.Cell(l.Currency), AuditLogService.Cell(l.Match.ToString()), AuditLogService.Cell(l.Note));
            csv.Append("\r\n");
        }
        foreach (var t in report.Totals)
        {
            csv.Append($"Total,,,,,,,,{t.Currency},,\"Console PayPal received {Number(t.ConsoleReceived)}, refunded {Number(t.ConsoleRefunded)}; PayPal received {Number(t.PayPalReceived)}, refunded {Number(t.PayPalRefunded)}; offline received {Number(t.OfflineReceived)}, refunded {Number(t.OfflineRefunded)}\"\r\n");
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    // Amounts are numbers we format ourselves (never text from outside), so they are written as they are.
    private static string Number(decimal? amount) => amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
}
