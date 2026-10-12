using System.Net;
using InpremClockingApp.Models;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.BackOffice;

/// <summary>
/// Manage Staff / Manage Volunteers: the JSON handlers the editor dialogs call
/// (<c>/Staff?handler=Create|Update|Move</c>, <c>/Volunteer?handler=Create|Update|Move|Delete</c>).
/// </summary>
public class PeopleCrudTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public PeopleCrudTests(AppFixture app) => _app = app;

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20] + "@alpha.test";

    private static object StaffPayload(string email, string first = "Editor", string last = "Created", long id = 0) => new
    {
        StaffId = id,
        FirstName = first,
        LastName = last,
        EmailAddress = email,
        Gender = "M",
        PhoneNumber = "6145559900",
        ZipCode = "43229",
        Address = "77 Editor Lane, Testville",
    };

    private static object VolunteerPayload(string email, string? category = "Personal or Individual Volunteering", string first = "Editor", string last = "Created", long id = 0) => new
    {
        VolunteerId = id,
        FirstName = first,
        LastName = last,
        EmailAddress = email,
        Gender = "F",
        PhoneNumber = "6145559901",
        ZipCode = "43229",
        Address = "78 Editor Lane, Testville",
        VolunteerCategory = category,
    };

    private async Task AddClockingsAsync(long staffId, params DateOnly[] days)
    {
        await using var db = _app.CreateDbContext();
        foreach (var day in days)
        {
            var inUtc = day.ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc);
            db.ClockingsStaff.Add(new ClockingStaff
            {
                TenantId = _app.Data.Alpha.Id, StafId = staffId, FullName = "Mover Person",
                ClockInTime = inUtc, ClockOutTime = inUtc.AddHours(2), WorkingHours = TimeSpan.FromHours(2),
                CreatedAt = inUtc, ClockDate = day.ToDateTime(TimeOnly.MinValue),
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task AddVolunteerClockingsAsync(long volunteerId, params DateOnly[] days)
    {
        await using var db = _app.CreateDbContext();
        foreach (var day in days)
        {
            var inUtc = day.ToDateTime(new TimeOnly(14, 0), DateTimeKind.Utc);
            db.Clockings.Add(new Clocking
            {
                TenantId = _app.Data.Alpha.Id, VoluntId = volunteerId, FullName = "Mover Person",
                ClockInTime = inUtc, ClockOutTime = inUtc.AddHours(2), WorkingHours = TimeSpan.FromHours(2),
                CreatedAt = inUtc, ClockDate = day.ToDateTime(TimeOnly.MinValue),
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Staff_Create_Saves_In_The_Admins_Organization()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var email = NewEmail("created");

        using var response = await client.PostJsonAsync("/Staff?handler=Create", StaffPayload(email));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.ReadJsonAsync()).GetProperty("staffId").GetInt64();
        await using var db = _app.CreateDbContext();
        var saved = await db.Staffs.IgnoreQueryFilters().SingleAsync(s => s.StaffId == id);
        Assert.Equal(_app.Data.Alpha.Id, saved.TenantId);
        Assert.Equal("Staff", saved.Type);
        Assert.Equal(email, saved.EmailAddress);
        Assert.NotNull(saved.CreatedAt);
    }

    [Fact]
    public async Task Staff_Create_Validates_Email()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var invalid = await client.PostJsonAsync("/Staff?handler=Create", StaffPayload("not-an-email"));
        using var duplicate = await client.PostJsonAsync("/Staff?handler=Create", StaffPayload("staff1@alpha.test"));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Please enter a valid email address", (await invalid.ReadJsonAsync()).GetProperty("errors").GetProperty("EmailAddress")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal("A staff with this email already exists", (await duplicate.ReadJsonAsync()).GetProperty("errors").GetProperty("EmailAddress")[0].GetString());
    }

    [Fact]
    public async Task Staff_Create_Same_Name_Asks_First_Then_Creates_When_Confirmed()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var casey = _app.Data.Alpha.Staff[2];
        var email = NewEmail("samename");

        using var ask = await client.PostJsonAsync("/Staff?handler=Create", StaffPayload(email, casey.FirstName, casey.LastName));

        Assert.Equal(HttpStatusCode.Conflict, ask.StatusCode);
        var json = await ask.ReadJsonAsync();
        Assert.True(json.GetProperty("nameConflict").GetBoolean());
        var match = Assert.Single(json.GetProperty("matches").EnumerateArray());
        Assert.Equal(casey.Id, match.GetProperty("id").GetInt64());
        Assert.Equal("Casey Fields", match.GetProperty("name").GetString());
        Assert.Equal("staff3@alpha.test", match.GetProperty("email").GetString());

        using var confirmed = await client.PostJsonAsync("/Staff?handler=Create&confirmDifferentPerson=true", StaffPayload(email, casey.FirstName, casey.LastName));
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
    }

    [Fact]
    public async Task Staff_Update_Changes_The_Record()
    {
        var id = await _app.AddStaffAsync(_app.Data.Alpha, "Before", "Update");
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var email = NewEmail("updated");

        using var response = await client.PostJsonAsync("/Staff?handler=Update", StaffPayload(email, "After", "Update", id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = _app.CreateDbContext();
        var saved = await db.Staffs.IgnoreQueryFilters().SingleAsync(s => s.StaffId == id);
        Assert.Equal(("After", email, "6145559900"), (saved.FirstName, saved.EmailAddress, saved.PhoneNumber));
    }

    [Fact]
    public async Task Staff_Update_Unknown_Id_Changes_Nothing()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostJsonAsync("/Staff?handler=Update", StaffPayload(NewEmail("ghost"), id: 999_999));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Staff_Move_Without_Sessions_Turns_Staff_Into_Volunteer()
    {
        var id = await _app.AddStaffAsync(_app.Data.Alpha, "Mover", "Alone");
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        string email;
        await using (var db = _app.CreateDbContext()) email = (await db.Staffs.IgnoreQueryFilters().SingleAsync(s => s.StaffId == id)).EmailAddress;

        using var response = await client.PostJsonAsync("/Staff?handler=Move", StaffPayload(email, "Mover", "Alone", id));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var check = _app.CreateDbContext();
        Assert.False(await check.Staffs.IgnoreQueryFilters().AnyAsync(s => s.StaffId == id));
        var volunteer = await check.Volunteers.IgnoreQueryFilters().SingleAsync(v => v.EmailAddress == email);
        Assert.Equal(_app.Data.Alpha.Id, volunteer.TenantId);
        Assert.Equal("Volunteer", volunteer.Type);
    }

    [Fact]
    [Trait(Traits.Finding, "F-01")]
    public async Task Staff_Move_With_Several_Sessions_Fails_Halfway()
    {
        // The page handler copies sessions without their ClockDate, so every copy gets
        // 0001-01-01; the second copy breaks the one-session-per-day index. The request fails
        // after the volunteer and the first session were already moved, leaving the person both
        // a staff member (with the rest of the sessions) and a volunteer.
        // (api/staff/move had this fixed in 73ffd06, but the page does not call it.)
        var id = await _app.AddStaffAsync(_app.Data.Alpha, "Mover", "Many");
        await AddClockingsAsync(id, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 3), new DateOnly(2026, 2, 4));
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        string email;
        await using (var db = _app.CreateDbContext()) email = (await db.Staffs.IgnoreQueryFilters().SingleAsync(s => s.StaffId == id)).EmailAddress;

        using var response = await client.PostJsonAsync("/Staff?handler=Move", StaffPayload(email, "Mover", "Many", id));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var check = _app.CreateDbContext();
        Assert.True(await check.Staffs.IgnoreQueryFilters().AnyAsync(s => s.StaffId == id));
        Assert.Equal(2, await check.ClockingsStaff.IgnoreQueryFilters().CountAsync(c => c.StafId == id));
        var volunteer = await check.Volunteers.IgnoreQueryFilters().SingleAsync(v => v.EmailAddress == email);
        var moved = await check.Clockings.IgnoreQueryFilters().Where(c => c.VoluntId == volunteer.VolunteerId).ToListAsync();
        var single = Assert.Single(moved);
        Assert.Equal(DateTime.MinValue, single.ClockDate);
    }

    [Fact]
    public async Task Staff_Move_Api_Moves_All_Sessions_With_Their_Dates()
    {
        // api/staff/move (fixed version; no page calls it today).
        var id = await _app.AddStaffAsync(_app.Data.Alpha, "Mover", "Api");
        await AddClockingsAsync(id, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 3));
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        string email;
        await using (var db = _app.CreateDbContext()) email = (await db.Staffs.IgnoreQueryFilters().SingleAsync(s => s.StaffId == id)).EmailAddress;

        var (status, message) = await PostMessageAsync(client, "/api/staff/move", new { StaffId = id });

        Assert.Equal((HttpStatusCode.OK, "Move successful - 2 clocking record(s) moved."), (status, message));
        await using var check = _app.CreateDbContext();
        // Unlike the page handler, the staff row itself is kept (with no sessions left).
        Assert.True(await check.Staffs.IgnoreQueryFilters().AnyAsync(s => s.StaffId == id));
        Assert.Equal(0, await check.ClockingsStaff.IgnoreQueryFilters().CountAsync(c => c.StafId == id));
        var volunteer = await check.Volunteers.IgnoreQueryFilters().SingleAsync(v => v.EmailAddress == email);
        var dates = await check.Clockings.IgnoreQueryFilters().Where(c => c.VoluntId == volunteer.VolunteerId)
            .OrderBy(c => c.ClockDate).Select(c => c.ClockDate).ToListAsync();
        Assert.Equal(new[] { new DateTime(2026, 2, 2), new DateTime(2026, 2, 3) }, dates);
    }

    [Fact]
    public async Task Volunteer_Create_And_Update()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var email = NewEmail("vcreated");

        using var created = await client.PostJsonAsync("/Volunteer?handler=Create", VolunteerPayload(email));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadJsonAsync()).GetProperty("volunteerId").GetInt64();

        using var updated = await client.PostJsonAsync("/Volunteer?handler=Update", VolunteerPayload(email, "Corporate Volunteering", "Renamed", "Created", id));
        Assert.Equal(HttpStatusCode.BadRequest, updated.StatusCode);
        Assert.Equal("Place of work is required", (await updated.ReadJsonAsync()).GetProperty("errors").GetProperty("PlaceOfWork")[0].GetString());

        using var updatedOk = await client.PostJsonAsync("/Volunteer?handler=Update", VolunteerPayload(email, "Personal or Individual Volunteering", "Renamed", "Created", id));
        Assert.Equal(HttpStatusCode.OK, updatedOk.StatusCode);

        await using var db = _app.CreateDbContext();
        var saved = await db.Volunteers.IgnoreQueryFilters().SingleAsync(v => v.VolunteerId == id);
        Assert.Equal((_app.Data.Alpha.Id, "Volunteer", "Renamed"), (saved.TenantId, saved.Type, saved.FirstName));
    }

    [Fact]
    public async Task Volunteer_Create_Requires_Category_And_Unique_Email()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var noCategory = await client.PostJsonAsync("/Volunteer?handler=Create", VolunteerPayload(NewEmail("nocat"), category: null));
        using var duplicate = await client.PostJsonAsync("/Volunteer?handler=Create", VolunteerPayload("volunteer1@alpha.test"));

        Assert.Equal(HttpStatusCode.BadRequest, noCategory.StatusCode);
        Assert.Equal("Select a volunteer category", (await noCategory.ReadJsonAsync()).GetProperty("errors").GetProperty("VolunteerCategory")[0].GetString());
        Assert.Equal("A volunteer with this email already exists", (await duplicate.ReadJsonAsync()).GetProperty("errors").GetProperty("EmailAddress")[0].GetString());
    }

    [Fact]
    [Trait(Traits.Finding, "F-06")]
    public async Task Volunteer_Delete_Does_Nothing()
    {
        var id = await _app.AddVolunteerAsync(_app.Data.Alpha, "Not", "Deleted");
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostJsonAsync("/Volunteer?handler=Delete", VolunteerPayload(NewEmail("del"), id: id));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = _app.CreateDbContext();
        Assert.True(await db.Volunteers.IgnoreQueryFilters().AnyAsync(v => v.VolunteerId == id));
    }

    [Fact]
    [Trait(Traits.Finding, "F-02")]
    public async Task Volunteer_Move_Creates_Staff_Typed_As_Volunteer()
    {
        var id = await _app.AddVolunteerAsync(_app.Data.Alpha, "Vol", "Mover");
        await AddVolunteerClockingsAsync(id, new DateOnly(2026, 2, 9));
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        string email;
        await using (var db = _app.CreateDbContext()) email = (await db.Volunteers.IgnoreQueryFilters().SingleAsync(v => v.VolunteerId == id)).EmailAddress;

        using var response = await client.PostJsonAsync("/Volunteer?handler=Move", VolunteerPayload(email, first: "Vol", last: "Mover", id: id));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var check = _app.CreateDbContext();
        Assert.False(await check.Volunteers.IgnoreQueryFilters().AnyAsync(v => v.VolunteerId == id));
        var staff = await check.Staffs.IgnoreQueryFilters().SingleAsync(s => s.EmailAddress == email);
        Assert.Equal(_app.Data.Alpha.Id, staff.TenantId);
        Assert.Equal("Volunteer", staff.Type);
        // The single session moves, but without its date (see F-01).
        var moved = Assert.Single(await check.ClockingsStaff.IgnoreQueryFilters().Where(c => c.StafId == staff.StaffId).ToListAsync());
        Assert.Equal(DateTime.MinValue, moved.ClockDate);
    }

    private static async Task<(HttpStatusCode, string)> PostMessageAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostJsonAsync(path, body);
        var text = await response.Content.ReadAsStringAsync();
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(text).RootElement;
            return (response.StatusCode, json.ValueKind == System.Text.Json.JsonValueKind.String ? json.GetString()! : text);
        }
        catch (System.Text.Json.JsonException)
        {
            return (response.StatusCode, text);
        }
    }
}
