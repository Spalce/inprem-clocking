using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace InpremClockingApp.Models;

// One session per staff member per calendar day, enforced at the database level.
[Index(nameof(StafId), nameof(ClockDate), IsUnique = true)]
public class ClockingStaff
{
    [Key]
    public long ClockingStaffId { get; set; }

    [ForeignKey("StafId")]
    public Staff? Staff { get; set; }
    public long StafId { get; set; }

    public string? FullName { get; set; }

    [DisplayName("Clock In Time")]
    // [DataType(DataType.Time)]
    public DateTime? ClockInTime { get; set; }

    [DisplayName("Clock Out Time")]
    // [DataType(DataType.Time)]
    public DateTime? ClockOutTime { get; set; }

    [DisplayName("Leave On Break Time")]
    // [DataType(DataType.Time)]
    public DateTime? LeaveOnBreakTime { get; set; }

    [DisplayName("Return from Break Time")]
    // [DataType(DataType.Time)]
    public DateTime? ReturnOnBreakTime { get; set; }

    [DisplayName("Working Hours")]
    public TimeSpan? WorkingHours { get; set; }

    [DataType(DataType.Date)]
    [DisplayName("Date")]
    public DateTime? CreatedAt { get; set; }

    // Org-local calendar day this session belongs to, set once at clock-in. Backed by a unique
    // index on (StafId, ClockDate) so at most one session per staff member per day can ever be
    // persisted, even under concurrent/duplicate clock-in requests.
    [Column(TypeName = "date")]
    [DisplayName("Clock Date")]
    public DateTime ClockDate { get; set; }

    public TimeSpan CalculateWorkHours(ClockingStaff clockingObj)
    {
        //Calculate Working hours
        TimeSpan timeSpan = clockingObj.ClockOutTime.GetValueOrDefault() - clockingObj.ClockInTime.GetValueOrDefault();

        //Calculate break duration
        TimeSpan breakDuration = clockingObj.ReturnOnBreakTime.GetValueOrDefault() - clockingObj.LeaveOnBreakTime.GetValueOrDefault();
        TimeSpan actualWorkingHours = timeSpan.Subtract(breakDuration);
        return actualWorkingHours;
    }
}
