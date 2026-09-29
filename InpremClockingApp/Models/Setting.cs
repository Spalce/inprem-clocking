using System.ComponentModel.DataAnnotations;

namespace InpremClockingApp.Models;

public class Setting
{
    [Key]
    public int Id { get; set; }

    // Nullable for now (multi-tenancy Phase 0) - backfilled and made required in Phase 1.
    // See multi-tenancy.md.
    public int? TenantId { get; set; }

    public bool Action { get; set; }
    [Display(Name = "Duration (in hours)")]
    public int Duration { get; set; }
    [Display(Name = "Setting Caption")]
    public string? Name { get; set; }
}
