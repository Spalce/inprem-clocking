namespace InpremClockingApp.Models;

public class StaffClockingVm
{
    public IEnumerable<ClockingStaff>? Clocking { get; set; }
    public IEnumerable<Staff>? Staff { get; set; }
    public ClockingStaff? ClockingStaff { get; set; }
}
