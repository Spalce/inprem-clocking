using System.Net;
using System.Text;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.Isolation;

/// <summary>
/// Tenant isolation as it is today, for reads: Alpha's admin opens every page, API and download,
/// including with Beta's ids, and never sees any of Beta's people or emails. Ids are filled in
/// from the seed: {betaStaff}, {betaVolunteer}, {betaStaffEmail}, {betaVolunteerEmail}.
/// </summary>
[Collection(ReadOnlyAppCollection.Name)]
public class IsolationReadTests
{
    private readonly AppFixture _app;

    public IsolationReadTests(AppFixture app) => _app = app;

    public static TheoryData<string> AlphaGets => new()
    {
        "/BackOffice?RangeStart=2026-03-01&RangeEnd=2026-03-31", "/Staff", "/Volunteer", "/User",
        "/StaffClocking", "/VolunteerClocking", "/StaffReport", "/VolunteerReport",
        "/StaffClockingReport?Start=2026-03-01T00:00&End=2026-03-31T23:59",
        "/VolunteerClockingReport?Start=2026-03-01T00:00&End=2026-03-31T23:59",
        "/StaffClockingReport?Start=2026-03-01T00:00&End=2026-03-31T23:59&StaffId={betaStaff}",
        "/Staff?handler=ExportCsv", "/Volunteer?handler=ExportCsv", "/Staff?handler=ExportJson", "/Volunteer?handler=ExportJson",
        "/api/people/emails?type=staff&q=staff", "/api/people/emails?type=volunteer&q=volunteer",
        "/api/people/staff?q=%40", "/api/people/volunteers?q=%40",
        "/api/people/staff-hours?id={betaStaff}&start=2026-03-01&end=2026-03-31",
        "/api/people/volunteer-hours?id={betaVolunteer}&start=2026-03-01&end=2026-03-31",
        "/api/search/staff?term=", "/api/search/volunteer?term=",
        "/api/search/staff?term=Gale", "/api/search/volunteer?term=Jules",
        "/api/staff/all-staff", "/api/staff/all-Volunteer",
    };

    public static TheoryData<string> AlphaPdfs => new()
    {
        "/api/people/staff-list-pdf", "/api/people/volunteer-list-pdf",
        "/api/people/staff-clocking-report-pdf?start=2026-03-01T00:00&end=2026-03-31T23:59",
        "/api/people/volunteer-clocking-report-pdf?start=2026-03-01T00:00&end=2026-03-31T23:59",
        "/api/people/staff-clocking-report-pdf?staffId={betaStaff}&start=2026-03-01T00:00&end=2026-03-31T23:59",
        "/api/people/volunteer-clocking-report-pdf?volunteerId={betaVolunteer}&start=2026-03-01T00:00&end=2026-03-31T23:59",
        "/BackOffice?handler=DownloadReport&RangeStart=2026-03-01&RangeEnd=2026-03-31",
        "/Staff?handler=ExportPdf", "/Volunteer?handler=ExportPdf",
    };

    private string Fill(string path) => path
        .Replace("{betaStaff}", _app.Data.Beta.Staff[0].Id.ToString())
        .Replace("{betaVolunteer}", _app.Data.Beta.Volunteers[0].Id.ToString())
        .Replace("{betaStaffEmail}", Uri.EscapeDataString(_app.Data.Beta.Staff[0].Email))
        .Replace("{betaVolunteerEmail}", Uri.EscapeDataString(_app.Data.Beta.Volunteers[0].Email));

    private IEnumerable<string> BetaMarkers =>
        _app.Data.Beta.Staff.Concat(_app.Data.Beta.Volunteers).SelectMany(p => new[] { p.Email, p.FullName })
            .Concat(new[] { "owner@beta.test", "admin@beta.test", "beta.test" });

    private void AssertNoBetaData(string text, string path)
    {
        foreach (var marker in BetaMarkers)
        {
            Assert.False(text.Contains(marker, StringComparison.OrdinalIgnoreCase), $"{path} shows Beta's '{marker}'.");
        }
    }

    [Theory]
    [MemberData(nameof(AlphaGets))]
    public async Task Isolation_Alpha_Admin_Sees_No_Beta_Data_In_Pages_And_Apis(string path)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var url = Fill(path);

        using var response = await client.GetAsync(url);

