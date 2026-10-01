using System.ComponentModel.DataAnnotations;

namespace InpremClockingApp.Models.Billing;

// A single billing period's invoice for a tenant (see multi-tenancy.md Part 2, decision #7).
// Generated manually by a SuperAdmin, or later by the Phase 8 renewal job - never by a payment
// gateway today, since none is integrated yet. InvoiceNumber generation and the
// generate/mark-paid/void workflow arrive with Phase 8's BillingService; this phase only adds
// the table.
public class Invoice
{
    [Key]
    public int Id { get; set; }

    public int TenantId { get; set; }

    public int SubscriptionId { get; set; }

    [Required]
    [StringLength(30)]
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    [Range(0, double.MaxValue)]
    public decimal Amount { get; set; }

    [StringLength(3)]
    public string Currency { get; set; } = "USD";

    public DateTime IssuedDate { get; set; }

    public DateTime DueDate { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public DateTime? PaidDate { get; set; }

    [StringLength(100)]
    public string? PaymentMethod { get; set; }

    [StringLength(100)]
    public string? PaymentReference { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }
}
