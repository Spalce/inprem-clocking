namespace InpremClockingApp.Models;

public class VolunteerClockingVm
{
    public IEnumerable<Clocking>? Clocking { get; set; }
    public IEnumerable<Volunteer>? Volunteer { get; set; }
    public Clocking? ClockingVolunteer { get; set; }
}
