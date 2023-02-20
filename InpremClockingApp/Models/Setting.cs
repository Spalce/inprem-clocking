using System.ComponentModel.DataAnnotations;

namespace InpremClockingApp.Models;

public class Setting
{
    [Key]
    public int Id { get; set; }
    public bool Action { get; set; }
    [Display(Name = "Duration (in hours)")]
    public int Duration { get; set; }
    [Display(Name = "Setting Caption")]
    public string? Name { get; set; }
}
