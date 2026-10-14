using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Tests.Unit.Provider;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Billing;

/// <summary>Phase 1 step 1.3: plan and price rules, and the plan service (one audit entry per change).</summary>
public class PlanTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private const string Actor = "provider:test";

    private sealed class FakePlanStore : IPlanStore
    {
        public List<Plan> Plans { get; } = new();
        public List<AuditEntry> Audit { get; } = new();
        public int Saves { get; private set; }

        public Task<IReadOnlyList<Plan>> ListAsync(string? search, PlanStatus? status, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Plan>>(Plans.Where(p => status == null || p.Status == status)
                .Where(p => search == null || p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || p.Code.Contains(search)).ToList());

        public Task<Plan?> FindAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Plans.FirstOrDefault(p => p.Id == id));
        public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) => Task.FromResult(Plans.Any(p => p.Code == code));
        public Task<IReadOnlyDictionary<Guid, int>> CountSubscriptionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int>());
        public Task AddAsync(Plan plan, CancellationToken ct = default) { Plans.Add(plan); return Task.CompletedTask; }
        public Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) { Audit.Add(entry); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
    }

    private static (PlanService Service, FakePlanStore Store) Service()
    {
        var store = new FakePlanStore();
        return (new PlanService(store, new FakeClock { UtcNow = Now }), store);
    }

    private static PlanInput Input(string name = "Standard", int trial = 30) => new(name, "For most centres", "Kiosk\nReports", 100, trial, true);

    [Theory]
    [InlineData("standard", true)]
    [InlineData("plus-2026", true)]
    [InlineData("Standard", false)]
    [InlineData("a", false)]
    [InlineData("two--dashes", false)]
    [InlineData("-start", false)]
    [InlineData("has space", false)]
    public void Plan_Code_Format(string code, bool valid) => Assert.Equal(valid, Plan.IsValidCode(code));

    [Fact]
    public void Plan_Refuses_Invalid_Values()
    {
        Assert.Throws<ArgumentException>(() => Plan.Create("ok", " ", null, null, null, 30, true, Now));
        Assert.Throws<ArgumentException>(() => Plan.Create("ok", new string('x', 101), null, null, null, 30, true, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan.Create("ok", "Name", null, null, null, -1, true, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan.Create("ok", "Name", null, null, null, 366, true, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan.Create("ok", "Name", null, null, 0, 30, true, Now));
        Assert.Throws<ArgumentException>(() => Plan.Create("BAD CODE", "Name", null, null, null, 30, true, Now));
        Assert.Throws<ArgumentException>(() => Plan.Create("ok", "Name", null, null, null, 30, true, DateTime.Now));
    }

    [Fact]
    public void Prices_Are_Fixed_One_Active_Per_Cycle_And_Retire_With_The_Plan()
    {
        var plan = Plan.Create("standard", "Standard", null, null, null, 30, true, Now);
        Assert.False(plan.IsOffered);

        var monthly = plan.AddPrice(BillingCycle.Monthly, 29.99m, "usd", Now);
        Assert.Equal(("USD", PlanStatus.Active), (monthly.Currency, monthly.Status));
        Assert.True(plan.IsOffered);
        Assert.Throws<InvalidOperationException>(() => plan.AddPrice(BillingCycle.Monthly, 39m, "USD", Now));
        plan.AddPrice(BillingCycle.Yearly, 299m, "USD", Now);

        var other = Plan.Create("other", "Other", null, null, null, 30, true, Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => other.AddPrice(BillingCycle.Yearly, 0m, "USD", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => other.AddPrice(BillingCycle.Yearly, 100_000.01m, "USD", Now));
        Assert.Throws<ArgumentException>(() => other.AddPrice(BillingCycle.Yearly, 1.234m, "USD", Now));
        Assert.Throws<ArgumentException>(() => other.AddPrice(BillingCycle.Yearly, 10m, "EUR", Now));
        Assert.Empty(other.Prices);

        monthly.Retire();
        plan.AddPrice(BillingCycle.Monthly, 39m, "USD", Now); // a new amount is a new price
        Assert.Equal(3, plan.Prices.Count);

        plan.Retire();
        Assert.All(plan.Prices, p => Assert.Equal(PlanStatus.Retired, p.Status));
        Assert.False(plan.IsOffered);
        Assert.Throws<InvalidOperationException>(() => plan.AddPrice(BillingCycle.Monthly, 39m, "USD", Now));
        plan.Reactivate();
        Assert.Equal(PlanStatus.Active, plan.Status);
        Assert.False(plan.IsOffered); // offered again only once it has an active price
    }

    [Fact]
    public async Task Service_Every_Change_Writes_One_Audit_Entry()
    {
        var (service, store) = Service();
        var created = await service.CreateAsync("standard", Input(), BillingCycle.Monthly, 29.99m, "USD", Actor);
        Assert.True(created.Succeeded, created.Error);
        var id = created.PlanId!.Value;

        Assert.True((await service.UpdateAsync(id, Input("Standard plan", 14), Actor)).Succeeded);
        Assert.True((await service.AddPriceAsync(id, BillingCycle.Yearly, 299m, "USD", Actor)).Succeeded);
        var yearly = store.Plans[0].Prices.Single(p => p.Cycle == BillingCycle.Yearly).Id;
        Assert.True((await service.RetirePriceAsync(id, yearly, Actor)).Succeeded);
        Assert.True((await service.RetireAsync(id, Actor)).Succeeded);
        Assert.True((await service.ReactivateAsync(id, Actor)).Succeeded);

        Assert.Equal(new[] { "plan.created", "plan.updated", "plan.price-added", "plan.price-retired", "plan.retired", "plan.reactivated" }, store.Audit.Select(a => a.Action));
        Assert.All(store.Audit, a => Assert.Equal((Actor, id.ToString()), (a.Actor, a.Target)));
        Assert.Equal(6, store.Saves);
        Assert.Equal(("Standard plan", 14), (store.Plans[0].Name, store.Plans[0].TrialDays));
    }

    [Fact]
    public async Task Service_Refusals_Change_Nothing_And_Write_No_Audit()
    {
        var (service, store) = Service();
        var id = (await service.CreateAsync("standard", Input(), BillingCycle.Monthly, 29.99m, "USD", Actor)).PlanId!.Value;
        store.Audit.Clear();

        Assert.Contains("already used", (await service.CreateAsync("standard", Input(), BillingCycle.Monthly, 10m, "USD", Actor)).Error);
        Assert.Contains("lower-case", (await service.CreateAsync("Bad Code", Input(), BillingCycle.Monthly, 10m, "USD", Actor)).Error);
        Assert.Equal("The amount must be more than 0 and at most 100,000.", (await service.CreateAsync("other", Input(), BillingCycle.Monthly, 0m, "USD", Actor)).Error);
        Assert.Contains("already has an active monthly price", (await service.AddPriceAsync(id, BillingCycle.Monthly, 39m, "USD", Actor)).Error);
        Assert.Equal("No such plan.", (await service.UpdateAsync(Guid.NewGuid(), Input(), Actor)).Error);
        Assert.Equal("No such price on this plan.", (await service.RetirePriceAsync(id, Guid.NewGuid(), Actor)).Error);
        Assert.Equal("The plan is already active.", (await service.ReactivateAsync(id, Actor)).Error);
        await service.RetireAsync(id, Actor);
        store.Audit.Clear();
        Assert.Equal("The plan is already retired.", (await service.RetireAsync(id, Actor)).Error);

        Assert.Empty(store.Audit);
        Assert.Single(store.Plans);
    }

    [Fact]
    public async Task Service_Offers_Only_Active_Plans_With_An_Active_Price()
    {
        var (service, store) = Service();
        var a = (await service.CreateAsync("a-plan", Input("A"), BillingCycle.Monthly, 10m, "USD", Actor)).PlanId!.Value;
        var b = (await service.CreateAsync("b-plan", Input("B"), BillingCycle.Monthly, 10m, "USD", Actor)).PlanId!.Value;
        var c = (await service.CreateAsync("c-plan", Input("C"), BillingCycle.Monthly, 10m, "USD", Actor)).PlanId!.Value;
        await service.RetireAsync(b, Actor);
        await service.RetirePriceAsync(c, store.Plans.Single(p => p.Id == c).Prices[0].Id, Actor);

        Assert.Equal(new[] { a }, (await service.ListOfferedAsync()).Select(p => p.Id));
    }
}
