using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.Reports;

/// <summary>
/// Report totals for fixed ranges over the seed data. Expected values are worked out by hand:
/// <list type="bullet">
/// <item>normal day: 09:00-17:00 = 8h clocked, 0.5h break, 7.5h worked;</item>
/// <item>Alpha 8 Mar (US change): 01:00 EST-06:00 EDT = 4h clocked, 0.5h break, 3.5h worked;</item>
/// <item>Beta 29 Mar (UK change): 00:30 GMT-05:00 BST = 3.5h clocked, 0.5h break, 3h worked.</item>
/// </list>
/// Per Alpha person (5, 6, 8, 9 Mar): 28h clocked, 2h break, 26h worked.
/// Per Beta person (26, 27, 29, 30 Mar): 27.5h clocked, 2h break, 25.5h worked.
/// </summary>
[Collection(ReadOnlyAppCollection.Name)]
public class ReportTotalsTests
{
    private readonly AppFixture _app;

    public ReportTotalsTests(AppFixture app) => _app = app;

    [Fact]
    public async Task Dashboard_Shows_Counts_And_Worked_Time()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var text = (await client.GetPageAsync("/BackOffice?RangeStart=2026-03-01&RangeEnd=2026-03-31")).Body!.CleanText();

        // Volunteer card: 3 volunteers, 3 worked in the period, 3 x 26h.
        Assert.Contains("Total: 3", text);
        Assert.Contains("Total number of Volunteers who worked for selected Period: 3", text);
        Assert.Contains("Total Working Time for selected Period: 78 Hours 0 Minutes", text);
        // Staff card: all-time, 3 x 26h. Admin card: 2 admins.
        Assert.Contains("Working Time: 78 Hours 0 Minutes", text);
        Assert.Contains("Total: 2", text);
    }

    [Fact]
    public async Task Dashboard_Beta_Worked_Time_Includes_Half_Hours()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Beta);

        var text = (await client.GetPageAsync("/BackOffice?RangeStart=2026-03-01&RangeEnd=2026-03-31")).Body!.CleanText();

        Assert.Contains("Total Working Time for selected Period: 76 Hours 30 Minutes", text);
        Assert.Contains("Working Time: 76 Hours 30 Minutes", text);
    }

    [Fact]
    [Trait(Traits.Finding, "F-04")]
    public async Task Dashboard_Range_End_Date_Is_Not_Included()
    {
        // The "To" date input arrives as midnight at the start of that day, so 9 March (Alpha's
        // last day) is left out: 3 people x (7.5 + 7.5 + 3.5) = 55h30.
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var text = (await client.GetPageAsync("/BackOffice?RangeStart=2026-03-01&RangeEnd=2026-03-09")).Body!.CleanText();

        Assert.Contains("Total Working Time for selected Period: 55 Hours 30 Minutes", text);
    }

    [Fact]
    public async Task HoursWorked_Page_Shows_Totals_For_A_Staff_Member()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var page = await client.GetPageAsync($"/HoursWorked?type=staff&id={_app.Data.Alpha.Staff[0].Id}");

        Assert.Equal("28:00", page.SummaryValue("Total Hours Clocked"));
        Assert.Equal("02:00", page.SummaryValue("Total Hours for Break"));
        Assert.Equal("26:00", page.SummaryValue("Actual Hours Worked"));
    }

    [Fact]
    public async Task HoursWorked_Page_Shows_Totals_For_A_Volunteer()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Beta);

        var page = await client.GetPageAsync($"/HoursWorked?type=volunteer&id={_app.Data.Beta.Volunteers[0].Id}");

        Assert.Equal("27:30", page.SummaryValue("Total Hours Clocked"));
        Assert.Equal("02:00", page.SummaryValue("Total Hours for Break"));
        Assert.Equal("25:30", page.SummaryValue("Actual Hours Worked"));
    }

    [Fact]
    public async Task HoursWorked_Page_Range_Includes_Both_End_Dates()
    {
        // 6 and 8 March: (8 + 4) clocked, 1h break, 11h worked.
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var page = await client.GetPageAsync($"/HoursWorked?type=staff&id={_app.Data.Alpha.Staff[0].Id}&start=2026-03-06&end=2026-03-08");

        Assert.Equal("12:00", page.SummaryValue("Total Hours Clocked"));
        Assert.Equal("01:00", page.SummaryValue("Total Hours for Break"));
        Assert.Equal("11:00", page.SummaryValue("Actual Hours Worked"));
    }

    [Theory]
    [InlineData("staff", "0")]
    [InlineData("volunteer", "0")]
    [InlineData("other", "1")]
    public async Task HoursWorked_Page_Rejects_Bad_Requests(string type, string id)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.GetAsync($"/HoursWorked?type={type}&id={id}");

        Assert.Equal(type == "other" ? System.Net.HttpStatusCode.BadRequest : System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Hours_Api_Staff_Totals_For_A_Month()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var json = await client.GetJsonAsync($"/api/people/staff-hours?id={_app.Data.Alpha.Staff[0].Id}&start=2026-03-01&end=2026-03-31");

        Assert.Equal(28, json.GetProperty("totalClockedHours").GetDouble());
        Assert.Equal(2, json.GetProperty("totalBreakHours").GetDouble());
        Assert.Equal(26, json.GetProperty("actualHoursWorked").GetDouble());
        Assert.Equal("28 hour(s) 00 minutes", json.GetProperty("totalClockedDisplay").GetString());
        Assert.Equal("02 hour(s) 00 minutes", json.GetProperty("totalBreakDisplay").GetString());
        Assert.Equal("26 hour(s) 00 minutes", json.GetProperty("actualWorkedDisplay").GetString());
        Assert.Equal("2026-03-01T00:00:00.0000000", json.GetProperty("start").GetString());
        Assert.Equal("2026-03-31T23:59:59.9999999", json.GetProperty("end").GetString());
    }

    [Fact]
    public async Task Hours_Api_Volunteer_Totals_For_A_Month()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Beta);

        var json = await client.GetJsonAsync($"/api/people/volunteer-hours?id={_app.Data.Beta.Volunteers[0].Id}&start=2026-03-01&end=2026-03-31");

        Assert.Equal(27.5, json.GetProperty("totalClockedHours").GetDouble());
        Assert.Equal(2, json.GetProperty("totalBreakHours").GetDouble());
        Assert.Equal(25.5, json.GetProperty("actualHoursWorked").GetDouble());
        Assert.Equal("25 hour(s) 30 minutes", json.GetProperty("actualWorkedDisplay").GetString());
    }

    [Fact]
    public async Task Hours_Api_Range_Includes_Both_End_Dates()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var json = await client.GetJsonAsync($"/api/people/staff-hours?id={_app.Data.Alpha.Staff[0].Id}&start=2026-03-06&end=2026-03-08");

        Assert.Equal(12, json.GetProperty("totalClockedHours").GetDouble());
        Assert.Equal(11, json.GetProperty("actualHoursWorked").GetDouble());
    }

    [Fact]
    public async Task Hours_Api_Defaults_To_Last_30_Days()
    {
        // The seed data is in March 2026, outside the default window.
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var json = await client.GetJsonAsync($"/api/people/staff-hours?id={_app.Data.Alpha.Staff[0].Id}");

        Assert.Equal(0, json.GetProperty("totalClockedHours").GetDouble());
        Assert.Equal("00 hour(s) 00 minutes", json.GetProperty("actualWorkedDisplay").GetString());
    }

    [Theory]
    [InlineData("staff-hours")]
    [InlineData("volunteer-hours")]
    [InlineData("staff-hours-pdf")]
    [InlineData("volunteer-hours-pdf")]
    public async Task Hours_Api_Rejects_Missing_Id(string endpoint)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.GetAsync($"/api/people/{endpoint}?id=0");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid id", (await response.ReadJsonAsync()).GetProperty("error").GetString());
    }
}
