using System.Collections.Concurrent;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>
/// A pretend PayPal Invoicing for the test hosts (Phase 4): invoices live in memory; tests pay or
/// refund them as a payer would (<see cref="PayByPayPal"/>), and can make PayPal fail (<see cref="FailNext"/>).
/// </summary>
public sealed class FakeInvoiceProvider : IInvoiceProvider
{
    private int _next;
    private readonly ConcurrentDictionary<string, Entry> _invoices = new();

    public sealed class Entry
    {
        public required NewPayPalInvoice Request { get; init; }
        public required string Id { get; init; }
        public PayPalInvoiceState State { get; set; } = PayPalInvoiceState.Draft;
        public List<PayPalInvoicePayment> Payments { get; } = new();
        public List<PayPalInvoiceRefund> Refunds { get; } = new();
        public decimal Total => Request.Lines.Sum(l => l.Quantity * l.UnitAmount);
    }

    public bool IsConfigured { get; set; } = true;

    /// <summary>The next call fails as if PayPal were unreachable.</summary>
    public bool FailNext { get; set; }

    public ConcurrentQueue<(RefundTarget Target, string TransactionId, decimal Amount, string RequestId)> MoneyRefunds { get; } = new();

    public IReadOnlyCollection<Entry> All => _invoices.Values.ToList();

    /// <summary>What PayPal's transaction search returns (tests add to it).</summary>
    public ConcurrentQueue<PayPalTransaction> Transactions { get; } = new();

    public Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<PayPalTransaction>>(Transactions.Where(t => t.DateUtc >= fromUtc && t.DateUtc < toUtc).ToList());
    }

    /// <summary>A refund made at PayPal on an invoice payment (e.g. from PayPal's site); returns the refund id.</summary>
    public string RefundAtPayPal(string invoiceId, decimal amount)
    {
        var entry = _invoices[invoiceId];
        var id = "RF-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        entry.Refunds.Add(new PayPalInvoiceRefund(id, false, DateOnly.FromDateTime(DateTime.UtcNow), amount, entry.Request.Currency));
        entry.State = Paid(entry);
        return id;
    }

    public Entry Get(string id) => _invoices[id];

    public Task<string> CreateDraftAsync(NewPayPalInvoice invoice, CancellationToken ct = default)
    {
        Check();
        var existing = _invoices.Values.FirstOrDefault(e => e.Request.Number == invoice.Number);
        if (existing != null) return Task.FromResult(existing.Id);
        var id = $"INV2-TEST-{Interlocked.Increment(ref _next):D4}-{Guid.NewGuid().ToString("N")[..9].ToUpperInvariant()}";
        _invoices[id] = new Entry { Request = invoice, Id = id };
        return Task.FromResult(id);
    }

    public Task<string> SendAsync(string invoiceId, CancellationToken ct = default)
    {
        Check();
        var entry = _invoices[invoiceId];
        if (entry.State == PayPalInvoiceState.Draft) entry.State = PayPalInvoiceState.Sent;
        return Task.FromResult(PayerUrl(invoiceId));
    }

    public Task<PayPalInvoiceInfo?> GetAsync(string invoiceId, CancellationToken ct = default)
    {
        Check();
        if (!_invoices.TryGetValue(invoiceId, out var e)) return Task.FromResult<PayPalInvoiceInfo?>(null);
        return Task.FromResult<PayPalInvoiceInfo?>(new PayPalInvoiceInfo(
            e.Id, e.Request.Number, e.State, e.State.ToString().ToUpperInvariant(), e.Total, e.Request.Currency,
            e.Payments.Sum(p => p.Amount), e.Refunds.Sum(r => r.Amount), e.Request.DueDate, PayerUrl(e.Id), e.Payments.ToList(), e.Refunds.ToList()));
    }

    public Task CancelAsync(string invoiceId, string note, CancellationToken ct = default)
    {
        Check();
        var entry = _invoices[invoiceId];
        if (entry.State == PayPalInvoiceState.Draft) _invoices.TryRemove(invoiceId, out _);
        else entry.State = PayPalInvoiceState.Cancelled;
        return Task.CompletedTask;
    }

    public Task<string> RecordExternalPaymentAsync(string invoiceId, DateOnly date, OfflinePaymentMethod method, decimal amount, string currency, string? note, CancellationToken ct = default)
    {
        Check();
        var entry = _invoices[invoiceId];
        var id = "EXTR-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        entry.Payments.Add(new PayPalInvoicePayment(id, true, date, amount, currency, PayPalMethod(method)));
        entry.State = Paid(entry);
        return Task.FromResult(id);
    }

    public Task<string> RecordExternalRefundAsync(string invoiceId, DateOnly date, OfflinePaymentMethod method, decimal amount, string currency, CancellationToken ct = default)
    {
        Check();
        var entry = _invoices[invoiceId];
        var id = "EXTR-R-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        entry.Refunds.Add(new PayPalInvoiceRefund(id, true, date, amount, currency));
        entry.State = Paid(entry);
        return Task.FromResult(id);
    }

    public Task<ProviderRefund> RefundAsync(RefundTarget target, string transactionId, decimal amount, string currency, string requestId, string? note, CancellationToken ct = default)
    {
        Check();
        if (!MoneyRefunds.Any(r => r.RequestId == requestId)) MoneyRefunds.Enqueue((target, transactionId, amount, requestId));
        return Task.FromResult(new ProviderRefund("REF-" + requestId.GetHashCode().ToString("X8"), "COMPLETED"));
    }

    /// <summary>The payer pays the whole invoice at PayPal; returns the capture id.</summary>
    public string PayByPayPal(string invoiceId, DateOnly? date = null)
    {
        var entry = _invoices[invoiceId];
        var capture = "CAP-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        entry.Payments.Add(new PayPalInvoicePayment(capture, false, date ?? DateOnly.FromDateTime(DateTime.UtcNow), entry.Total - entry.Payments.Sum(p => p.Amount), entry.Request.Currency, "PAYPAL"));
        entry.State = Paid(entry);
        return capture;
    }

    private static PayPalInvoiceState Paid(Entry e)
    {
        var paid = e.Payments.Sum(p => p.Amount);
        var refunded = e.Refunds.Sum(r => r.Amount);
        return refunded > 0 ? (refunded >= paid ? PayPalInvoiceState.Refunded : PayPalInvoiceState.PartiallyRefunded)
            : paid >= e.Total ? PayPalInvoiceState.Paid : paid > 0 ? PayPalInvoiceState.PartiallyPaid : PayPalInvoiceState.Sent;
    }

    private static string PayPalMethod(OfflinePaymentMethod method) => method switch
    {
        OfflinePaymentMethod.BankTransfer => "BANK_TRANSFER",
        OfflinePaymentMethod.Cash => "CASH",
        OfflinePaymentMethod.Cheque => "CHECK",
        _ => "OTHER",
    };

    private static string PayerUrl(string id) => $"https://www.sandbox.paypal.com/invoice/p/#{id}";

    private void Check()
    {
        if (!IsConfigured) throw new PaymentProviderException("PayPal is not configured (PayPal:ClientId and PayPal:Secret).");
        if (FailNext)
        {
            FailNext = false;
            throw new PaymentProviderException("PayPal did not answer (test).");
        }
    }
}
