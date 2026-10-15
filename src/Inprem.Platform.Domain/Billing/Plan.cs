using System.Text.RegularExpressions;

namespace Inprem.Platform.Domain.Billing;

public enum PlanStatus
{
    Active,

    /// <summary>Not offered any more; organizations already on it keep it.</summary>
    Retired,
}

public enum BillingCycle
{
    Monthly,
    Yearly,
}

/// <summary>
/// A plan offered to organizations (Phase 1 step 1.3, design 8). Prices are separate
/// (<see cref="PlanPrice"/>) and fixed once created: a new amount is a new price.
/// </summary>
public sealed class Plan
{
    public const int MaxTrialDays = 365;
    private static readonly Regex CodePattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    private readonly List<PlanPrice> _prices = new();

    private Plan()
    {
        Code = "";
        Name = "";
    }

    public Guid Id { get; private set; }

    /// <summary>Stable lower-case identifier used by the website and the API, e.g. "standard".</summary>
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>One feature per line, shown on the website's pricing page.</summary>
    public string? Features { get; private set; }

    /// <summary>Most staff and volunteers together the plan allows; null = no limit.</summary>
    public int? MaxPeople { get; private set; }

    /// <summary>Free trial length for new organizations on this plan (D21: 30 by default).</summary>
    public int TrialDays { get; private set; }

    /// <summary>Shown on the public pricing page (otherwise only the console can assign it).</summary>
    public bool IsPublic { get; private set; }
    public PlanStatus Status { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    /// <summary>The PayPal catalog product for this plan (created once, Phase 2 step 2.3).</summary>
    public string? PayPalProductId { get; private set; }

    public IReadOnlyList<PlanPrice> Prices => _prices;

    public static Plan Create(string code, string name, string? description, string? features, int? maxPeople, int trialDays, bool isPublic, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        var normalized = (code ?? "").Trim().ToLowerInvariant();
        if (!IsValidCode(normalized)) throw new ArgumentException("Use lower-case letters, digits and single dashes (e.g. standard, plus-2026).", nameof(code));

        var plan = new Plan { Id = Guid.NewGuid(), Code = normalized, Status = PlanStatus.Active, CreatedUtc = nowUtc };
        plan.Update(name, description, features, maxPeople, trialDays, isPublic);
        return plan;
    }

    public static bool IsValidCode(string code) => code.Length is >= 2 and <= 50 && CodePattern.IsMatch(code);

    /// <summary>Changes what the plan says; the code and prices are not changed here.</summary>
    public void Update(string name, string? description, string? features, int? maxPeople, int trialDays, bool isPublic)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) throw new ArgumentException("A plan needs a name (up to 100 characters).", nameof(name));
        if (trialDays is < 0 or > MaxTrialDays) throw new ArgumentOutOfRangeException(nameof(trialDays), $"Trial length must be 0 to {MaxTrialDays} days.");
        if (maxPeople is <= 0) throw new ArgumentOutOfRangeException(nameof(maxPeople), "The people limit must be positive, or empty for no limit.");

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Features = string.IsNullOrWhiteSpace(features) ? null : features.Trim();
        MaxPeople = maxPeople;
        TrialDays = trialDays;
        IsPublic = isPublic;
    }

    /// <summary>Adds a price. At most one active price per billing cycle: retire the old one first.</summary>
    public PlanPrice AddPrice(BillingCycle cycle, decimal amount, string currency, DateTime nowUtc)
    {
        if (Status == PlanStatus.Retired) throw new InvalidOperationException("A retired plan cannot get new prices.");
        if (_prices.Any(p => p.Cycle == cycle && p.Status == PlanStatus.Active))
            throw new InvalidOperationException($"The plan already has an active {cycle.ToString().ToLowerInvariant()} price. Retire it first.");
        var price = PlanPrice.Create(Id, cycle, amount, currency, nowUtc);
        _prices.Add(price);
        return price;
    }

    /// <summary>No new organizations on this plan; its prices retire with it.</summary>
    public void Retire()
    {
        Status = PlanStatus.Retired;
        foreach (var price in _prices) price.Retire();
    }

    /// <summary>Offered again (its retired prices stay retired; add new ones).</summary>
    public void Reactivate() => Status = PlanStatus.Active;

    /// <summary>Records the PayPal product (once; it never changes).</summary>
    public void LinkPayPalProduct(string productId)
    {
        if (string.IsNullOrWhiteSpace(productId) || productId.Length > 50) throw new ArgumentException("PayPal product id required (at most 50 characters).", nameof(productId));
        if (PayPalProductId != null && PayPalProductId != productId) throw new InvalidOperationException("This plan is already linked to another PayPal product.");
        PayPalProductId = productId;
    }

    public bool IsOffered => Status == PlanStatus.Active && _prices.Any(p => p.Status == PlanStatus.Active);
}

/// <summary>One price of a plan: amount, currency and cycle, fixed once created.</summary>
public sealed class PlanPrice
{
    private PlanPrice()
    {
        Currency = "";
    }

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public BillingCycle Cycle { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>ISO 4217 code; USD only for now (D25).</summary>
    public string Currency { get; private set; }

    /// <summary>The matching PayPal billing plan; set in Phase 2.</summary>
    public string? PayPalPlanId { get; private set; }
    public PlanStatus Status { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    public static readonly string[] SupportedCurrencies = { "USD" };

    internal static PlanPrice Create(Guid planId, BillingCycle cycle, decimal amount, string currency, DateTime nowUtc)
    {
        if (amount <= 0 || amount > 100_000) throw new ArgumentOutOfRangeException(nameof(amount), "The amount must be more than 0 and at most 100,000.");
        if (decimal.Round(amount, 2) != amount) throw new ArgumentException("Use at most two decimals.", nameof(amount));
        var code = (currency ?? "").Trim().ToUpperInvariant();
        if (!SupportedCurrencies.Contains(code)) throw new ArgumentException($"Currency must be one of: {string.Join(", ", SupportedCurrencies)}.", nameof(currency));
        return new PlanPrice { Id = Guid.NewGuid(), PlanId = planId, Cycle = cycle, Amount = amount, Currency = code, Status = PlanStatus.Active, CreatedUtc = nowUtc };
    }

    public void Retire() => Status = PlanStatus.Retired;

    /// <summary>Records the PayPal billing plan for this price (once: a price never changes, so neither does its plan).</summary>
    public void LinkPayPalPlan(string payPalPlanId)
    {
        if (string.IsNullOrWhiteSpace(payPalPlanId) || payPalPlanId.Length > 50) throw new ArgumentException("PayPal plan id required (at most 50 characters).", nameof(payPalPlanId));
        if (PayPalPlanId != null && PayPalPlanId != payPalPlanId) throw new InvalidOperationException("This price is already linked to another PayPal plan.");
        PayPalPlanId = payPalPlanId;
    }
}
