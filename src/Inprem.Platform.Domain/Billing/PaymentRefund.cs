namespace Inprem.Platform.Domain.Billing;

public enum RefundStatus
{
    /// <summary>PayPal accepted the refund but has not finished it (e.g. an eCheck payment).</summary>
    Pending,
    Completed,
    Failed,
}

/// <summary>
/// One refund of a payment (Phase 4 step 4.2): money given back through PayPal, or returned offline
/// and recorded. Kept one row per refund, so partial refunds add up and PayPal's repeated or late
/// messages about the same refund (its id) change nothing. The payment's refunded total is the sum
/// of its refunds that are not failed.
/// </summary>
public sealed class PaymentRefund
{
    public const int MaxReasonLength = 300;

    private PaymentRefund()
    {
        Currency = "";
        Reason = "";
        RecordedBy = "";
    }

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public int TenantId { get; private set; }

    /// <summary>PayPal's refund id (unique), or null for a refund returned offline.</summary>
    public string? ExternalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public RefundStatus Status { get; private set; }
    public string Reason { get; private set; }
    public DateTime RefundedUtc { get; private set; }
    public string RecordedBy { get; private set; }

    /// <summary>A refund of <paramref name="payment"/>; at most what has not been refunded yet.</summary>
    public static PaymentRefund Record(Payment payment, string? externalRefundId, decimal amount, RefundStatus status, string reason, DateTime nowUtc, string recordedBy)
    {
        if (payment.Status == PaymentStatus.Reversed) throw new InvalidOperationException("The payment was reversed (a chargeback): nothing is left to refund.");
        if (amount <= 0 || decimal.Round(amount, 2) != amount) throw new ArgumentOutOfRangeException(nameof(amount), "A positive amount with at most two decimals.");
        if (amount > payment.Amount - payment.RefundedAmount) throw new ArgumentOutOfRangeException(nameof(amount), "More than is left to refund.");
        if (externalRefundId?.Length > 100) throw new ArgumentException("At most 100 characters.", nameof(externalRefundId));
        var text = (reason ?? "").Trim();
        if (text.Length is 0 or > MaxReasonLength) throw new ArgumentException($"A reason of 1 to {MaxReasonLength} characters.", nameof(reason));
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        return new PaymentRefund
        {
            Id = Guid.NewGuid(), PaymentId = payment.Id, TenantId = payment.TenantId, ExternalRefundId = string.IsNullOrWhiteSpace(externalRefundId) ? null : externalRefundId,
            Amount = amount, Currency = payment.Currency, Status = status, Reason = text, RefundedUtc = nowUtc, RecordedBy = recordedBy,
        };
    }

    /// <summary>PayPal finished (or failed) a pending refund. Returns false when nothing changed.</summary>
    public bool SetStatus(RefundStatus status)
    {
        if (Status == status) return false;
        if (Status == RefundStatus.Failed) throw new InvalidOperationException("A failed refund stays failed.");
        Status = status;
        return true;
    }
}
