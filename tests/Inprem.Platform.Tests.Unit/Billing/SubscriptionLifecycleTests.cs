using Inprem.Platform.Domain.Billing;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Billing;

/// <summary>
/// Phase 2 step 2.1: every transition of the subscription state diagram (design 8) and the
/// checkout-session state diagram (design 7.5), refusals for moves the diagrams do not have,
/// and idempotent repeats (the same PayPal news can arrive from confirm, webhook and reconciliation).
/// </summary>
public class SubscriptionLifecycleTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private const string PayPalId = "I-BW452GLLEP1G";

    private static (Plan Plan, PlanPrice Price) Catalog()
    {
        var plan = Plan.Create("standard", "Standard", null, null, 50, 30, true, Now);
        return (plan, plan.AddPrice(BillingCycle.Monthly, 29m, "USD", Now));
    }

    private static Subscription Trial(int days = 30)
    {
        var (plan, price) = Catalog();
        return Subscription.Start(7, plan, price, BillingMode.PayPal, days, Now);
    }

    private static Subscription ActiveOne()
    {
        var s = Trial();
        s.Activate(PayPalId, Catalog().Price, Now, Now.AddMonths(1), Now);
        return s;
    }

    // ------------------------------------------------------------ subscription

    [Fact]
    public void Trialing_To_PendingActivation_To_Active_Exactly_Once()
    {
        var s = Trial();
        var price = Catalog().Price;
        Assert.True(s.AwaitActivation(PayPalId, price, Now));
        Assert.False(s.AwaitActivation(PayPalId, price, Now)); // repeat: nothing changes
        Assert.Equal(SubscriptionStatus.PendingActivation, s.Status);

        Assert.True(s.Activate(PayPalId, price, Now, Now.AddMonths(1), Now.AddMinutes(1)));
        Assert.False(s.Activate(PayPalId, price, Now, Now.AddMonths(1), Now.AddMinutes(2))); // webhook after confirm
        Assert.Equal((SubscriptionStatus.Active, BillingMode.PayPal, PayPalId, price.Id), (s.Status, s.Mode, s.ExternalSubscriptionId, s.PlanPriceId));
        Assert.Equal(Now.AddMonths(1), s.CurrentPeriodEndUtc);
        Assert.Equal(Now.AddMinutes(1), s.StatusChangedUtc);
    }

    [Fact]
    public void Active_Cannot_Take_Another_PayPal_Subscription()
    {
        var s = ActiveOne();
        Assert.Throws<InvalidOperationException>(() => s.Activate("I-OTHER", Catalog().Price, null, null, Now));
        Assert.Throws<InvalidOperationException>(() => s.AwaitActivation("I-OTHER", Catalog().Price, Now));
    }

    [Fact]
    public void Trialing_To_Expired_When_The_Trial_Ends_And_Expired_Subscribes()
    {
        var s = Trial(10);
        Assert.False(s.ExpireTrialIfOver(Now.AddDays(9)));
        Assert.True(s.HasFullAccess(Now.AddDays(9)));
        Assert.Equal(1, s.TrialDaysLeft(Now.AddDays(9)));
        Assert.False(s.HasFullAccess(Now.AddDays(10))); // read-only at once, even before the job runs
        Assert.True(s.ExpireTrialIfOver(Now.AddDays(10)));
        Assert.False(s.ExpireTrialIfOver(Now.AddDays(11)));
        Assert.Equal(SubscriptionStatus.Expired, s.Status);

        Assert.True(s.AwaitActivation(PayPalId, Catalog().Price, Now.AddDays(12))); // EX -> PA "subscribes"
        Assert.True(s.Activate(PayPalId, Catalog().Price, null, null, Now.AddDays(12)));
    }

    [Fact]
    public void Active_To_PastDue_And_Back_On_Payment()
    {
        var s = ActiveOne();
        Assert.True(s.MarkPastDue(29m, Now.AddMonths(1)));
        Assert.False(s.MarkPastDue(29m, Now.AddMonths(1)));
        Assert.Equal((SubscriptionStatus.PastDue, 29m), (s.Status, s.OutstandingBalance));
        Assert.True(s.HasFullAccess(Now.AddMonths(1))); // PayPal is retrying

        Assert.True(s.RecordPayment(Now.AddMonths(1), Now.AddMonths(2), Now.AddMonths(1).AddDays(3)));
        Assert.Equal((SubscriptionStatus.Active, 0m, Now.AddMonths(2)), (s.Status, s.OutstandingBalance, s.CurrentPeriodEndUtc));
        Assert.False(s.RecordPayment(Now.AddMonths(2), Now.AddMonths(3), Now.AddMonths(2))); // a renewal: period only
        Assert.Equal(Now.AddMonths(3), s.CurrentPeriodEndUtc);
    }

    [Fact]
    public void PastDue_To_Suspended_Is_Read_Only_And_Reactivates()
    {
        var s = ActiveOne();
        s.MarkPastDue(29m, Now);
        Assert.True(s.Suspend(Now.AddDays(5)));
        Assert.False(s.Suspend(Now.AddDays(6)));
        Assert.False(s.HasFullAccess(Now.AddDays(5)));

        Assert.True(s.Activate(PayPalId, Catalog().Price, null, Now.AddMonths(2), Now.AddDays(7))); // balance paid + reactivated
        Assert.Equal((SubscriptionStatus.Active, 0m), (s.Status, s.OutstandingBalance));
    }

    [Fact]
    public void Active_To_Canceled_Keeps_Access_To_The_Period_End_Then_Closes_After_Retention()
    {
        var s = ActiveOne();
        var periodEnd = s.CurrentPeriodEndUtc!.Value;
        Assert.True(s.Cancel(Now.AddDays(3)));
        Assert.False(s.Cancel(Now.AddDays(4)));
        Assert.True(s.CancelAtPeriodEnd);
        Assert.True(s.HasFullAccess(periodEnd.AddSeconds(-1)));
        Assert.False(s.HasFullAccess(periodEnd));

        Assert.Equal(periodEnd.AddDays(90), s.ClosableAfterUtc());
        Assert.Throws<InvalidOperationException>(() => s.Close(periodEnd.AddDays(89)));
        Assert.True(s.Close(periodEnd.AddDays(90)));
        Assert.False(s.Close(periodEnd.AddDays(91)));
        Assert.Equal(SubscriptionStatus.Closed, s.Status);
        Assert.Throws<InvalidOperationException>(() => s.AwaitActivation(PayPalId, Catalog().Price, Now)); // closed is final
    }

    [Fact]
    public void Suspended_And_Expired_Close_After_Retention()
    {
        var suspended = ActiveOne();
        suspended.Suspend(Now);
        Assert.True(suspended.Close(Now.AddDays(90)));

        var expired = Trial(1);
        expired.ExpireTrialIfOver(Now.AddDays(1));
        Assert.Throws<InvalidOperationException>(() => expired.Close(Now.AddDays(2), retentionDays: 30));
        Assert.True(expired.Close(Now.AddDays(31), retentionDays: 30));

        Assert.Throws<InvalidOperationException>(() => ActiveOne().Close(Now.AddYears(1))); // active never closes
        Assert.Null(ActiveOne().ClosableAfterUtc());
    }

    [Fact]
    public void Canceled_Can_Resubscribe_And_PayPal_Expiry_Is_Read_Only()
    {
        var s = ActiveOne();
        s.Cancel(Now);
        Assert.True(s.Activate("I-NEW", Catalog().Price, Now, Now.AddMonths(1), Now.AddDays(1)));
        Assert.Equal(("I-NEW", false), (s.ExternalSubscriptionId, s.CancelAtPeriodEnd));

        Assert.True(s.ExpireSubscription(Now.AddYears(1)));
        Assert.False(s.ExpireSubscription(Now.AddYears(1)));
        Assert.False(s.HasFullAccess(Now.AddYears(1)));
    }

    [Fact]
    public void Moves_The_Diagram_Does_Not_Have_Are_Refused()
    {
        Assert.Throws<InvalidOperationException>(() => Trial().MarkPastDue(1m, Now));
        Assert.Throws<InvalidOperationException>(() => Trial().Suspend(Now));
        Assert.Throws<InvalidOperationException>(() => Trial().Cancel(Now));
        Assert.Throws<InvalidOperationException>(() => Trial().RecordPayment(null, null, Now));
        Assert.Throws<InvalidOperationException>(() => Trial().ExpireSubscription(Now));
        Assert.Throws<InvalidOperationException>(() => Trial().Close(Now.AddYears(1)));
        Assert.Throws<ArgumentException>(() => Trial().Activate(" ", Catalog().Price, null, null, Now));
        Assert.Throws<ArgumentException>(() => Trial().Activate(PayPalId, Catalog().Price, null, null, DateTime.Now));
        Assert.Throws<ArgumentException>(() => Trial().Activate(PayPalId, Catalog().Price, Now, Now.AddDays(-1), Now));
    }

    [Fact]
    public void A_Signup_Awaiting_Payment_Has_No_Access_Until_PayPal_Approves()
    {
        var (plan, price) = Catalog();
        var s = Subscription.StartAwaitingPayment(9, plan, price, Now);
        Assert.Equal((SubscriptionStatus.PendingActivation, BillingMode.PayPal, null), (s.Status, s.Mode, s.TrialEndsUtc));
        Assert.False(s.HasFullAccess(Now));
        Assert.True(s.AwaitActivation(PayPalId, price, Now));
        Assert.True(s.HasFullAccess(Now));
        Assert.True(s.Activate(PayPalId, price, Now, Now.AddMonths(1), Now));
        price.Retire();
        Assert.Throws<ArgumentException>(() => Subscription.StartAwaitingPayment(9, plan, price, Now));
    }

    [Fact]
    public void Free_Always_Has_Access_And_Console_Overrides_Work()
    {
        var free = Subscription.Start(1, null, null, BillingMode.Free, 0, Now);
        Assert.True(free.HasFullAccess(Now.AddYears(5)));

        var expired = Trial(1);
        expired.ExpireTrialIfOver(Now.AddDays(1));
        expired.GrantFreeAccess(Now.AddDays(10), Now.AddDays(1));
        Assert.True(expired.HasFullAccess(Now.AddDays(9)));
        Assert.False(expired.HasFullAccess(Now.AddDays(10)));
        expired.GrantFreeAccess(null, Now.AddDays(2));
        Assert.Throws<ArgumentException>(() => expired.GrantFreeAccess(Now, Now.AddDays(2)));

        expired.ExtendTrial(Now.AddDays(20), Now.AddDays(2)); // Expired -> Trialing again
        Assert.Equal((SubscriptionStatus.Trialing, Now.AddDays(20)), (expired.Status, expired.TrialEndsUtc));
        Assert.Throws<ArgumentException>(() => expired.ExtendTrial(Now, Now.AddDays(2)));
        Assert.Throws<InvalidOperationException>(() => ActiveOne().ExtendTrial(Now.AddDays(40), Now));

        var s = Trial();
        s.OverrideMaxPeople(200);
        Assert.Equal(200, s.MaxPeopleOverride);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.OverrideMaxPeople(0));
    }

    // ---------------------------------------------------------------- checkout

    private static CheckoutSession Checkout() =>
        CheckoutSession.Create(CheckoutPurpose.Convert, 7, null, Catalog().Price, "https://app.inprem.test/Billing/Return", "https://app.inprem.test/Billing", Guid.NewGuid().ToString(), "user:u1", Now);

    private static CheckoutSession Issued()
    {
        var c = Checkout();
        c.Issued(PayPalId, "P-5ML4271244454362WXNWU5NQ", "https://www.sandbox.paypal.com/webapps/billing/subscriptions?ba_token=BA-1");
        return c;
    }

    [Fact]
    public void Checkout_Created_AwaitingApproval_Approved_Active()
    {
        var c = Checkout();
        Assert.StartsWith("chk_", c.Reference);
        Assert.Equal(28, c.Reference.Length);
        Assert.NotEqual(c.Reference, Checkout().Reference);
        Assert.False(c.Apply(PayPalSubscriptionState.Active, Now)); // not issued yet: PayPal cannot have it

        c.Issued(PayPalId, "P-1", "https://www.sandbox.paypal.com/approve");
        Assert.Equal(CheckoutStatus.AwaitingApproval, c.Status);
        Assert.Throws<InvalidOperationException>(() => c.Issued(PayPalId, "P-1", "https://www.sandbox.paypal.com/approve"));
        Assert.False(c.Apply(PayPalSubscriptionState.ApprovalPending, Now));
        Assert.True(c.Apply(PayPalSubscriptionState.Approved, Now));
        Assert.False(c.Apply(PayPalSubscriptionState.Approved, Now));
        Assert.True(c.Apply(PayPalSubscriptionState.Active, Now.AddMinutes(1)));
        Assert.Equal((CheckoutStatus.Active, Now.AddMinutes(1)), (c.Status, c.CompletedUtc));

        Assert.False(c.Apply(PayPalSubscriptionState.Cancelled, Now)); // final
        Assert.False(c.Fail("late", Now));
    }

    [Fact]
    public void Checkout_AwaitingApproval_Straight_To_Active_Cancelled_Expired_Or_Failed()
    {
        var direct = Issued();
        Assert.True(direct.Apply(PayPalSubscriptionState.Active, Now));

        var cancelled = Issued();
        Assert.True(cancelled.CancelByBuyer(Now));
        Assert.Equal(CheckoutStatus.Cancelled, cancelled.Status);

        var timedOut = Issued();
        Assert.False(timedOut.ExpireIfOver(Now.AddHours(3).AddSeconds(-1)));
        Assert.True(timedOut.ExpireIfOver(Now.AddHours(3)));
        Assert.Equal(CheckoutStatus.Expired, timedOut.Status);

        var mismatch = Issued();
        Assert.True(mismatch.Fail("custom_id does not match", Now));
        Assert.Equal((CheckoutStatus.Failed, "custom_id does not match"), (mismatch.Status, mismatch.FailureReason));

        var approvedTooLong = Issued();
        approvedTooLong.Apply(PayPalSubscriptionState.Approved, Now);
        Assert.False(approvedTooLong.CancelByBuyer(Now)); // only before approval
        Assert.False(approvedTooLong.ExpireIfOver(Now.AddDays(1))); // approved: reconciliation decides
        Assert.True(approvedTooLong.Fail("Not ACTIVE after 24 hours", Now.AddDays(1)));

        var declined = Issued();
        Assert.True(declined.Apply(PayPalSubscriptionState.Suspended, Now));
        Assert.Equal(CheckoutStatus.Failed, declined.Status);
    }

    [Fact]
    public void Checkout_Checks_Its_Input()
    {
        var price = Catalog().Price;
        Assert.Throws<ArgumentException>(() => CheckoutSession.Create(CheckoutPurpose.Convert, 7, null, price, "http://app.test/r", "https://app.test/c", "k", "u", Now));
        Assert.NotNull(CheckoutSession.Create(CheckoutPurpose.Convert, 7, null, price, "http://127.0.0.1:5000/r", "http://localhost:5000/c", "k-loopback", "u", Now)); // this computer only
        Assert.Throws<ArgumentException>(() => CheckoutSession.Create(CheckoutPurpose.Convert, 7, null, price, "/relative", "https://app.test/c", "k", "u", Now));
        Assert.Throws<ArgumentException>(() => CheckoutSession.Create(CheckoutPurpose.Convert, null, null, price, "https://app.test/r", "https://app.test/c", "k", "u", Now));
        Assert.Throws<ArgumentException>(() => CheckoutSession.Create(CheckoutPurpose.New, 7, null, price, "https://app.test/r", "https://app.test/c", "k", "u", Now));
        Assert.Throws<ArgumentException>(() => CheckoutSession.Create(CheckoutPurpose.Convert, null, Guid.NewGuid(), price, "https://app.test/r", "https://app.test/c", "k", "u", Now));
        Assert.Throws<ArgumentException>(() => CheckoutSession.Create(CheckoutPurpose.Convert, 7, null, price, "https://app.test/r", "https://app.test/c", " ", "u", Now));
        price.Retire();
        Assert.Throws<ArgumentException>(() => CheckoutSession.Create(CheckoutPurpose.Convert, 7, null, price, "https://app.test/r", "https://app.test/c", "k", "u", Now));
        Assert.NotNull(CheckoutSession.Create(CheckoutPurpose.New, null, Guid.NewGuid(), Catalog().Price, "https://site.test/r", "https://site.test/c", "k", "signup:1", Now));
        Assert.NotNull(CheckoutSession.Create(CheckoutPurpose.New, 7, Guid.NewGuid(), Catalog().Price, "https://site.test/r", "https://site.test/c", "k", "signup:1", Now)); // the signup's organization
        Assert.Throws<ArgumentException>(() => CheckoutSession.Create(CheckoutPurpose.Convert, 7, Guid.NewGuid(), Catalog().Price, "https://site.test/r", "https://site.test/c", "k", "u", Now));
    }

    [Theory]
    [InlineData("APPROVAL_PENDING", PayPalSubscriptionState.ApprovalPending)]
    [InlineData("APPROVED", PayPalSubscriptionState.Approved)]
    [InlineData("ApprovalPending", PayPalSubscriptionState.ApprovalPending)]
    [InlineData("active", PayPalSubscriptionState.Active)]
    [InlineData("SUSPENDED", PayPalSubscriptionState.Suspended)]
    [InlineData("CANCELLED", PayPalSubscriptionState.Cancelled)]
    [InlineData("EXPIRED", PayPalSubscriptionState.Expired)]
    [InlineData("SOMETHING_NEW", PayPalSubscriptionState.Unknown)]
    [InlineData(null, PayPalSubscriptionState.Unknown)]
    public void PayPal_Status_Mapping(string? status, PayPalSubscriptionState expected) => Assert.Equal(expected, PayPalStatus.Parse(status));

    // ------------------------------------------------------- payments, events

    [Fact]
    public void Payment_Refund_And_Reversal()
    {
        var p = Payment.Record(7, null, PaymentSource.PayPal, "SALE-1", 29m, "usd", Now, "webhook");
        Assert.Equal(("USD", PaymentStatus.Completed), (p.Currency, p.Status));
        Assert.True(p.SetRefundedTotal(10m, Now)); // the total of its refunds (step 4.2: one row per refund)
        Assert.False(p.SetRefundedTotal(10m, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.SetRefundedTotal(30m, Now));
        Assert.True(p.Reverse(Now));
        Assert.False(p.Reverse(Now));
        Assert.Equal((PaymentStatus.Reversed, 29m), (p.Status, p.RefundedAmount));

        Assert.Throws<ArgumentException>(() => Payment.Record(7, null, PaymentSource.PayPal, null, 29m, "USD", Now, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment.Record(7, null, PaymentSource.Offline, null, 0m, "USD", Now, "x"));
        Assert.NotNull(Payment.Record(7, null, PaymentSource.Offline, null, 29.5m, "USD", Now, "provider:x"));
    }

    [Fact]
    public void Billing_Event_Attempts_And_Outcome()
    {
        var e = BillingEvent.Receive("WH-1", "BILLING.SUBSCRIPTION.ACTIVATED", "subscription", PayPalId, "{}", Now);
        Assert.Equal(BillingEventStatus.Received, e.Status);
        e.StartAttempt();
        e.Failed(new string('x', 3000));
        Assert.Equal((BillingEventStatus.Failed, 1, BillingEvent.MaxErrorLength), (e.Status, e.Attempts, e.Error!.Length));
        e.StartAttempt();
        e.Processed(Now.AddMinutes(1));
        Assert.Equal((BillingEventStatus.Processed, 2, null), (e.Status, e.Attempts, e.Error));
        Assert.Throws<ArgumentException>(() => BillingEvent.Receive(" ", "X", null, null, "{}", Now));
    }

    [Fact]
    public void PayPal_Links_Are_Set_Once()
    {
        var (plan, price) = Catalog();
        plan.LinkPayPalProduct("PROD-1");
        plan.LinkPayPalProduct("PROD-1");
        Assert.Throws<InvalidOperationException>(() => plan.LinkPayPalProduct("PROD-2"));
        price.LinkPayPalPlan("P-1");
        Assert.Throws<InvalidOperationException>(() => price.LinkPayPalPlan("P-2"));
        Assert.Equal(("PROD-1", "P-1"), (plan.PayPalProductId, price.PayPalPlanId));
    }
}
