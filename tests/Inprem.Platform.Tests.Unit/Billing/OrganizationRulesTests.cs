using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Organizations;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Billing;

/// <summary>Phase 1 step 1.2: address names (slugs), organization status, notes, starting subscriptions.</summary>
public class OrganizationRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("akim-asafo", null)]
    [InlineData("abc", null)]
    [InlineData("ab", "3 to 50")]
    [InlineData("Akim", "lower-case")]
    [InlineData("akim--asafo", "lower-case")]
    [InlineData("akim asafo", "lower-case")]
    [InlineData("admin", "reserved")]
    [InlineData("api", "reserved")]
    public void Slug_Rules(string slug, string? problem)
    {
        var result = TenantProfile.SlugProblem(slug);
        if (problem == null) Assert.Null(result);
        else Assert.Contains(problem, result);
    }

    [Theory]
    [InlineData("AKIM ASAFO SHS", "akim-asafo-shs")]
    [InlineData("  St. Mary's  Centre!! ", "st-mary-s-centre")]
    [InlineData("A", "a-org")]
    [InlineData("Admin", "admin-org")]
    public void Slug_Suggestion(string name, string expected) => Assert.Equal(expected, TenantProfile.Suggest(name));

    [Fact]
    public void Slug_Suggestion_Is_At_Most_50_Characters() =>
        Assert.True(TenantProfile.Suggest(new string('x', 80)).Length <= 50);

    [Fact]
    public void Profile_Status_Changes_Need_A_Reason_And_A_Real_Change()
    {
        var profile = TenantProfile.Create(5, "Org-Five", Now);
        Assert.Equal("org-five", profile.Slug);
        Assert.Throws<ArgumentException>(() => profile.ChangeSlug("no good"));

        Assert.Throws<InvalidOperationException>(() => profile.Reactivate("already active", Now));
        Assert.Throws<ArgumentException>(() => profile.Suspend(" ", Now));
        Assert.Throws<ArgumentException>(() => profile.Suspend(new string('x', 501), Now));
        profile.Suspend("  Unpaid  ", Now);
        Assert.Equal((OrganizationStatus.Suspended, "Unpaid", (DateTime?)Now), (profile.Status, profile.StatusReason, profile.StatusChangedUtc));
        Assert.Throws<InvalidOperationException>(() => profile.Suspend("again", Now));
        profile.Reactivate("Paid", Now);
        Assert.Equal(OrganizationStatus.Active, profile.Status);
        Assert.Throws<ArgumentException>(() => TenantProfile.Create(1, "ok-slug", DateTime.Now));
    }

    [Fact]
    public void Notes_Need_Text()
    {
        Assert.Throws<ArgumentException>(() => TenantNote.Create(1, "  ", "provider:x", Now));
        Assert.Throws<ArgumentException>(() => TenantNote.Create(1, new string('x', 2001), "provider:x", Now));
        Assert.Equal("Hello", TenantNote.Create(1, " Hello ", "provider:x", Now).Text);
    }

    private static (Plan Plan, PlanPrice Price) OfferedPlan()
    {
        var plan = Plan.Create("standard", "Standard", null, null, null, 30, true, Now);
        return (plan, plan.AddPrice(BillingCycle.Monthly, 29m, "USD", Now));
    }

    [Fact]
    public void Subscription_Starts_As_Designed()
    {
        var (plan, price) = OfferedPlan();

        var paypal = Subscription.Start(7, plan, price, BillingMode.PayPal, 14, Now);
        Assert.Equal((SubscriptionStatus.Trialing, Now.AddDays(14), (Guid?)plan.Id), (paypal.Status, paypal.TrialEndsUtc!.Value, paypal.PlanId));

        var invoiced = Subscription.Start(7, plan, price, BillingMode.Invoiced, 0, Now);
        Assert.Equal((SubscriptionStatus.Active, (DateTime?)null), (invoiced.Status, invoiced.TrialEndsUtc));

        var free = Subscription.Start(7, null, null, BillingMode.Free, 30, Now);
        Assert.Equal((SubscriptionStatus.Active, (DateTime?)null, (Guid?)null), (free.Status, free.TrialEndsUtc, free.PlanId)); // no trial for Free
    }

    [Fact]
    public void Subscription_Refuses_What_Cannot_Be_Billed()
    {
        var (plan, price) = OfferedPlan();
        Assert.Throws<ArgumentException>(() => Subscription.Start(7, null, null, BillingMode.PayPal, 14, Now));       // no plan
        Assert.Throws<ArgumentException>(() => Subscription.Start(7, plan, price, BillingMode.PayPal, 0, Now));       // PayPal needs a trial for now
        Assert.Throws<ArgumentOutOfRangeException>(() => Subscription.Start(7, plan, price, BillingMode.Invoiced, 400, Now));

        var other = Plan.Create("other", "Other", null, null, null, 30, true, Now);
        var otherPrice = other.AddPrice(BillingCycle.Monthly, 9m, "USD", Now);
        Assert.Throws<ArgumentException>(() => Subscription.Start(7, plan, otherPrice, BillingMode.Invoiced, 0, Now)); // price of another plan

        price.Retire();
        Assert.Throws<ArgumentException>(() => Subscription.Start(7, plan, price, BillingMode.Invoiced, 0, Now));      // plan no longer offered
    }
}
