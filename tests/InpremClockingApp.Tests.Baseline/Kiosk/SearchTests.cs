using System.Net;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.Kiosk;

/// <summary>
/// Search APIs used by the kiosk pages (api/search/...) and the back-office lookups
/// (api/people/emails, api/people/staff, api/people/volunteers), plus the kiosk pages themselves.
/// </summary>
[Collection(ReadOnlyAppCollection.Name)]
public class SearchTests
{
    private readonly AppFixture _app;

    public SearchTests(AppFixture app) => _app = app;

    [Theory]
    [InlineData("/StaffAttendance")]
    [InlineData("/VolunteerAttendance")]
    public async Task Kiosk_Attendance_Pages_Load(string path)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var page = await client.GetPageAsync(path);

        Assert.NotNull(page.QuerySelector("form[method='post']"));
        Assert.NotNull(page.QuerySelector("input[name='Input.EmailAddress']"));
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_Clock_Page_Loads_For_A_Person(string kind)
    {
        var id = kind == "staff" ? _app.Data.Alpha.Staff[0].Id : _app.Data.Alpha.Volunteers[0].Id;
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.GetAsync($"/{kind}-clockin/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"/api/controls/{kind}-clockin/", html);
    }

    [Fact]
    public async Task Kiosk_Search_Staff_By_Name_Or_Email()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var byName = await client.GetJsonAsync("/api/search/staff?term=Avery");
        var byEmail = await client.GetJsonAsync("/api/search/staff?term=staff2@");

        var hit = Assert.Single(byName.EnumerateArray());
        Assert.Equal("staff1@alpha.test", hit.GetProperty("email").GetString());
        Assert.Equal("Avery Stone", hit.GetProperty("name").GetString());
        Assert.Equal("Blake Rivers", Assert.Single(byEmail.EnumerateArray()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Kiosk_Search_Volunteer_And_Fetch_Record_By_Email()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var hits = await client.GetJsonAsync("/api/search/volunteer?term=");
        var record = await client.GetJsonAsync("/api/search/volunteer-data/volunteer2@alpha.test");

        Assert.Equal(3, hits.GetArrayLength());
        Assert.Equal(_app.Data.Alpha.Volunteers[1].Id, record.GetProperty("volunteerId").GetInt64());
        Assert.Equal("Emery", record.GetProperty("firstName").GetString());
    }

    [Fact]
    public async Task Kiosk_Fetch_Staff_Record_By_Email()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var record = await client.GetJsonAsync("/api/search/staff-data/staff3@alpha.test");

        Assert.Equal(_app.Data.Alpha.Staff[2].Id, record.GetProperty("staffId").GetInt64());
    }

    [Fact]
    public async Task BackOffice_Email_Suggestions_By_Type()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var staff = await client.GetJsonAsync("/api/people/emails?type=staff&q=staff");
        var volunteers = await client.GetJsonAsync("/api/people/emails?type=volunteer&q=staff");
        // An empty q never reaches the handler's own "empty -> []" check: binding rejects it first.
        using var empty = await client.GetAsync("/api/people/emails?type=staff&q=");

        Assert.Equal(new[] { "staff1@alpha.test", "staff2@alpha.test", "staff3@alpha.test" },
            staff.EnumerateArray().Select(e => e.GetProperty("email").GetString()));
        Assert.Equal("Avery Stone", staff[0].GetProperty("name").GetString());
        Assert.Equal(0, volunteers.GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    [Trait(Traits.Finding, "F-09")]
    public async Task BackOffice_Email_Suggestions_Without_Type_Fail()
    {
        // Without "type" the endpoint runs its staff and volunteer queries at the same time on
        // one DbContext, which EF Core does not allow. Every current caller passes "type".
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.GetAsync("/api/people/emails?q=s");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task BackOffice_People_Lookup_Pages_Results()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var all = await client.GetJsonAsync("/api/people/staff?q=%40&page=1&pageSize=10");
        var one = await client.GetJsonAsync("/api/people/volunteers?q=Finley&page=1&pageSize=1");

        Assert.Equal(3, all.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "Avery Stone", "Blake Rivers", "Casey Fields" },
            all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("text").GetString()));
        var finley = Assert.Single(one.GetProperty("items").EnumerateArray());
        Assert.Equal(_app.Data.Alpha.Volunteers[2].Id, finley.GetProperty("id").GetInt64());
        Assert.Equal("volunteer3@alpha.test", finley.GetProperty("email").GetString());
    }

    [Fact]
    public async Task BackOffice_People_Lookup_Requires_A_Query()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.GetAsync("/api/people/staff?q=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
