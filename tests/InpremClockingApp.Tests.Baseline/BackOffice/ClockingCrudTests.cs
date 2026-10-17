using System.Net;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.BackOffice;

/// <summary>
/// Staff Clocking / Volunteer Clocking pages: the manual entry form (plain form post) and the
/// Edit / ClockOut / BreakStart / BreakEnd JSON handlers. Times typed by the admin are Alpha
/// local time (America/New_York; EST = UTC-5 before 8 March, EDT = UTC-4 after).
/// </summary>
public class ClockingCrudTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public ClockingCrudTests(AppFixture app) => _app = app;

    private static string Local(DateTime value) => value.ToString("yyyy-MM-ddTHH:mm");

    [Fact]
    public async Task Clocking_Manual_Entry_Creates_Todays_Session()
    {
        var id = await _app.AddStaffAsync(_app.Data.Alpha, "Manual", "Entry");
        var today = Arrange.TodayIn(_app.Data.Alpha);
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostFormAsync("/StaffClocking", new Dictionary<string, string>
        {
            ["ClockingStaff.StafId"] = id.ToString(),
            ["ClockingStaff.ClockInTime"] = Local(today.AddHours(9)),
            ["ClockingStaff.ClockOutTime"] = Local(today.AddHours(17)),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = _app.CreateDbContext();
        var row = await db.ClockingsStaff.IgnoreQueryFilters().SingleAsync(c => c.StafId == id);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Assert.Equal(TimeZoneInfo.ConvertTimeToUtc(today.AddHours(9), zone), row.ClockInTime);
        Assert.Equal(TimeZoneInfo.ConvertTimeToUtc(today.AddHours(17), zone), row.ClockOutTime);
        Assert.Equal(TimeSpan.FromHours(8), row.WorkingHours);
        Assert.Equal(today, row.ClockDate);
        Assert.Equal("Manual Entry", row.FullName);
        Assert.Equal(_app.Data.Alpha.Id, row.TenantId);
    }

    [Fact]
    [Trait(Traits.Finding, "F-07")]
    public async Task Clocking_Manual_Entry_For_A_Past_Day_Is_Filed_Under_Today()
    {
        var id = await _app.AddStaffAsync(_app.Data.Alpha, "Past", "Entry");
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        await client.PostFormAsync("/StaffClocking", new Dictionary<string, string>
        {
            ["ClockingStaff.StafId"] = id.ToString(),
            ["ClockingStaff.ClockInTime"] = "2026-02-10T09:00",
            ["ClockingStaff.ClockOutTime"] = "2026-02-10T17:00",
        });

        await using var db = _app.CreateDbContext();
        var row = await db.ClockingsStaff.IgnoreQueryFilters().SingleAsync(c => c.StafId == id);
        Assert.Equal(new DateTime(2026, 2, 10, 14, 0, 0), row.ClockInTime);
        Assert.Equal(Arrange.TodayIn(_app.Data.Alpha), row.ClockDate);
    }

    [Fact]
    [Trait(Traits.ChangedBy, "Developer's request 2026-10-17: the second same-day entry is explained in the pop-up (it reloaded silently)")]
    public async Task Clocking_Manual_Entry_Rules()
    {
        var id = await _app.AddStaffAsync(_app.Data.Alpha, "Manual", "Rules");
        var today = Arrange.TodayIn(_app.Data.Alpha);
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        // Clock out before clock in: page redisplays with an error, nothing saved.
        using var backwards = await client.PostFormAsync("/StaffClocking", new Dictionary<string, string>
        {
            ["ClockingStaff.StafId"] = id.ToString(),
            ["ClockingStaff.ClockInTime"] = Local(today.AddHours(17)),
            ["ClockingStaff.ClockOutTime"] = Local(today.AddHours(9)),
        });
        Assert.Equal(HttpStatusCode.OK, backwards.StatusCode);
        Assert.Contains("Clock Out must be after Clock In.", (await backwards.ReadDocumentAsync()).Body!.CleanText());

        // Unknown person: back to the list.
        using var unknown = await client.PostFormAsync("/StaffClocking", new Dictionary<string, string> { ["ClockingStaff.StafId"] = "999999" });
        Assert.Equal(HttpStatusCode.Redirect, unknown.StatusCode);

        // No clock-in time: now. A second entry on the same day is not saved, and the pop-up says why.
        using var first = await client.PostFormAsync("/StaffClocking", new Dictionary<string, string> { ["ClockingStaff.StafId"] = id.ToString() });
        using var second = await client.PostFormAsync("/StaffClocking", new Dictionary<string, string> { ["ClockingStaff.StafId"] = id.ToString() });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("Manual Rules has already clocked in today.", await second.Content.ReadAsStringAsync());

        await using var db = _app.CreateDbContext();
        var row = await db.ClockingsStaff.IgnoreQueryFilters().SingleAsync(c => c.StafId == id);
        Assert.InRange(row.ClockInTime!.Value, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
        Assert.Null(row.ClockOutTime);
        Assert.Null(row.WorkingHours);
    }

    [Fact]
    public async Task Clocking_Volunteer_Manual_Entry_Creates_Todays_Session()
    {
        var id = await _app.AddVolunteerAsync(_app.Data.Alpha, "Manual", "Volunteer");
        var today = Arrange.TodayIn(_app.Data.Alpha);
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostFormAsync("/VolunteerClocking", new Dictionary<string, string>
        {
            ["ClockingVolunteerProp.VoluntId"] = id.ToString(),
            ["ClockingVolunteerProp.ClockInTime"] = Local(today.AddHours(10)),
            ["ClockingVolunteerProp.ClockOutTime"] = Local(today.AddHours(12)),
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = _app.CreateDbContext();
        var row = await db.Clockings.IgnoreQueryFilters().SingleAsync(c => c.VoluntId == id);
        Assert.Equal(TimeSpan.FromHours(2), row.WorkingHours);
        Assert.Equal(today, row.ClockDate);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    [Trait(Traits.ChangedBy, "Developer's request 2026-10-17: a second manual clock-in the same day is explained in the pop-up (it reloaded silently)")]
    public async Task Clocking_A_Second_Manual_Clock_In_The_Same_Day_Is_Explained_And_Not_Saved(string kind)
    {
        var staff = kind == "staff";
        var id = staff ? await _app.AddStaffAsync(_app.Data.Alpha, "Twice", "Today") : await _app.AddVolunteerAsync(_app.Data.Alpha, "Twice", "Today");
        var today = Arrange.TodayIn(_app.Data.Alpha);
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var page = staff ? "/StaffClocking" : "/VolunteerClocking";
        var prefix = staff ? "ClockingStaff" : "ClockingVolunteerProp";
        var form = new Dictionary<string, string>
        {
            [$"{prefix}.{(staff ? "StafId" : "VoluntId")}"] = id.ToString(),
            [$"{prefix}.ClockInTime"] = Local(today.AddHours(9)),
        };
        using (var first = await client.PostFormAsync(page, form)) Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await client.PostFormAsync(page, form);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode); // the page again, not a silent reload
        var html = await second.Content.ReadAsStringAsync();
        Assert.Contains("Clocking not saved", html);
        var shown = html[html.IndexOf("Clocking not saved", StringComparison.Ordinal)..];
        Assert.True(shown.Contains("Twice Today has already clocked in today.", StringComparison.Ordinal), shown[..Math.Min(300, shown.Length)]);

        await using var db = _app.CreateDbContext();
        var rows = staff
            ? await db.ClockingsStaff.IgnoreQueryFilters().CountAsync(c => c.StafId == id)
            : await db.Clockings.IgnoreQueryFilters().CountAsync(c => c.VoluntId == id);
        Assert.Equal(1, rows);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Clocking_Edit_Corrects_Times_And_Recomputes_Hours(string kind)
    {
        // Avery's / Dana's 5 March session (EST, UTC-5).
        var personId = kind == "staff" ? _app.Data.Alpha.Staff[0].Id : _app.Data.Alpha.Volunteers[0].Id;
        var clockingId = await ClockingIdAsync(kind, personId, new DateTime(2026, 3, 5));
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostJsonAsync($"/{Page(kind)}?handler=Edit", Body(kind, clockingId,
            "2026-03-05T08:00", "2026-03-05T16:00", "2026-03-05T12:00", "2026-03-05T13:00"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = _app.CreateDbContext();
        var (inUtc, outUtc, hours) = kind == "staff"
            ? await db.ClockingsStaff.IgnoreQueryFilters().Where(c => c.ClockingStaffId == clockingId).Select(c => ValueTuple.Create(c.ClockInTime, c.ClockOutTime, c.WorkingHours)).SingleAsync()
            : await db.Clockings.IgnoreQueryFilters().Where(c => c.ClockingId == clockingId).Select(c => ValueTuple.Create(c.ClockInTime, c.ClockOutTime, c.WorkingHours)).SingleAsync();
        Assert.Equal(new DateTime(2026, 3, 5, 13, 0, 0), inUtc);
        Assert.Equal(new DateTime(2026, 3, 5, 21, 0, 0), outUtc);
        Assert.Equal(TimeSpan.FromHours(7), hours);
    }

    [Theory]
    [InlineData("2026-03-06T16:00", "2026-03-06T08:00", null, null, "Clock Out must be after Clock In.")]
    [InlineData("2026-03-06T08:00", "2026-03-06T16:00", "2026-03-06T12:00", null, "Enter both Break Start and Break End, or leave both blank.")]
    [InlineData("2026-03-06T08:00", "2026-03-06T16:00", "2026-03-06T13:00", "2026-03-06T12:00", "Break End must be after Break Start.")]
    [InlineData("2026-03-06T08:00", "2026-03-07T09:00", null, null, "The resulting working hours must be between 0 and 24 hours - check the break times.")]
    [InlineData(null, "2026-03-06T16:00", null, null, "Clock In is required.")]
    public async Task Clocking_Edit_Rules(string? clockIn, string? clockOut, string? breakStart, string? breakEnd, string expectedError)
    {
        var clockingId = await ClockingIdAsync("staff", _app.Data.Alpha.Staff[1].Id, new DateTime(2026, 3, 6));
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostJsonAsync("/StaffClocking?handler=Edit", Body("staff", clockingId, clockIn, clockOut, breakStart, breakEnd));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedError, (await response.ReadJsonAsync()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Clocking_Edit_Unknown_Record()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostJsonAsync("/StaffClocking?handler=Edit", Body("staff", 999_999, "2026-03-06T08:00", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Clocking record not found.", (await response.ReadJsonAsync()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Clocking_Page_Break_And_ClockOut_Handlers_Update_An_Open_Session()
    {
        var id = await _app.AddStaffAsync(_app.Data.Alpha, "Open", "Session");
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        await client.GetMessageAsync($"/api/controls/staff-clockin/{id}");
        var clockingId = await ClockingIdAsync("staff", id, Arrange.TodayIn(_app.Data.Alpha));

        using var breakStart = await client.PostJsonAsync("/StaffClocking?handler=BreakStart", new { ClockingStaffId = clockingId });
        using var breakEnd = await client.PostJsonAsync("/StaffClocking?handler=BreakEnd", new { ClockingStaffId = clockingId });
        using var clockOut = await client.PostJsonAsync("/StaffClocking?handler=ClockOut", new { ClockingStaffId = clockingId });

        Assert.All(new[] { breakStart, breakEnd, clockOut }, r => Assert.Equal(HttpStatusCode.Redirect, r.StatusCode));
        await using var db = _app.CreateDbContext();
        var row = await db.ClockingsStaff.IgnoreQueryFilters().SingleAsync(c => c.ClockingStaffId == clockingId);
        Assert.NotNull(row.LeaveOnBreakTime);
        Assert.NotNull(row.ReturnOnBreakTime);
        Assert.NotNull(row.ClockOutTime);
        Assert.Equal((row.ClockOutTime - row.ClockInTime) - (row.ReturnOnBreakTime - row.LeaveOnBreakTime), row.WorkingHours);
    }

    private static string Page(string kind) => kind == "staff" ? "StaffClocking" : "VolunteerClocking";

    private static object Body(string kind, long clockingId, string? clockIn, string? clockOut, string? breakStart, string? breakEnd) =>
        kind == "staff"
            ? new { ClockingStaffId = clockingId, ClockInTime = clockIn, ClockOutTime = clockOut, LeaveOnBreakTime = breakStart, ReturnOnBreakTime = breakEnd }
            : new { ClockingId = clockingId, ClockInTime = clockIn, ClockOutTime = clockOut, LeaveOnBreakTime = breakStart, ReturnOnBreakTime = breakEnd };

    private async Task<long> ClockingIdAsync(string kind, long personId, DateTime clockDate)
    {
        await using var db = _app.CreateDbContext();
        return kind == "staff"
            ? await db.ClockingsStaff.IgnoreQueryFilters().Where(c => c.StafId == personId && c.ClockDate == clockDate).Select(c => c.ClockingStaffId).SingleAsync()
            : await db.Clockings.IgnoreQueryFilters().Where(c => c.VoluntId == personId && c.ClockDate == clockDate).Select(c => c.ClockingId).SingleAsync();
    }
}
