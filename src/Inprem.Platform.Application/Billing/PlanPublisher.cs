using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Billing;

namespace Inprem.Platform.Application.Billing;

/// <summary>
/// "Publish to PayPal" (Phase 2 step 2.3): a PayPal product for the plan (once) and a PayPal
/// billing plan for every active price that has none. A price never changes, so a new amount
/// is a new price and gets a new PayPal plan; organizations on the old price are moved to it
/// deliberately (plan change, step 2.4), never automatically.
/// </summary>
/// <remarks>
/// Each PayPal id is saved as soon as PayPal returns it, and every request carries a request id
/// derived from our own id: if publishing stops halfway, publishing again finishes the rest
/// without creating anything twice. One audit entry per publish (published or failed).
/// </remarks>
public sealed class PlanPublisher
{
    private readonly IPlanStore _store;
    private readonly IPaymentProvider _paypal;
    private readonly IClock _clock;

    public PlanPublisher(IPlanStore store, IPaymentProvider paypal, IClock clock)
    {
        _store = store;
        _paypal = paypal;
        _clock = clock;
    }

    public bool IsAvailable => _paypal.IsConfigured;

    /// <summary>True if the plan is offered and has an active price without a PayPal plan.</summary>
    public static bool NeedsPublishing(Plan plan) =>
        plan.Status == PlanStatus.Active && plan.Prices.Any(p => p.Status == PlanStatus.Active && p.PayPalPlanId == null);

    public async Task<PlanResult> PublishAsync(Guid planId, string actor, CancellationToken ct = default)
    {
        if (!_paypal.IsConfigured) return PlanResult.Fail("PayPal is not configured on this site (PayPal:ClientId and PayPal:Secret).");
        var plan = await _store.FindAsync(planId, ct).ConfigureAwait(false);
        if (plan == null) return PlanResult.Fail("No such plan.");
        if (plan.Status != PlanStatus.Active) return PlanResult.Fail("A retired plan is not published. Offer it again first.");
        var pending = plan.Prices.Where(p => p.Status == PlanStatus.Active && p.PayPalPlanId == null).ToList();
        if (pending.Count == 0) return PlanResult.Fail("Every active price of this plan is already in PayPal.");

        var done = new List<string>();
        try
        {
            if (plan.PayPalProductId == null)
            {
                plan.LinkPayPalProduct(await _paypal.CreateProductAsync(plan.Name, plan.Description, $"inprem-product-{plan.Id:N}", ct).ConfigureAwait(false));
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
                done.Add($"product {plan.PayPalProductId}");
            }

            foreach (var price in pending)
            {
                var name = $"{plan.Name} ({(price.Cycle == BillingCycle.Monthly ? "monthly" : "yearly")})";
                price.LinkPayPalPlan(await _paypal.CreatePlanAsync(
                    new NewBillingPlan(plan.PayPalProductId!, name, plan.Description, price.Cycle, price.Amount, price.Currency), $"inprem-plan-{price.Id:N}", ct).ConfigureAwait(false));
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
                done.Add($"{PlanService.Describe(price)} as {price.PayPalPlanId}");
            }
        }
        catch (PaymentProviderException e)
        {
            await AuditAsync(actor, "plan.publish-failed", plan,
                $"{plan.Name}: {e.Message}{(e.DebugId == null ? "" : $" (PayPal debug id {e.DebugId})")}. Done before the failure: {(done.Count == 0 ? "nothing" : string.Join("; ", done))}", ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            return PlanResult.Fail($"{e.Message} What was already published is kept; publish again to finish.");
        }

        await AuditAsync(actor, "plan.published", plan, $"{plan.Name}: {string.Join("; ", done)}", ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return PlanResult.Ok(plan.Id);
    }

    private Task AuditAsync(string actor, string action, Plan plan, string details, CancellationToken ct) =>
        _store.AddAuditAsync(AuditEntry.Record(actor, action, plan.Id.ToString(), details, _clock.UtcNow), ct);
}
