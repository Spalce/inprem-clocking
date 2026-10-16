using Inprem.Platform.Domain.Billing;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Billing;

/// <summary>Phase 4 step 4.2: one row per refund; the payment's refunded total follows its refunds.</summary>
public class PaymentRefundTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private static Payment Paid(decimal amount = 29m) => Payment.Record(7, null, PaymentSource.PayPal, "SALE-1", amount, "USD", Now, "paypal:webhook");

    [Fact]
    public void A_Refund_Is_At_Most_What_Is_Left_And_Needs_A_Reason()
    {
        var payment = Paid();
        var refund = PaymentRefund.Record(payment, "REF-1", 10m, RefundStatus.Completed, " Duplicate charge ", Now, "provider:1");
        Assert.Equal((10m, "USD", "Duplicate charge", "REF-1", 7), (refund.Amount, refund.Currency, refund.Reason, refund.ExternalRefundId, refund.TenantId));

        payment.SetRefundedTotal(10m, Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentRefund.Record(payment, null, 19.01m, RefundStatus.Completed, "x", Now, "p"));
        Assert.NotNull(PaymentRefund.Record(payment, null, 19m, RefundStatus.Completed, "x", Now, "p"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentRefund.Record(payment, null, 0m, RefundStatus.Completed, "x", Now, "p"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaymentRefund.Record(payment, null, 1.005m, RefundStatus.Completed, "x", Now, "p"));
        Assert.Throws<ArgumentException>(() => PaymentRefund.Record(payment, null, 1m, RefundStatus.Completed, "  ", Now, "p"));
        Assert.Null(PaymentRefund.Record(payment, " ", 1m, RefundStatus.Completed, "offline", Now, "p").ExternalRefundId);
    }

    [Fact]
    public void A_Reversed_Payment_Cannot_Be_Refunded()
    {
        var payment = Paid();
        payment.Reverse(Now);
        Assert.Throws<InvalidOperationException>(() => PaymentRefund.Record(payment, null, 1m, RefundStatus.Completed, "x", Now, "p"));
        Assert.False(payment.SetRefundedTotal(5m, Now)); // a chargeback stays a chargeback
    }

    [Fact]
    public void The_Refunded_Total_And_Status_Follow_The_Refunds()
    {
        var payment = Paid();
        Assert.True(payment.SetRefundedTotal(10m, Now));
        Assert.Equal((PaymentStatus.Refunded, 10m, Now), (payment.Status, payment.RefundedAmount, payment.RefundedUtc));
        Assert.False(payment.SetRefundedTotal(10m, Now));
        Assert.True(payment.SetRefundedTotal(15m, Now.AddHours(1)));
        Assert.Equal(15m, payment.RefundedAmount);
        Assert.True(payment.SetRefundedTotal(0m, Now)); // the only refund failed
        Assert.Equal((PaymentStatus.Completed, null), (payment.Status, payment.RefundedUtc));
        Assert.Throws<ArgumentOutOfRangeException>(() => payment.SetRefundedTotal(29.01m, Now));
    }

    [Fact]
    public void A_Pending_Refund_Completes_Or_Fails_And_A_Failed_One_Stays_Failed()
    {
        var refund = PaymentRefund.Record(Paid(), "REF-2", 5m, RefundStatus.Pending, "x", Now, "p");
        Assert.True(refund.SetStatus(RefundStatus.Completed));
        Assert.False(refund.SetStatus(RefundStatus.Completed));
        Assert.True(refund.SetStatus(RefundStatus.Failed));
        Assert.Throws<InvalidOperationException>(() => refund.SetStatus(RefundStatus.Completed));
    }
}
