using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

public interface IPlanStore
{
    Task<IReadOnlyList<Plan>> ListAsync(string? search, PlanStatus? status, CancellationToken ct = default);
    Task<Plan?> FindAsync(Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);

    /// <summary>Organizations (subscriptions) on each plan; filled from Phase 1 step 1.2.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountSubscriptionsAsync(CancellationToken ct = default);

    Task AddAsync(Plan plan, CancellationToken ct = default);
    Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default);

    /// <summary>Saves the plan change and its audit entry together.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record PlanInput(string Name, string? Description, string? Features, int? MaxPeople, int TrialDays, bool IsPublic);

/// <summary>Result of a plan change: the plan's id, or a message for the person.</summary>
public sealed record PlanResult(bool Succeeded, Guid? PlanId, string? Error)
{
    public static PlanResult Ok(Guid id) => new(true, id, null);

    public static PlanResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// Plans and prices (Phase 1 step 1.3). Local only: nothing is sent to PayPal until Phase 2.
/// Every change writes exactly one audit entry, saved together with the change (P1-4).
/// </summary>
public sealed class PlanService
{
    public const int DefaultTrialDays = 30; // D21

    private readonly IPlanStore _store;
    private readonly IClock _clock;

    public PlanService(IPlanStore store, IClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public Task<IReadOnlyList<Plan>> ListAsync(string? search, PlanStatus? status, CancellationToken ct = default) => _store.ListAsync(search, status, ct);

    public Task<Plan?> FindAsync(Guid id, CancellationToken ct = default) => _store.FindAsync(id, ct);

    public Task<IReadOnlyDictionary<Guid, int>> CountSubscriptionsAsync(CancellationToken ct = default) => _store.CountSubscriptionsAsync(ct);

    /// <summary>Plans that can be chosen for a new subscription, with their active prices.</summary>
    public async Task<IReadOnlyList<Plan>> ListOfferedAsync(CancellationToken ct = default) =>
        (await _store.ListAsync(null, PlanStatus.Active, ct).ConfigureAwait(false)).Where(p => p.IsOffered).ToList();

    public async Task<PlanResult> CreateAsync(string code, PlanInput input, BillingCycle cycle, decimal amount, string currency, string actor, CancellationToken ct = default)
    {
        var normalized = (code ?? "").Trim().ToLowerInvariant();
        if (await _store.CodeExistsAsync(normalized, ct).ConfigureAwait(false)) return PlanResult.Fail($"The code \"{normalized}\" is already used by another plan.");

        Plan plan;
        try
        {
            plan = Plan.Create(normalized, input.Name, input.Description, input.Features, input.MaxPeople, input.TrialDays, input.IsPublic, _clock.UtcNow);
            plan.AddPrice(cycle, amount, currency, _clock.UtcNow);
        }
        catch (ArgumentException e)
        {
            return PlanResult.Fail(Message(e));
        }

        await _store.AddAsync(plan, ct).ConfigureAwait(false);
        await AuditAsync(actor, "plan.created", plan, $"{plan.Name} ({plan.Code}): {Describe(plan.Prices[0])}, trial {plan.TrialDays} days", ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return PlanResult.Ok(plan.Id);
    }

    public Task<PlanResult> UpdateAsync(Guid id, PlanInput input, string actor, CancellationToken ct = default) =>
        ChangeAsync(id, actor, "plan.updated", plan =>
        {
            plan.Update(input.Name, input.Description, input.Features, input.MaxPeople, input.TrialDays, input.IsPublic);
            return $"{plan.Name}: trial {plan.TrialDays} days, people limit {plan.MaxPeople?.ToString() ?? "none"}, {(plan.IsPublic ? "public" : "console only")}";
        }, ct);

    public Task<PlanResult> AddPriceAsync(Guid id, BillingCycle cycle, decimal amount, string currency, string actor, CancellationToken ct = default) =>
        ChangeAsync(id, actor, "plan.price-added", plan => $"{plan.Name}: {Describe(plan.AddPrice(cycle, amount, currency, _clock.UtcNow))}", ct);

    public Task<PlanResult> RetirePriceAsync(Guid id, Guid priceId, string actor, CancellationToken ct = default) =>
        ChangeAsync(id, actor, "plan.price-retired", plan =>
        {
            var price = plan.Prices.FirstOrDefault(p => p.Id == priceId) ?? throw new ArgumentException("No such price on this plan.");
            if (price.Status == PlanStatus.Retired) throw new InvalidOperationException("That price is already retired.");
            price.Retire();
            return $"{plan.Name}: {Describe(price)}";
        }, ct);

    public Task<PlanResult> RetireAsync(Guid id, string actor, CancellationToken ct = default) =>
        ChangeAsync(id, actor, "plan.retired", plan =>
        {
            if (plan.Status == PlanStatus.Retired) throw new InvalidOperationException("The plan is already retired.");
            plan.Retire();
            return plan.Name;
        }, ct);

    public Task<PlanResult> ReactivateAsync(Guid id, string actor, CancellationToken ct = default) =>
        ChangeAsync(id, actor, "plan.reactivated", plan =>
        {
            if (plan.Status == PlanStatus.Active) throw new InvalidOperationException("The plan is already active.");
            plan.Reactivate();
            return plan.Name;
        }, ct);

    private async Task<PlanResult> ChangeAsync(Guid id, string actor, string action, Func<Plan, string> change, CancellationToken ct)
    {
        var plan = await _store.FindAsync(id, ct).ConfigureAwait(false);
        if (plan == null) return PlanResult.Fail("No such plan.");

        string details;
        try
        {
            details = change(plan);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return PlanResult.Fail(Message(e));
        }

        await AuditAsync(actor, action, plan, details, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return PlanResult.Ok(plan.Id);
    }

    private Task AuditAsync(string actor, string action, Plan plan, string details, CancellationToken ct) =>
        _store.AddAuditAsync(AuditEntry.Record(actor, action, plan.Id.ToString(), details, _clock.UtcNow), ct);

    public static string Describe(PlanPrice price) =>
        $"{price.Amount:0.00} {price.Currency} {(price.Cycle == BillingCycle.Monthly ? "per month" : "per year")}";

    // ArgumentException adds " (Parameter 'x')" to its message; people need only the sentence.
    private static string Message(Exception e) =>
        e is ArgumentException a && a.ParamName != null ? a.Message.Replace($" (Parameter '{a.ParamName}')", "") : e.Message;
}
