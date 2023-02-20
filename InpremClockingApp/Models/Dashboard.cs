namespace InpremClockingApp.Models;

public class Dashboard
{
    public int StaffCount { get; set; }
    public int VolunteerCount { get; set; }
    public int AdminCount { get; set; }
    public string? StaffClocking { get; set; }
    public string? VolunteerClocking { get; set; }
}
