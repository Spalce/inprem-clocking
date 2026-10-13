using System.Net;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.Infrastructure;

/// <summary>
/// Proves the test harness itself: the unchanged app starts on a migrated test database, the
/// standard organizations are seeded as specified, and the real login page signs people in.
/// </summary>
public class TestInfrastructureTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public TestInfrastructureTests(AppFixture app) => _app = app;

    [Fact]
    public async Task App_Starts_And_Serves_Login_Page()
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(SignIn.LoginPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Input.Password", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Seed_Creates_Organizations_With_People_And_Clockings()
    {
        await using var db = _app.CreateDbContext();

        // The first organization comes from the migrations, the other three from the seeder.
        var tenants = await db.Tenants.IgnoreQueryFilters().OrderBy(t => t.Id).ToListAsync();
        Assert.Equal(
            new[] { "Inprem Holistic Community Resource Center", "Alpha", "Beta", "Gamma" },
            tenants.Select(t => t.Name));
        Assert.Equal(
            new[] { "America/New_York", "America/New_York", "Europe/London", "America/Chicago" },
            tenants.Select(t => t.TimeZoneId));

        foreach (var tenant in new[] { _app.Data.Alpha, _app.Data.Beta })
        {
            Assert.Equal(2, await db.Users.IgnoreQueryFilters().CountAsync(u => u.TenantId == tenant.Id));
            Assert.Equal(3, await db.Staffs.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenant.Id));
            Assert.Equal(3, await db.Volunteers.IgnoreQueryFilters().CountAsync(v => v.TenantId == tenant.Id));
            // 3 people x 4 days each.
            Assert.Equal(12, await db.ClockingsStaff.IgnoreQueryFilters().CountAsync(c => c.TenantId == tenant.Id));
            Assert.Equal(12, await db.Clockings.IgnoreQueryFilters().CountAsync(c => c.TenantId == tenant.Id));
        }

        foreach (var empty in new[] { _app.Data.House.Id, _app.Data.Gamma.Id })
        {
            Assert.Equal(0, await db.Users.IgnoreQueryFilters().CountAsync(u => u.TenantId == empty));
            Assert.Equal(0, await db.Staffs.IgnoreQueryFilters().CountAsync(s => s.TenantId == empty));
            Assert.Equal(0, await db.Volunteers.IgnoreQueryFilters().CountAsync(v => v.TenantId == empty));
        }
    }

    [Fact]
    public async Task Seed_Alpha_Daylight_Saving_Day_Is_Stored_In_Utc()
    {
        await using var db = _app.CreateDbContext();
        var staffId = _app.Data.Alpha.Staff[0].Id;

        var row = await db.ClockingsStaff.IgnoreQueryFilters()
            .SingleAsync(c => c.StafId == staffId && c.ClockDate == new DateTime(2026, 3, 8));

        // 01:00 EST, 03:00 / 03:30 / 06:00 EDT on 8 March 2026.
        Assert.Equal(new DateTime(2026, 3, 8, 6, 0, 0), row.ClockInTime);
        Assert.Equal(new DateTime(2026, 3, 8, 7, 0, 0), row.LeaveOnBreakTime);
        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0), row.ReturnOnBreakTime);
        Assert.Equal(new DateTime(2026, 3, 8, 10, 0, 0), row.ClockOutTime);
        Assert.Equal(TimeSpan.FromHours(3.5), row.WorkingHours);
    }

    [Fact]
    public async Task Seed_Beta_Daylight_Saving_Day_Is_Stored_In_Utc()
    {
        await using var db = _app.CreateDbContext();
        var volunteerId = _app.Data.Beta.Volunteers[0].Id;

        var row = await db.Clockings.IgnoreQueryFilters()
            .SingleAsync(c => c.VoluntId == volunteerId && c.ClockDate == new DateTime(2026, 3, 29));

        // 00:30 GMT, 02:00 / 02:30 / 05:00 BST on 29 March 2026.
        Assert.Equal(new DateTime(2026, 3, 29, 0, 30, 0), row.ClockInTime);
        Assert.Equal(new DateTime(2026, 3, 29, 1, 0, 0), row.LeaveOnBreakTime);
        Assert.Equal(new DateTime(2026, 3, 29, 1, 30, 0), row.ReturnOnBreakTime);
        Assert.Equal(new DateTime(2026, 3, 29, 4, 0, 0), row.ClockOutTime);
        Assert.Equal(TimeSpan.FromHours(3), row.WorkingHours);
    }

    [Fact]
    public async Task No_Account_Is_Seeded_From_Configuration()
    {
        await using var db = _app.CreateDbContext();

        // Only the four seeded tenant admins exist: the app creates no account from configuration
        // (Phase 0 step 0.6; before, the test host had to blank the seed keys).
        Assert.Equal(4, await db.Users.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task SignIn_Through_Login_Page_Redirects_To_Kiosk_And_Opens_BackOffice()
    {
        using var client = _app.CreateClient();

        var location = await SignIn.AsAsync(client, _app.Data.Alpha.Admin!.Email, _app.Data.Password);

        Assert.Equal("/VolunteerAttendance", location);
        using var backOffice = await client.GetAsync("/BackOffice");
        Assert.Equal(HttpStatusCode.OK, backOffice.StatusCode);
    }

    [Fact]
    public async Task SignIn_With_Wrong_Password_Shows_Invalid_Login()
    {
        using var client = _app.CreateClient();

        using var response = await SignIn.PostLoginAsync(client, _app.Data.Alpha.Admin!.Email, "Wrong-Passw0rd!");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid login attempt.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anonymous_BackOffice_Request_Redirects_To_Login()
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync("/BackOffice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/Identity/Account/Login", response.Headers.Location!.ToString());
    }
}
