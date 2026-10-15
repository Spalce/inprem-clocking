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
    public PaymentSource Source { get; private set; }

    /// <summary>PayPal sale id (unique), or the offline reference.</summary>
    public string? ExternalId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTime PaidUtc { get; private set; }
    public DateTime? RefundedUtc { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public string RecordedBy { get; private set; }

    public static Payment Record(int tenantId, Guid? subscriptionId, PaymentSource source, string? externalId, decimal amount, string currency, DateTime paidUtc, string recordedBy)
    {
        if (paidUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(paidUtc));
        if (amount <= 0 || decimal.Round(amount, 2) != amount) throw new ArgumentOutOfRangeException(nameof(amount), "A positive amount with at most two decimals.");
        var code = (currency ?? "").Trim().ToUpperInvariant();
        if (code.Length != 3) throw new ArgumentException("A 3-letter currency code.", nameof(currency));
        if (source == PaymentSource.PayPal && string.IsNullOrWhiteSpace(externalId)) throw new ArgumentException("A PayPal payment needs its sale id.", nameof(externalId));
        if (externalId?.Length > 100) throw new ArgumentException("At most 100 characters.", nameof(externalId));
        return new Payment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SubscriptionId = subscriptionId, Source = source, ExternalId = externalId,
            Amount = amount, Currency = code, Status = PaymentStatus.Completed, PaidUtc = paidUtc, RecordedBy = recordedBy,
        };
    }

    /// <summary>PAYMENT.SALE.REFUNDED (fully or partly). Returns false when nothing changed.</summary>
    public bool Refund(decimal amount, DateTime nowUtc)
    {
        if (amount <= 0 || amount > Amount) throw new ArgumentOutOfRangeException(nameof(amount), "More than 0 and at most the payment.");
        if (Status == PaymentStatus.Reversed || (Status == PaymentStatus.Refunded && RefundedAmount == amount)) return false;
        Status = PaymentStatus.Refunded;
        RefundedAmount = amount;
        RefundedUtc = nowUtc;
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
