namespace Inprem.Platform.Domain.Billing;

public enum PaymentSource
{
    /// <summary>A PayPal subscription payment (sale).</summary>
    PayPal,

    /// <summary>Recorded by the provider (bank transfer, cheque, cash; Phase 4).</summary>
    Offline,
}

public enum PaymentStatus
{
    Completed,
    Refunded,
    Reversed,
}

/// <summary>Money received from an organization (Phase 2 step 2.1). Recorded once per PayPal sale id.</summary>
public sealed class Payment
{
    private Payment()
    {
        Currency = "";
        RecordedBy = "";
    }

    public Guid Id { get; private set; }
    public int TenantId { get; private set; }
    public Guid? SubscriptionId { get; private set; }

    /// <summary>The invoice it pays (Phase 4), or null for a subscription payment.</summary>
    public Guid? InvoiceId { get; private set; }
    public PaymentSource Source { get; private set; }

    /// <summary>How an offline payment was made (null for PayPal, and for payments moved from the old billing code).</summary>
    public OfflinePaymentMethod? Method { get; private set; }

    /// <summary>PayPal sale id (unique), or the offline reference.</summary>
    public string? ExternalId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTime PaidUtc { get; private set; }
    public DateTime? RefundedUtc { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string RecordedBy { get; private set; }

    public static Payment Record(int tenantId, Guid? subscriptionId, PaymentSource source, string? externalId, decimal amount, string currency, DateTime paidUtc, string recordedBy,
        Guid? invoiceId = null, OfflinePaymentMethod? method = null)
    {
        if (source == PaymentSource.PayPal && method != null) throw new ArgumentException("A PayPal payment has no offline method.", nameof(method));
        if (paidUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(paidUtc));
        if (amount <= 0 || decimal.Round(amount, 2) != amount) throw new ArgumentOutOfRangeException(nameof(amount), "A positive amount with at most two decimals.");
        var code = (currency ?? "").Trim().ToUpperInvariant();
        if (code.Length != 3) throw new ArgumentException("A 3-letter currency code.", nameof(currency));
        if (source == PaymentSource.PayPal && string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("A PayPal payment needs its sale id.", nameof(externalId));
        if (externalId?.Length > 100) throw new ArgumentException("At most 100 characters.", nameof(externalId));
        return new Payment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SubscriptionId = subscriptionId, InvoiceId = invoiceId, Source = source, ExternalId = externalId, Method = method,
            Amount = amount, Currency = code, Status = PaymentStatus.Completed, PaidUtc = paidUtc, RecordedBy = recordedBy,
        };
    }

    /// <summary>
    /// The refunded total from the payment's refunds (Phase 4: one <see cref="PaymentRefund"/> per refund,
    /// failed ones excluded). Returns false when nothing changed.
    /// </summary>
    public bool SetRefundedTotal(decimal total, DateTime nowUtc)
    {
        if (total < 0 || total > Amount) throw new ArgumentOutOfRangeException(nameof(total), "From 0 to the payment's amount.");
        if (Status == PaymentStatus.Reversed || RefundedAmount == total) return false;
        RefundedAmount = total;
        Status = total > 0 ? PaymentStatus.Refunded : PaymentStatus.Completed;
        RefundedUtc = total > 0 ? nowUtc : null;
        return true;
    }

    /// <summary>PAYMENT.SALE.REVERSED (chargeback). Returns false when already reversed.</summary>
    public bool Reverse(DateTime nowUtc)
    {
        if (Status == PaymentStatus.Reversed) return false;
        Status = PaymentStatus.Reversed;
        RefundedAmount = Amount;
        RefundedUtc = nowUtc;
        return true;
    }
}
