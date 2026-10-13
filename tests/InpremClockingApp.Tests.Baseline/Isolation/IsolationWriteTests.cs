using System.Net;
using System.Text;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.Isolation;

/// <summary>
/// Tenant isolation as it is today, for writes: Alpha's admin sends every write the UI can
/// make with one of Beta's ids. Beta's rows are fingerprinted before and after; any change is
/// a leak. Own fixture, because some of these requests do change data.
/// </summary>
public class IsolationWriteTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public IsolationWriteTests(AppFixture app) => _app = app;

    /// <summary>Every Beta staff, volunteer and clocking row, serialized in a stable order.</summary>
    private async Task<string> BetaFingerprintAsync()
    {
        var beta = _app.Data.Beta.Id;
        await using var db = _app.CreateDbContext();
        var sb = new StringBuilder();
        foreach (var s in await db.Staffs.IgnoreQueryFilters().Where(x => x.TenantId == beta).OrderBy(x => x.StaffId).ToListAsync())
            sb.AppendLine($"S|{s.StaffId}|{s.EmailAddress}|{s.FirstName}|{s.LastName}|{s.PhoneNumber}|{s.ZipCode}|{s.Address}|{s.Gender}|{s.Type}");
        foreach (var v in await db.Volunteers.IgnoreQueryFilters().Where(x => x.TenantId == beta).OrderBy(x => x.VolunteerId).ToListAsync())
            sb.AppendLine($"V|{v.VolunteerId}|{v.EmailAddress}|{v.FirstName}|{v.LastName}|{v.PhoneNumber}|{v.ZipCode}|{v.Address}|{v.Gender}|{v.VolunteerCategory}");
        foreach (var c in await db.ClockingsStaff.IgnoreQueryFilters().Where(x => x.TenantId == beta).OrderBy(x => x.ClockingStaffId).ToListAsync())
            sb.AppendLine($"CS|{c.ClockingStaffId}|{c.StafId}|{c.ClockInTime:O}|{c.LeaveOnBreakTime:O}|{c.ReturnOnBreakTime:O}|{c.ClockOutTime:O}|{c.WorkingHours}|{c.ClockDate:O}");
        foreach (var c in await db.Clockings.IgnoreQueryFilters().Where(x => x.TenantId == beta).OrderBy(x => x.ClockingId).ToListAsync())
            sb.AppendLine($"CV|{c.ClockingId}|{c.VoluntId}|{c.ClockInTime:O}|{c.LeaveOnBreakTime:O}|{c.ReturnOnBreakTime:O}|{c.ClockOutTime:O}|{c.WorkingHours}|{c.ClockDate:O}");
        return sb.ToString();
    }

    private async Task<long> BetaStaffClockingIdAsync()
    {
        var betaStaff = _app.Data.Beta.Staff[2].Id;
        await using var db = _app.CreateDbContext();
        return await db.ClockingsStaff.IgnoreQueryFilters().Where(c => c.StafId == betaStaff).OrderBy(c => c.ClockDate).Select(c => c.ClockingStaffId).FirstAsync();
    }

    private async Task<long> BetaVolunteerClockingIdAsync()
    {
        var betaVolunteer = _app.Data.Beta.Volunteers[2].Id;
        await using var db = _app.CreateDbContext();
        return await db.Clockings.IgnoreQueryFilters().Where(c => c.VoluntId == betaVolunteer).OrderBy(c => c.ClockDate).Select(c => c.ClockingId).FirstAsync();
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Isolation_Kiosk_Buttons_With_Beta_Id_Change_Nothing(string kind)
    {
        var betaId = kind == "staff" ? _app.Data.Beta.Staff[2].Id : _app.Data.Beta.Volunteers[2].Id;
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var before = await BetaFingerprintAsync();

        Assert.Equal((HttpStatusCode.BadRequest, "false"), await client.GetMessageAsync($"/api/controls/{kind}-clockin/{betaId}"));
        Assert.Equal((HttpStatusCode.OK, "No staff matches this id provided"), await client.GetMessageAsync($"/api/controls/{kind}-clockout/{betaId}"));
        Assert.Equal((HttpStatusCode.OK, "No staff clocking exist for today"), await client.GetMessageAsync($"/api/controls/{kind}-leavein/{betaId}"));
        Assert.Equal((HttpStatusCode.OK, "No staff clocking found for today"), await client.GetMessageAsync($"/api/controls/{kind}-leaveout/{betaId}"));

        Assert.Equal(before, await BetaFingerprintAsync());
    }

    [Fact]
    public async Task Isolation_Editing_Beta_People_Changes_Nothing()
    {
        var betaStaff = _app.Data.Beta.Staff[2];
        var betaVolunteer = _app.Data.Beta.Volunteers[2];
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var before = await BetaFingerprintAsync();

        using var staff = await client.PostJsonAsync("/Staff?handler=Update", new
        {
            StaffId = betaStaff.Id, FirstName = "Hijacked", LastName = "Name", EmailAddress = "hijack@alpha.test",
            Gender = "M", PhoneNumber = "6145550000", ZipCode = "00000", Address = "1 Hijack Road, Nowhere",
        });
        using var volunteer = await client.PostJsonAsync("/Volunteer?handler=Update", new
        {
            VolunteerId = betaVolunteer.Id, FirstName = "Hijacked", LastName = "Name", EmailAddress = "hijack2@alpha.test",
            Gender = "M", PhoneNumber = "6145550000", ZipCode = "00000", Address = "1 Hijack Road, Nowhere",
            VolunteerCategory = "Personal or Individual Volunteering",
        });

        Assert.Equal(HttpStatusCode.NoContent, staff.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, volunteer.StatusCode);
        Assert.Equal(before, await BetaFingerprintAsync());
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Isolation_Editing_Beta_Clockings_Changes_Nothing(string kind)
    {
        var clockingId = kind == "staff" ? await BetaStaffClockingIdAsync() : await BetaVolunteerClockingIdAsync();
        var page = kind == "staff" ? "StaffClocking" : "VolunteerClocking";
        object Body() => kind == "staff"
            ? new { ClockingStaffId = clockingId, ClockInTime = "2026-03-26T01:00", ClockOutTime = "2026-03-26T02:00" }
            : new { ClockingId = clockingId, ClockInTime = "2026-03-26T01:00", ClockOutTime = "2026-03-26T02:00" };
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var before = await BetaFingerprintAsync();

        using var edit = await client.PostJsonAsync($"/{page}?handler=Edit", Body());
        using var breakStart = await client.PostJsonAsync($"/{page}?handler=BreakStart", Body());
        using var breakEnd = await client.PostJsonAsync($"/{page}?handler=BreakEnd", Body());
        using var clockOut = await client.PostJsonAsync($"/{page}?handler=ClockOut", Body());

        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);
        Assert.Equal("Clocking record not found.", (await edit.ReadJsonAsync()).GetProperty("error").GetString());
        Assert.Equal(before, await BetaFingerprintAsync());
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Isolation_Manual_Clocking_For_Beta_Person_Is_Refused(string kind)
    {
        var betaId = kind == "staff" ? _app.Data.Beta.Staff[2].Id : _app.Data.Beta.Volunteers[2].Id;
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var before = await BetaFingerprintAsync();

        using var response = kind == "staff"
            ? await client.PostFormAsync("/StaffClocking", new Dictionary<string, string> { ["ClockingStaff.StafId"] = betaId.ToString() })
            : await client.PostFormAsync("/VolunteerClocking", new Dictionary<string, string> { ["ClockingVolunteerProp.VoluntId"] = betaId.ToString() });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(before, await BetaFingerprintAsync());
        await using var db = _app.CreateDbContext();
        Assert.Equal(0, kind == "staff"
            ? await db.ClockingsStaff.IgnoreQueryFilters().CountAsync(c => c.StafId == betaId && c.TenantId == _app.Data.Alpha.Id)
            : await db.Clockings.IgnoreQueryFilters().CountAsync(c => c.VoluntId == betaId && c.TenantId == _app.Data.Alpha.Id));
    }

    [Fact]
    public async Task Isolation_Move_Api_With_Beta_Id_Is_Not_Found()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var before = await BetaFingerprintAsync();

        using var response = await client.PostJsonAsync("/api/staff/move", new { StaffId = _app.Data.Beta.Staff[2].Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await BetaFingerprintAsync());
    }

    [Fact]
    public async Task Isolation_Kiosk_Registration_May_Reuse_Another_Organizations_Email()
    {
        // Emails are unique per organization only, so Alpha can register Beta's staff email.
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var before = await BetaFingerprintAsync();

        using var response = await client.PostFormAsync("/StaffAttendance", new Dictionary<string, string>
        {
            ["Input.EmailAddress"] = _app.Data.Beta.Staff[1].Email,
            ["Input.FirstName"] = "Alpha",
            ["Input.LastName"] = "Namesake",
            ["Input.ZipCode"] = "43229",
            ["Input.Gender"] = "F",
            ["Input.PhoneNumber"] = "6145557777",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(before, await BetaFingerprintAsync());
        await using var db = _app.CreateDbContext();
        Assert.Equal(_app.Data.Alpha.Id, (await db.Staffs.IgnoreQueryFilters().SingleAsync(s => s.FirstName == "Alpha" && s.LastName == "Namesake")).TenantId);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    [Trait(Traits.Finding, "F-08")]
    public async Task Isolation_Move_Page_Handler_With_Beta_Id(string kind)
    {
        // The Move page handlers delete the posted id without checking it belongs to the
        // signed-in organization. This records what happens to Beta's row and its sessions.
        var betaId = kind == "staff" ? _app.Data.Beta.Staff[0].Id : _app.Data.Beta.Volunteers[0].Id;
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var email = $"moved-{kind}@alpha.test";

        using var response = kind == "staff"
            ? await client.PostJsonAsync("/Staff?handler=Move", new { StaffId = betaId, FirstName = "Cross", LastName = "Move", EmailAddress = email, Gender = "M", PhoneNumber = "6145558888", ZipCode = "43229", Address = "1 Cross Move Road, Testville" })
            : await client.PostJsonAsync("/Volunteer?handler=Move", new { VolunteerId = betaId, FirstName = "Cross", LastName = "Move", EmailAddress = email, Gender = "M", PhoneNumber = "6145558888", ZipCode = "43229", Address = "1 Cross Move Road, Testville" });

        await using var db = _app.CreateDbContext();
        var betaRowExists = kind == "staff"
            ? await db.Staffs.IgnoreQueryFilters().AnyAsync(s => s.StaffId == betaId)
            : await db.Volunteers.IgnoreQueryFilters().AnyAsync(v => v.VolunteerId == betaId);
        var betaSessions = kind == "staff"
            ? await db.ClockingsStaff.IgnoreQueryFilters().CountAsync(c => c.StafId == betaId)
            : await db.Clockings.IgnoreQueryFilters().CountAsync(c => c.VoluntId == betaId);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(betaRowExists, "Beta's person was deleted by Alpha's admin.");
        Assert.Equal(0, betaSessions);
    }
}
