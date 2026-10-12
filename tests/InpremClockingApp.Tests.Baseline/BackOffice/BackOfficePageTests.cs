using System.Net;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.BackOffice;

/// <summary>
/// Every back-office page loads for an organization admin and shows that organization's data.
/// Seed facts used below (see TestDataSeeder): Alpha staff Avery Stone, Blake Rivers, Casey Fields;
/// volunteers Dana Brooks, Emery Woods, Finley Hart; every person has 4 sessions in March 2026.
/// </summary>
[Collection(ReadOnlyAppCollection.Name)]
public class BackOfficePageTests
{
    private readonly AppFixture _app;

    public BackOfficePageTests(AppFixture app) => _app = app;

    public static TheoryData<string> AdminPages => new()
    {
        "/BackOffice", "/Staff", "/Volunteer", "/User", "/StaffClocking", "/VolunteerClocking",
        "/StaffReport", "/VolunteerReport", "/StaffClockingReport", "/VolunteerClockingReport",
    };

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task BackOffice_Page_Loads_For_Admin(string path)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task BackOffice_Staff_List_Shows_The_Organizations_Staff()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var page = await client.GetPageAsync("/Staff");

        var rows = page.QuerySelectorAll("tr[data-id]");
        Assert.Equal(_app.Data.Alpha.Staff.Select(s => s.Id.ToString()).OrderBy(x => x), rows.Select(r => r.GetAttribute("data-id")!).OrderBy(x => x));
        var text = page.Body!.CleanText();
        Assert.Contains("Avery", text);
        Assert.Contains("staff1@alpha.test", text);
    }

    [Fact]
    public async Task BackOffice_Staff_Search_Filters_By_Name_Or_Email()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var byName = await client.GetPageAsync("/Staff?Search=Blake");
        var byEmail = await client.GetPageAsync("/Staff?Search=staff3@");

        Assert.Equal(new[] { _app.Data.Alpha.Staff[1].Id.ToString() }, byName.QuerySelectorAll("tr[data-id]").Select(r => r.GetAttribute("data-id")));
        Assert.Equal(new[] { _app.Data.Alpha.Staff[2].Id.ToString() }, byEmail.QuerySelectorAll("tr[data-id]").Select(r => r.GetAttribute("data-id")));
    }

    [Fact]
    public async Task BackOffice_Volunteer_List_Shows_The_Organizations_Volunteers()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var text = (await client.GetPageAsync("/Volunteer")).Body!.CleanText();

        foreach (var v in _app.Data.Alpha.Volunteers)
        {
            Assert.Contains(v.Email, text);
        }
    }

    [Fact]
    public async Task BackOffice_Staff_Details_Shows_The_Person()
    {
        var avery = _app.Data.Alpha.Staff[0];
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var text = (await client.GetPageAsync($"/StaffDetails?id={avery.Id}")).Body!.CleanText();

        Assert.Contains("Avery Stone", text);
        Assert.Contains($"ID: {avery.Id}", text);
        Assert.Contains("Email Address: staff1@alpha.test", text);
    }

    [Fact]
    public async Task BackOffice_Volunteer_Details_Shows_The_Person()
    {
        var dana = _app.Data.Alpha.Volunteers[0];
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var text = (await client.GetPageAsync($"/VolunteerDetails?id={dana.Id}")).Body!.CleanText();

        Assert.Contains("Dana Brooks", text);
        Assert.Contains("Email Address: volunteer1@alpha.test", text);
    }

    [Fact]
    public async Task BackOffice_Details_Of_Unknown_Id_Redirects_To_List()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var staff = await client.GetAsync("/StaffDetails?id=999999");
        using var volunteer = await client.GetAsync("/VolunteerDetails?id=999999");

        Assert.Equal(HttpStatusCode.Redirect, staff.StatusCode);
        Assert.Equal("/Staff", staff.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, volunteer.StatusCode);
        Assert.Equal("/Volunteer", volunteer.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task BackOffice_Admins_Page_Lists_The_Organizations_Admins()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var text = (await client.GetPageAsync("/User")).Body!.CleanText();

        Assert.Contains("owner@alpha.test", text);
        Assert.Contains("admin@alpha.test", text);
    }

    [Fact]
    public async Task BackOffice_Staff_Clocking_Lists_Sessions_In_Local_Time()
    {
        var avery = _app.Data.Alpha.Staff[0];
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var page = await client.GetPageAsync("/StaffClocking");

        var rows = page.QuerySelectorAll("tr[data-full-name]").ToList();
        Assert.Equal(12, rows.Count);
        // 8 March (US clock change): 01:00 EST in, 06:00 EDT out, 3h30 worked.
        var dstRow = rows.Single(r => r.GetAttribute("data-full-name") == "Avery Stone" && r.GetAttribute("data-clock-in") == "2026-03-08T01:00");
        Assert.Equal("2026-03-08T06:00", dstRow.GetAttribute("data-clock-out"));
        var cells = dstRow.QuerySelectorAll("td").Select(c => c.CleanText()).ToList();
        Assert.Equal(new[] { "Avery Stone", "2026-03-08", "01:00:00", "06:00:00", "03:00:00", "03:30:00", "03:30:00" }, cells.Take(7));
    }

    [Fact]
    public async Task BackOffice_Volunteer_Clocking_Lists_Sessions_In_Local_Time()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Beta);

        var page = await client.GetPageAsync("/VolunteerClocking");

        var rows = page.QuerySelectorAll("tr[data-full-name]").ToList();
        Assert.Equal(12, rows.Count);
        // 29 March (UK clock change): 00:30 GMT in, 05:00 BST out, 3h worked.
        var dstRow = rows.Single(r => r.GetAttribute("data-full-name") == "Jules Ford" && r.GetAttribute("data-clock-in") == "2026-03-29T00:30");
        Assert.Equal("2026-03-29T05:00", dstRow.GetAttribute("data-clock-out"));
    }

    [Fact]
    public async Task BackOffice_Staff_Clocking_Report_Filters_By_Range_And_Person()
    {
        var avery = _app.Data.Alpha.Staff[0];
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var all = await client.GetPageAsync("/StaffClockingReport?Start=2026-03-01T00:00&End=2026-03-31T23:59");
        var one = await client.GetPageAsync($"/StaffClockingReport?Start=2026-03-01T00:00&End=2026-03-31T23:59&StaffId={avery.Id}");
        var part = await client.GetPageAsync("/StaffClockingReport?Start=2026-03-06T00:00&End=2026-03-08T23:59");

        Assert.Equal(12, ReportRows(all));
        Assert.Equal(4, ReportRows(one));
        Assert.Equal(6, ReportRows(part));
    }

    [Fact]
    public async Task BackOffice_Volunteer_Clocking_Report_Filters_By_Range()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var all = await client.GetPageAsync("/VolunteerClockingReport?Start=2026-03-01T00:00&End=2026-03-31T23:59");

        Assert.Equal(12, ReportRows(all));
    }

    [Fact]
    public async Task BackOffice_Clocking_Report_Rejects_Start_After_End()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var page = await client.GetPageAsync("/StaffClockingReport?Start=2026-03-31T00:00&End=2026-03-01T00:00");

        Assert.Contains("Start must be before or equal to End.", page.Body!.CleanText());
        Assert.Equal(0, ReportRows(page));
    }

    // Report rows are the table body rows whose first cell is a person's name.
    private static int ReportRows(AngleSharp.Dom.IDocument page) =>
        page.QuerySelectorAll("tbody tr").Count(r => r.QuerySelectorAll("td").Length >= 7);
}
