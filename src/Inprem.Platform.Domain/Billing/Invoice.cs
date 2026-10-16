using System.Text.RegularExpressions;

namespace Inprem.Platform.Domain.Billing;

public enum InvoiceSource
{
    /// <summary>A subscription period's invoice (Invoiced organizations): one per period.</summary>
    Subscription,

    /// <summary>A one-off PayPal invoice for a special case (setup, extra work, a balance).</summary>
    PayPalInvoice,

    /// <summary>Issued by the provider and paid offline (bank transfer, cash, cheque, mobile money); never sent through PayPal.</summary>
    Manual,
}

public enum InvoiceStatus
{
    Draft,
    Sent,
    PartiallyPaid,
    Paid,
    PartiallyRefunded,
    Refunded,
    Cancelled,
}

/// <summary>How an offline payment was made (Phase 4). PayPal has no mobile money method: it is sent as OTHER.</summary>
public enum OfflinePaymentMethod
{
    BankTransfer,
    Cash,
    Cheque,
    MobileMoney,
    Other,
}

/// <summary>One line of an invoice: what, how many, at what price (no tax: none is charged today).</summary>
public sealed class InvoiceLine
{
    public const int MaxDescriptionLength = 200; // PayPal's item name limit

    private InvoiceLine()
    {
        Description = "";
    }

    public int Position { get; private set; }
    public string Description { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitAmount { get; private set; }
    public decimal Amount => Quantity * UnitAmount;

    public static InvoiceLine Create(int position, string description, decimal quantity, decimal unitAmount)
    {
        var text = (description ?? "").Trim();
        if (text.Length == 0 || text.Length > MaxDescriptionLength) throw new ArgumentException($"A description of 1 to {MaxDescriptionLength} characters.", nameof(description));
        if (quantity <= 0 || quantity > 9999 || decimal.Round(quantity, 2) != quantity) throw new ArgumentOutOfRangeException(nameof(quantity), "A quantity above 0, at most 9999, with at most two decimals.");
        if (unitAmount < 0 || unitAmount > 1_000_000 || decimal.Round(unitAmount, 2) != unitAmount) throw new ArgumentOutOfRangeException(nameof(unitAmount), "A price from 0 to 1,000,000 with at most two decimals.");
        return new InvoiceLine { Position = position, Description = text, Quantity = quantity, UnitAmount = unitAmount };
    }
}

/// <summary>
/// An invoice to an organization (Phase 4 step 4.1). Its number is sequential and human-readable
/// (<c>INV-100001</c>, from a database sequence), and an organization has at most one invoice that
/// is not cancelled per subscription period (a unique index; the idea of the old billing code). The
/// amounts paid and refunded follow the payments recorded against it (PayPal or offline), and the
/// status follows the amounts.
/// </summary>
public sealed partial class Invoice
{
    public const int MaxLines = 100; // PayPal's limit
    public const int MaxNotesLength = 1000;

    private readonly List<InvoiceLine> _lines = new();

    private Invoice()
    {
        Number = "";
        Currency = "";
        CreatedBy = "";
    }