        Assert.True(response.IsSuccessStatusCode, $"{url} returned {(int)response.StatusCode}");
        AssertNoBetaData(Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()), url);
    }

    [Theory]
    [MemberData(nameof(AlphaPdfs))]
    public async Task Isolation_Alpha_Admin_Sees_No_Beta_Data_In_Pdfs(string path)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var url = Fill(path);

        var text = await client.GetPdfTextAsync(url);

        AssertNoBetaData(text, url);
    }

    [Fact]
    public async Task Isolation_Beta_Ids_Look_Like_Unknown_Ids_To_Alpha()
    {
        var betaStaff = _app.Data.Beta.Staff[0].Id;
        var betaVolunteer = _app.Data.Beta.Volunteers[0].Id;
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var staffDetails = await client.GetAsync($"/StaffDetails?id={betaStaff}");
        using var volunteerDetails = await client.GetAsync($"/VolunteerDetails?id={betaVolunteer}");
        using var staffHours = await client.GetAsync($"/HoursWorked?type=staff&id={betaStaff}");
        using var volunteerHours = await client.GetAsync($"/HoursWorked?type=volunteer&id={betaVolunteer}");
        using var staffPdf = await client.GetAsync($"/api/people/staff-hours-pdf?id={betaStaff}");
        using var volunteerPdf = await client.GetAsync($"/api/people/volunteer-hours-pdf?id={betaVolunteer}");
        using var staffData = await client.GetAsync($"/api/search/staff-data/{Uri.EscapeDataString(_app.Data.Beta.Staff[0].Email)}");
        using var volunteerData = await client.GetAsync($"/api/search/volunteer-data/{Uri.EscapeDataString(_app.Data.Beta.Volunteers[0].Email)}");

        Assert.Equal((HttpStatusCode.Redirect, "/Staff"), (staffDetails.StatusCode, staffDetails.Headers.Location?.OriginalString));
        Assert.Equal((HttpStatusCode.Redirect, "/Volunteer"), (volunteerDetails.StatusCode, volunteerDetails.Headers.Location?.OriginalString));
        Assert.Equal(HttpStatusCode.NotFound, staffHours.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, volunteerHours.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, staffPdf.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, volunteerPdf.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, staffData.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, volunteerData.StatusCode);
    }

    [Fact]
    public async Task Isolation_Beta_Hours_Requested_By_Alpha_Are_Zero()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var json = await client.GetJsonAsync($"/api/people/staff-hours?id={_app.Data.Beta.Staff[0].Id}&start=2026-03-01&end=2026-03-31");

        Assert.Equal(0, json.GetProperty("totalClockedHours").GetDouble());
    }

    [Theory]
    [InlineData("/api/reports/staff-clocking-one", "staff")]
    [InlineData("/api/reports/volunteer-clocking-one", "volunteer")]
    public async Task Isolation_Report_Api_With_Beta_Id_Returns_No_Rows(string path, string kind)
    {
        var betaId = kind == "staff" ? _app.Data.Beta.Staff[0].Id : _app.Data.Beta.Volunteers[0].Id;
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostJsonAsync(path, new { Id = betaId, StartDate = "2026-03-01", EndDate = "2026-03-31" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        AssertNoBetaData(text, path);
        Assert.Equal(0, (await response.ReadJsonAsync()).GetProperty("contents").GetArrayLength());
    }

    [Fact]
    public async Task Isolation_Report_Api_With_Own_Id_Returns_Rows()
    {
        // Control for the test above: the same request with Alpha's own id does return data.
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostJsonAsync("/api/reports/staff-clocking-one",
            new { Id = _app.Data.Alpha.Staff[0].Id, StartDate = "2026-03-01", EndDate = "2026-03-31" });

        var json = await response.ReadJsonAsync();
        Assert.Equal(4, json.GetProperty("contents").GetArrayLength());
        Assert.Equal("26 Hours 0 Minutes", json.GetProperty("detail").GetProperty("duration").GetString());
        Assert.Equal("Alpha", json.GetProperty("detail").GetProperty("company").GetString());
    }

    [Fact]
    public async Task Isolation_Beta_Admin_Sees_Only_Beta()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Beta);

        var text = (await client.GetPageAsync("/Staff")).Body!.CleanText();

        Assert.Contains("staff1@beta.test", text);
        Assert.DoesNotContain("alpha.test", text);
    }
}
