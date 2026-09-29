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
}