    public Guid Id { get; private set; }
    public int TenantId { get; private set; }
    public Guid? SubscriptionId { get; private set; }
    public InvoiceSource Source { get; private set; }
    public string Number { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public string Currency { get; private set; }
    public decimal Total { get; private set; }
    public decimal PaidAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public DateOnly IssueDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public DateOnly? PeriodStart { get; private set; }
    public DateOnly? PeriodEnd { get; private set; }

    /// <summary>The note printed on the invoice for the organization.</summary>
    public string? Notes { get; private set; }

    /// <summary>PayPal's invoice id (<c>INV2-…</c>); null for Manual invoices and before creation at PayPal.</summary>
    public string? ExternalInvoiceId { get; private set; }

    /// <summary>The page where the organization pays (PayPal's payer view).</summary>
    public string? PaymentUrl { get; private set; }

    public DateTime CreatedUtc { get; private set; }
    public string CreatedBy { get; private set; }
    public DateTime? SentUtc { get; private set; }
    public DateTime? PaidUtc { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public string? CancelReason { get; private set; }

    public IReadOnlyList<InvoiceLine> Lines => _lines;

    /// <summary>What is still to pay.</summary>
    public decimal Outstanding => Status is InvoiceStatus.Cancelled or InvoiceStatus.Draft ? 0 : Math.Max(0, Total - PaidAmount);

    public static string FormatNumber(long sequence) => sequence > 0 ? $"INV-{sequence:D6}" : throw new ArgumentOutOfRangeException(nameof(sequence));

    public static Invoice Create(
        int tenantId, Guid? subscriptionId, InvoiceSource source, string number, string currency,
        IEnumerable<(string Description, decimal Quantity, decimal UnitAmount)> lines,
        DateOnly issueDate, DateOnly dueDate, DateOnly? periodStart, DateOnly? periodEnd,
        string? notes, string createdBy, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if (tenantId <= 0) throw new ArgumentOutOfRangeException(nameof(tenantId));
        if (!NumberFormat().IsMatch(number ?? "")) throw new ArgumentException("An invoice number like INV-100001.", nameof(number));
        var code = (currency ?? "").Trim().ToUpperInvariant();
        if (code.Length != 3) throw new ArgumentException("A 3-letter currency code.", nameof(currency));
        if (dueDate < issueDate) throw new ArgumentException("The due date cannot be before the issue date.", nameof(dueDate));
        if (periodStart.HasValue != periodEnd.HasValue || periodEnd <= periodStart) throw new ArgumentException("A period needs a start and a later end.", nameof(periodEnd));
        if (source == InvoiceSource.Subscription && (subscriptionId == null || periodStart == null))
            throw new ArgumentException("A subscription invoice belongs to a subscription and a period.", nameof(source));
        var text = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (text?.Length > MaxNotesLength) throw new ArgumentException($"Notes of at most {MaxNotesLength} characters.", nameof(notes));
        if (string.IsNullOrWhiteSpace(createdBy)) throw new ArgumentException("Who created it.", nameof(createdBy));

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SubscriptionId = subscriptionId, Source = source, Number = number!, Status = InvoiceStatus.Draft,
            Currency = code, IssueDate = issueDate, DueDate = dueDate, PeriodStart = periodStart, PeriodEnd = periodEnd, Notes = text,
            CreatedUtc = nowUtc, CreatedBy = createdBy,
        };
        foreach (var (description, quantity, unitAmount) in lines ?? throw new ArgumentNullException(nameof(lines)))
            invoice._lines.Add(InvoiceLine.Create(invoice._lines.Count + 1, description, quantity, unitAmount));
        if (invoice._lines.Count is 0 or > MaxLines) throw new ArgumentException($"1 to {MaxLines} lines.", nameof(lines));
        invoice.Total = invoice._lines.Sum(l => l.Amount);
        if (invoice.Total <= 0) throw new ArgumentException("The total must be more than 0.", nameof(lines));
        return invoice;
    }

    /// <summary>The draft exists at PayPal. Returns false when it was already recorded (same id).</summary>
    public bool CreatedAtPayPal(string externalInvoiceId)
    {
        if (Source == InvoiceSource.Manual) throw new InvalidOperationException("A manual invoice is not sent through PayPal.");
        if (string.IsNullOrWhiteSpace(externalInvoiceId) || externalInvoiceId.Length > 50) throw new ArgumentException("PayPal's invoice id.", nameof(externalInvoiceId));
        if (ExternalInvoiceId == externalInvoiceId) return false;
        if (ExternalInvoiceId != null) throw new InvalidOperationException("The invoice already has another PayPal id.");
        if (Status != InvoiceStatus.Draft) throw new InvalidOperationException("Only a draft is created at PayPal.");
        ExternalInvoiceId = externalInvoiceId;
        return true;
    }

    /// <summary>Sent to the organization (by PayPal with its payment page, or issued by hand). Returns false when already sent.</summary>
    public bool MarkSent(string? paymentUrl, DateTime nowUtc)
    {
        if (Status == InvoiceStatus.Cancelled) throw new InvalidOperationException("A cancelled invoice cannot be sent.");
        if (Status != InvoiceStatus.Draft) return false;
        if (Source != InvoiceSource.Manual)
        {
            if (ExternalInvoiceId == null) throw new InvalidOperationException("Create the invoice at PayPal first.");
            if (!Uri.TryCreate(paymentUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || paymentUrl!.Length > 2000)
                throw new ArgumentException("PayPal's https payment page.", nameof(paymentUrl));
            PaymentUrl = paymentUrl;
        }
        Status = InvoiceStatus.Sent;
        SentUtc = nowUtc;
        return true;
    }

    /// <summary>
    /// The totals of the payments and refunds recorded against the invoice (ours, matched to PayPal's).
    /// The status follows: Sent, PartiallyPaid, Paid, PartiallyRefunded, Refunded. Returns false when nothing changed.
    /// </summary>
    public bool ApplyAmounts(decimal paid, decimal refunded, DateTime nowUtc)
    {
        if (paid < 0 || refunded < 0 || refunded > paid) throw new ArgumentOutOfRangeException(nameof(refunded), "Amounts from 0, refunds at most what was paid.");
        if (Status == InvoiceStatus.Draft) throw new InvalidOperationException("Send the invoice before recording payments.");
        if (Status == InvoiceStatus.Cancelled) throw new InvalidOperationException("The invoice is cancelled.");

        var status = refunded > 0 ? (refunded == paid ? InvoiceStatus.Refunded : InvoiceStatus.PartiallyRefunded)
            : paid >= Total ? InvoiceStatus.Paid
            : paid > 0 ? InvoiceStatus.PartiallyPaid
            : InvoiceStatus.Sent;
        if (status == Status && paid == PaidAmount && refunded == RefundedAmount) return false;
        if (paid >= Total && PaidUtc == null) PaidUtc = nowUtc;
        PaidAmount = paid;
        RefundedAmount = refunded;
        Status = status;
        return true;
    }

    /// <summary>Cancels an invoice nothing was paid on. Returns false when already cancelled.</summary>
    public bool Cancel(string reason, DateTime nowUtc)
    {
        if (Status == InvoiceStatus.Cancelled) return false;
        if (PaidAmount > 0) throw new InvalidOperationException("An invoice with payments cannot be cancelled: refund instead.");
        var text = (reason ?? "").Trim();
        if (text.Length is 0 or > 500) throw new ArgumentException("A reason of 1 to 500 characters.", nameof(reason));
        Status = InvoiceStatus.Cancelled;
        CancelReason = text;
        CancelledUtc = nowUtc;
        return true;
    }

    [GeneratedRegex(@"^INV-\d{6,12}$")]
    private static partial Regex NumberFormat();
}
