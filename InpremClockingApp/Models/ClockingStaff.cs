using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InpremClockingApp.Models;

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

    public DateTime GetUserDateTime(DateTime? utcTime = null, string? timeZone = "Eastern Standard Time")
    {
        if (utcTime == null)
            utcTime = DateTime.UtcNow;

        TimeZoneInfo userTz = TimeZoneInfo.FindSystemTimeZoneById(timeZone!);

        return TimeZoneInfo.ConvertTimeFromUtc(utcTime.Value, userTz);
    }

    public DateTime Convert(DateTime date, string fromZone, string toZone)
    {
        TimeZoneInfo to = TimeZoneInfo.FindSystemTimeZoneById(toZone);
        TimeZoneInfo from = TimeZoneInfo.FindSystemTimeZoneById(fromZone);
        return new DateTime(TimeZoneInfo.ConvertTime(date, from, to).Ticks, DateTimeKind.Unspecified);
    }

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
