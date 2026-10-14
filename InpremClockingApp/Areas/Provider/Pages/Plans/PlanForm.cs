using System.ComponentModel.DataAnnotations;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;

namespace InpremClockingApp.Areas.Provider.Pages.Plans;

/// <summary>What a person fills in for a plan (create and edit).</summary>
public sealed class PlanForm
{
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [StringLength(1000)] public string? Description { get; set; }
    [StringLength(2000)] public string? Features { get; set; }
    [Range(1, 1_000_000)] public int? MaxPeople { get; set; }
    [Range(0, Plan.MaxTrialDays)] public int TrialDays { get; set; } = PlanService.DefaultTrialDays;
    public bool IsPublic { get; set; } = true;

    public PlanInput ToInput() => new(Name, Description, Features, MaxPeople, TrialDays, IsPublic);

    public static PlanForm From(Plan plan) => new()
    {
        Name = plan.Name, Description = plan.Description, Features = plan.Features,
        MaxPeople = plan.MaxPeople, TrialDays = plan.TrialDays, IsPublic = plan.IsPublic,
    };
}

/// <summary>A new price: cycle, amount, currency (USD, D25).</summary>
public sealed class PriceForm
{
    public BillingCycle Cycle { get; set; } = BillingCycle.Monthly;
    [Range(0.01, 100_000)] public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
}
