using System.Net;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.Kiosk;

/// <summary>
/// The kiosk clock buttons (api/controls/...), called by the clock pages' JavaScript.
/// Staff and volunteer endpoints behave identically (including the wording, which says
/// "staff" on both), so every test runs for both. Each test clocks a person of its own.
/// </summary>
public class KioskClockingTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public KioskClockingTests(AppFixture app) => _app = app;

    private sealed record Session(DateTime? In, DateTime? BreakStart, DateTime? BreakEnd, DateTime? Out, TimeSpan? Hours, DateTime ClockDate, int TenantId);

    private async Task<long> AddPersonAsync(string kind) =>
        kind == "staff" ? await _app.AddStaffAsync(_app.Data.Alpha) : await _app.AddVolunteerAsync(_app.Data.Alpha);

    private async Task<List<Session>> SessionsAsync(string kind, long personId)
    {
        await using var db = _app.CreateDbContext();
        return kind == "staff"
            ? await db.ClockingsStaff.IgnoreQueryFilters().Where(c => c.StafId == personId)
                .Select(c => new Session(c.ClockInTime, c.LeaveOnBreakTime, c.ReturnOnBreakTime, c.ClockOutTime, c.WorkingHours, c.ClockDate, c.TenantId)).ToListAsync()
            : await db.Clockings.IgnoreQueryFilters().Where(c => c.VoluntId == personId)
                .Select(c => new Session(c.ClockInTime, c.LeaveOnBreakTime, c.ReturnOnBreakTime, c.ClockOutTime, c.WorkingHours, c.ClockDate, c.TenantId)).ToListAsync();
    }

    private static string Url(string kind, string action, long id) => $"/api/controls/{kind}-{action}/{id}";

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_ClockIn_Break_ClockOut_ComputesHours(string kind)
    {
        var id = await AddPersonAsync(kind);
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var before = DateTime.UtcNow.AddSeconds(-1);

        Assert.Equal((HttpStatusCode.OK, "You have successfully clocked in"), await client.GetMessageAsync(Url(kind, "clockin", id)));
        Assert.Equal((HttpStatusCode.OK, "You have successfully clocked for a leave out"), await client.GetMessageAsync(Url(kind, "leavein", id)));
        Assert.Equal((HttpStatusCode.OK, "You have successfully clocked to have returned fom break"), await client.GetMessageAsync(Url(kind, "leaveout", id)));
        Assert.Equal((HttpStatusCode.OK, "You have successfully clocked out"), await client.GetMessageAsync(Url(kind, "clockout", id)));

        var after = DateTime.UtcNow.AddSeconds(1);
        var session = Assert.Single(await SessionsAsync(kind, id));
        Assert.Equal(_app.Data.Alpha.Id, session.TenantId);
        Assert.Equal(Arrange.TodayIn(_app.Data.Alpha), session.ClockDate);

        // Stored in UTC, in order.
        Assert.InRange(session.In!.Value, before, after);
        Assert.True(session.In <= session.BreakStart && session.BreakStart <= session.BreakEnd && session.BreakEnd <= session.Out);
        Assert.InRange(session.Out!.Value, before, after);

        // working hours = (out - in) - (break end - break start)
        var expected = (session.Out.Value - session.In.Value) - (session.BreakEnd!.Value - session.BreakStart!.Value);
        Assert.Equal(expected, session.Hours);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_ClockOut_After_A_Shift_Stores_Shift_Minus_Break(string kind)
    {
        var id = await AddPersonAsync(kind);
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        await client.GetMessageAsync(Url(kind, "clockin", id));

        // Pretend the person clocked in 8 hours ago and took a 30-minute break 4 hours ago.
        var now = DateTime.UtcNow;
        await using (var db = _app.CreateDbContext())
        {
            var sql = kind == "staff"
                ? "UPDATE ClockingsStaff SET ClockInTime = {0}, LeaveOnBreakTime = {1}, ReturnOnBreakTime = {2} WHERE StafId = {3}"
                : "UPDATE Clockings SET ClockInTime = {0}, LeaveOnBreakTime = {1}, ReturnOnBreakTime = {2} WHERE VoluntId = {3}";
            await db.Database.ExecuteSqlRawAsync(sql, now.AddHours(-8), now.AddHours(-4), now.AddHours(-3.5), id);
        }

        Assert.Equal((HttpStatusCode.OK, "You have successfully clocked out"), await client.GetMessageAsync(Url(kind, "clockout", id)));

        var session = Assert.Single(await SessionsAsync(kind, id));
        Assert.InRange(session.Hours!.Value, TimeSpan.FromHours(7.5), TimeSpan.FromHours(7.5) + TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_One_Session_Per_Person_Per_Day(string kind)
    {
        var id = await AddPersonAsync(kind);
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        await client.GetMessageAsync(Url(kind, "clockin", id));
        Assert.Equal((HttpStatusCode.Conflict, "You have already clocked in today."), await client.GetMessageAsync(Url(kind, "clockin", id)));

        await client.GetMessageAsync(Url(kind, "clockout", id));
        Assert.Equal((HttpStatusCode.OK, "You have already clocked out"), await client.GetMessageAsync(Url(kind, "clockout", id)));
        Assert.Equal(
            (HttpStatusCode.Conflict, "You have already clocked in and out today. If you need to step away, use Leave for Break / Return from Break instead."),
            await client.GetMessageAsync(Url(kind, "clockin", id)));

        Assert.Single(await SessionsAsync(kind, id));
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_Break_Rules(string kind)
    {
        var id = await AddPersonAsync(kind);
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        Assert.Equal((HttpStatusCode.OK, "No staff clocking exist for today"), await client.GetMessageAsync(Url(kind, "leavein", id)));
        Assert.Equal((HttpStatusCode.OK, "No staff clocking found for today"), await client.GetMessageAsync(Url(kind, "leaveout", id)));

        await client.GetMessageAsync(Url(kind, "clockin", id));
        Assert.Equal((HttpStatusCode.OK, "You cannot clock since you are not on break"), await client.GetMessageAsync(Url(kind, "leaveout", id)));

        await client.GetMessageAsync(Url(kind, "leavein", id));
        Assert.Equal((HttpStatusCode.OK, "You are already on break"), await client.GetMessageAsync(Url(kind, "leavein", id)));

        await client.GetMessageAsync(Url(kind, "leaveout", id));
        Assert.Equal((HttpStatusCode.OK, "You have already clocked to have returned from break"), await client.GetMessageAsync(Url(kind, "leaveout", id)));
        // Only one break per session: a second break start is refused the same way.
        Assert.Equal((HttpStatusCode.OK, "You are already on break"), await client.GetMessageAsync(Url(kind, "leavein", id)));
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_No_Break_After_ClockOut(string kind)
    {
        var id = await AddPersonAsync(kind);
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        await client.GetMessageAsync(Url(kind, "clockin", id));
        await client.GetMessageAsync(Url(kind, "clockout", id));

        Assert.Equal((HttpStatusCode.OK, "You cannot take leave since you have already clocked out"), await client.GetMessageAsync(Url(kind, "leavein", id)));
        var session = Assert.Single(await SessionsAsync(kind, id));
        Assert.Null(session.BreakStart);
        Assert.Equal(session.Out!.Value - session.In!.Value, session.Hours);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_ClockOut_While_On_Break_Ends_The_Break(string kind)
    {
        var id = await AddPersonAsync(kind);
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        await client.GetMessageAsync(Url(kind, "clockin", id));
        await client.GetMessageAsync(Url(kind, "leavein", id));
        Assert.Equal((HttpStatusCode.OK, "You have successfully clocked out"), await client.GetMessageAsync(Url(kind, "clockout", id)));

        var session = Assert.Single(await SessionsAsync(kind, id));
        Assert.Equal(session.Out, session.BreakEnd);
        Assert.Equal((session.Out!.Value - session.In!.Value) - (session.BreakEnd!.Value - session.BreakStart!.Value), session.Hours);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_Unknown_Person(string kind)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        const long unknown = 999_999;

        Assert.Equal((HttpStatusCode.BadRequest, "false"), await client.GetMessageAsync(Url(kind, "clockin", unknown)));
        Assert.Equal((HttpStatusCode.OK, "No staff matches this id provided"), await client.GetMessageAsync(Url(kind, "clockout", unknown)));
        Assert.Equal((HttpStatusCode.OK, "No staff clocking exist for today"), await client.GetMessageAsync(Url(kind, "leavein", unknown)));
        Assert.Equal((HttpStatusCode.OK, "No staff clocking found for today"), await client.GetMessageAsync(Url(kind, "leaveout", unknown)));
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_ClockDate_Is_The_Organizations_Local_Day(string kind)
    {
        // Beta (Europe/London) clocks in: ClockDate is London's date, whatever the server zone.
        var id = kind == "staff" ? await _app.AddStaffAsync(_app.Data.Beta) : await _app.AddVolunteerAsync(_app.Data.Beta);
        using var client = await _app.AdminOfAsync(_app.Data.Beta);

        await client.GetMessageAsync(Url(kind, "clockin", id));

        var session = Assert.Single(await SessionsAsync(kind, id));
        Assert.Equal(Arrange.TodayIn(_app.Data.Beta), session.ClockDate);
        Assert.Equal(_app.Data.Beta.Id, session.TenantId);
    }
}
