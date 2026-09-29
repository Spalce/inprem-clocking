using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Models;

// One logout-settings row per tenant. See multi-tenancy.md.
[Index(nameof(TenantId), IsUnique = true)]
public class Setting
{
    [Key]
    public int Id { get; set; }

    public int TenantId { get; set; }

    public bool Action { get; set; }
    [Display(Name = "Duration (in hours)")]
    public int Duration { get; set; }
    [Display(Name = "Setting Caption")]
    public string? Name { get; set; }
}
