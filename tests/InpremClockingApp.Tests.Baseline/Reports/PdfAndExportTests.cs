using System.Net;
using System.Text;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.Reports;

/// <summary>
/// Every PDF download is a valid PDF whose text carries the organization header, the right
/// people and the right totals (see ReportTotalsTests for how the totals are derived).
/// </summary>
[Collection(ReadOnlyAppCollection.Name)]
public class PdfAndExportTests
{
    private readonly AppFixture _app;

    public PdfAndExportTests(AppFixture app) => _app = app;

    [Fact]
    public async Task Pdf_Staff_Hours_Confirmation()
    {
        var avery = _app.Data.Alpha.Staff[0];
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var text = await client.GetPdfTextAsync($"/api/people/staff-hours-pdf?id={avery.Id}&start=2026-03-01&end=2026-03-31T23:59:59");

        Assert.StartsWith("Alpha", text);
        Assert.Contains("This is a confirmation of staff hours worked by Avery Stone at Alpha, 1 Alpha Test Street, Testville.", text);
        Assert.Contains("Staff Hours Report", text);
        Assert.Contains("Staff: Avery Stone", text);
        Assert.Contains("Period: 01 Mar 2026 - 31 Mar 2026", text);
        // Session rows in Alpha's local time, including the clock-change day.
        Assert.Contains("Avery Stone 05/03/2026 09:00 05/03/2026 17:00 05/03/2026 12:00 05/03/2026 12:30", text);
        Assert.Contains("Avery Stone 08/03/2026 01:00 08/03/2026 06:00 08/03/2026 03:00 08/03/2026 03:30", text);
        Assert.Contains("Total Hours Clocked: 28 hour(s) 00 minutes", text);
        Assert.Contains("Total Hours for Break: 02 hour(s) 00 minutes", text);
        Assert.Contains("Actual Hours Worked: 26 hour(s) 00 minutes", text);
    }

    [Fact]
    public async Task Pdf_Volunteer_Hours_Confirmation()
    {
        var jules = _app.Data.Beta.Volunteers[0];
        using var client = await _app.AdminOfAsync(_app.Data.Beta);

        var text = await client.GetPdfTextAsync($"/api/people/volunteer-hours-pdf?id={jules.Id}&start=2026-03-01&end=2026-03-31T23:59:59");

        Assert.StartsWith("Beta", text);
        Assert.Contains("Volunteer Hours Report", text);
        Assert.Contains("Volunteer: Jules Ford", text);
        Assert.Contains("Jules Ford 29/03/2026 00:30 29/03/2026 05:00 29/03/2026 02:00 29/03/2026 02:30", text);
        Assert.Contains("Total Hours Clocked: 27 hour(s) 30 minutes", text);
        Assert.Contains("Actual Hours Worked: 25 hour(s) 30 minutes", text);
    }

    [Fact]
    [Trait(Traits.Finding, "F-03")]
    public async Task Pdf_From_HoursWorked_Button_Leaves_Out_The_End_Date()
    {
        // The Hours Worked page's Download PDF button sends start/end as midnight of each date,
        // and the PDF endpoint uses end as-is: for 6-8 March only 6 March is in the PDF
        // (7h30), while the page itself shows 11:00 for the same dates.
        var avery = _app.Data.Alpha.Staff[0];
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var text = await client.GetPdfTextAsync(
            $"/api/people/staff-hours-pdf?id={avery.Id}&start=2026-03-06T00%3A00%3A00.0000000&end=2026-03-08T00%3A00%3A00.0000000");

        Assert.Contains("Actual Hours Worked: 07 hour(s) 30 minutes", text);
        Assert.DoesNotContain("08/03/2026", text);
    }

    [Fact]
    public async Task Pdf_Hours_For_Unknown_Person_Is_Not_Found()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var staff = await client.GetAsync("/api/people/staff-hours-pdf?id=999999");
        using var volunteer = await client.GetAsync("/api/people/volunteer-hours-pdf?id=999999");

