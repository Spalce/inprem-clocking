using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Models.Billing;

// One subscription per tenant (see multi-tenancy.md Part 2, decision #7) - the unique index on
// TenantId enforces that cardinality the same way Setting.cs does for its one-row-per-tenant
// shape. Amount is a flat recurring fee set directly per tenant; there's no Plan catalog yet -
// decision #9 documents how per-seat/tiered pricing would be added later without reshaping this
// table. PaymentGateway/ExternalSubscriptionId stay null for every row until a real gateway
// (e.g. Stripe) is integrated - manual billing today never sets them.
[Index(nameof(TenantId), IsUnique = true)]
public class Subscription
{
    [Key]
    public int Id { get; set; }

    public int TenantId { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Amount { get; set; }

    [StringLength(3)]
    public string Currency { get; set; } = "USD";

    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

    public DateTime CurrentPeriodStart { get; set; }

    public DateTime CurrentPeriodEnd { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(30)]
    public string? PaymentGateway { get; set; }

    [StringLength(100)]
    public string? ExternalSubscriptionId { get; set; }
}
