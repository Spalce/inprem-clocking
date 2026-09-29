using System.ComponentModel.DataAnnotations;

namespace InpremClockingApp.Models;

public class Tenant
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    // IANA timezone id (e.g. "America/New_York"), not a Windows id - see ITenantClock (Phase 3
    // of multi-tenancy.md), which resolves ClockDate/day-boundary math from this per tenant.
    [Required]
    [StringLength(100)]
    public string TimeZoneId { get; set; } = "America/New_York";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Shown on PDF reports and the Hours Worked page (multi-tenancy.md Phase 5 hardening) -
    // these used to be hardcoded to Inprem's own address/contact everywhere, which meant every
    // other tenant's official documents incorrectly showed Inprem's details. Nullable since not
    // every tenant will fill them in immediately.
    [StringLength(255)]
    public string? Address { get; set; }

    [StringLength(255)]
    public string? ContactInfo { get; set; }
}