        Assert.Equal(HttpStatusCode.NotFound, staff.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, volunteer.StatusCode);
    }

    [Fact]
    public async Task Pdf_Staff_Clocking_Report_All_And_One()
    {
        var avery = _app.Data.Alpha.Staff[0];
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var all = await client.GetPdfTextAsync("/api/people/staff-clocking-report-pdf?start=2026-03-01T00:00&end=2026-03-31T23:59");
        var one = await client.GetPdfTextAsync($"/api/people/staff-clocking-report-pdf?staffId={avery.Id}&start=2026-03-01T00:00&end=2026-03-31T23:59");

        Assert.StartsWith("Alpha", all);
        Assert.Contains("Staff Clocking Report", all);
        Assert.Contains("Staff: All Staff", all);
        Assert.Contains("Avery Stone 2026-03-08 01:00:00 06:00:00 03:00:00 03:30:00 03:30:00", all);
        Assert.Contains("Total Hours: 78:00:00", all);
        Assert.Contains("Staff: Avery Stone", one);
        Assert.DoesNotContain("Blake Rivers", one);
        Assert.Contains("Total Hours: 26:00:00", one);
    }

    [Fact]
    public async Task Pdf_Volunteer_Clocking_Report()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Beta);

        var text = await client.GetPdfTextAsync("/api/people/volunteer-clocking-report-pdf?start=2026-03-01T00:00&end=2026-03-31T23:59");

        Assert.StartsWith("Beta", text);
        Assert.Contains("Volunteer Clocking Report", text);
        Assert.Contains("Total Hours: 76:30:00", text);
    }

    [Fact]
    public async Task Pdf_Staff_And_Volunteer_Lists()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var staff = await client.GetPdfTextAsync("/api/people/staff-list-pdf");
        var volunteers = await client.GetPdfTextAsync("/api/people/volunteer-list-pdf");

        Assert.Contains("Staff Report", staff);
        Assert.Contains("Total Staff: 3", staff);
        Assert.Contains("staff1@alpha.test", staff);
        Assert.Contains("Volunteer Report", volunteers);
        Assert.Contains("Total Volunteers: 3", volunteers);
        Assert.Contains("volunteer1@alpha.test", volunteers);
    }

    [Fact]
    public async Task Pdf_BackOffice_Volunteer_Report()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var text = await client.GetPdfTextAsync("/BackOffice?handler=DownloadReport&RangeStart=2026-03-01&RangeEnd=2026-03-31");

        Assert.StartsWith("Alpha", text);
        Assert.Contains("Volunteer Report", text);
        Assert.Contains("Total number of Volunteers that clocked in for the selected period: 3", text);
        Assert.Contains("Total hours worked by volunteers for the selected period: 78 hours", text);
        Assert.Contains("2026 March 78", text);
    }

    [Fact]
    [Trait(Traits.Finding, "F-05")]
    public async Task Pdf_Footer_Says_Inprem_Admin_For_Every_Organization()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Beta);

        var text = await client.GetPdfTextAsync("/api/people/staff-list-pdf");

        Assert.Contains("Please contact us for further information. Inprem Admin", text);
    }

    [Fact]
    public async Task Export_Staff_Csv_Excel_Pdf_Json()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var csv = await client.GetAsync("/Staff?handler=ExportCsv");
        var lines = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync()).TrimStart('﻿')
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Equal("staff.csv", csv.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("ID,First Name,Last Name,Email,Gender,Phone,Zip", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Contains($"{_app.Data.Alpha.Staff[0].Id},Avery,Stone,staff1@alpha.test,F,6145550101,43229", lines);

        using var excel = await client.GetAsync("/Staff?handler=ExportExcel");
        Assert.Equal("application/vnd.ms-excel", excel.Content.Headers.ContentType?.MediaType);
        Assert.Contains("staff1@alpha.test", await excel.Content.ReadAsStringAsync());

        var pdf = await client.GetPdfTextAsync("/Staff?handler=ExportPdf");
        Assert.Contains("Staff List", pdf);
        Assert.Contains("Total: 3", pdf);

        var json = await client.GetJsonAsync("/Staff?handler=ExportJson");
        Assert.Equal(3, json.GetArrayLength());
    }

    [Fact]
    public async Task Export_Staff_Respects_Search()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        var json = await client.GetJsonAsync("/Staff?handler=ExportJson&Search=Casey");

        Assert.Equal(1, json.GetArrayLength());
    }

    [Fact]
    public async Task Export_Volunteer_Csv_Includes_Category()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var csv = await client.GetAsync("/Volunteer?handler=ExportCsv");
        var lines = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync()).TrimStart('﻿')
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("ID,First Name,Last Name,Email,Gender,Phone,Zip,Category", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Contains($"{_app.Data.Alpha.Volunteers[0].Id},Dana,Brooks,volunteer1@alpha.test,M,6145550201,43229,Personal or Individual Volunteering", lines);
    }
}
