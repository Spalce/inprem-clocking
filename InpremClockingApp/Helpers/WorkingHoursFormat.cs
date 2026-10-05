namespace InpremClockingApp.Helpers;

// Shared by HoursWorked and the Staff/Volunteer Hours Worked PDFs - previously three near-identical
// copies of this method existed (one already drifted into a display bug: negating totalMinutes
// before splitting it into hours/minutes put a "-" on both halves, e.g. "-03:-59" instead of
// "-03:59"). Negative durations still indicate bad underlying data (see StaffClocking/
// VolunteerClocking's manual-entry validation), but however they occur, they should render legibly.
public static class WorkingHoursFormat
{
    public static string ToHoursMinutes(double hours)
    {
        var totalMinutes = (int)Math.Round(hours * 60);
        var sign = totalMinutes < 0 ? "-" : "";
        var absMinutes = Math.Abs(totalMinutes);

        var wholeHours = absMinutes / 60;
        var minutes = absMinutes % 60;

        return $"{sign}{wholeHours:D2}:{minutes:D2}";
    }
}
