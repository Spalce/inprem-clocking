using System.Net;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.Kiosk;

/// <summary>Walk-up registration on /StaffAttendance and /VolunteerAttendance (plain form posts).</summary>
public class KioskRegistrationTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public KioskRegistrationTests(AppFixture app) => _app = app;

    // Exactly the fields the kiosk forms have (no Address field on either).
    private static Dictionary<string, string> Form(string kind, string first, string last, string? email = null, string? phone = null)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var fields = new Dictionary<string, string>
        {
            ["Input.EmailAddress"] = email ?? $"walkup-{unique}@alpha.test",
            ["Input.FirstName"] = first,
            ["Input.LastName"] = last,
            ["Input.ZipCode"] = "43229",
            ["Input.Gender"] = "F",
            ["Input.PhoneNumber"] = phone ?? "614" + Random.Shared.NextInt64(1_000_000, 9_999_999),
        };
        if (kind == "volunteer") fields["Input.VolunteerCategory"] = "Personal or Individual Volunteering";
        return fields;
    }

    private static string PagePath(string kind) => kind == "staff" ? "/StaffAttendance" : "/VolunteerAttendance";
    private static string Noun(string kind) => kind == "staff" ? "staff member" : "volunteer";
    private static string Title(string kind) => kind == "staff" ? "Staff" : "Volunteer";

    private async Task<(long Id, int TenantId, string? Type)?> FindByEmailAsync(string kind, string email)
    {
        await using var db = _app.CreateDbContext();
        if (kind == "staff")
        {
            var s = await db.Staffs.IgnoreQueryFilters().Where(x => x.EmailAddress == email).Select(x => new { x.StaffId, x.TenantId, x.Type }).ToListAsync();
            return s.Count == 0 ? null : (s[^1].StaffId, s[^1].TenantId, s[^1].Type);
        }
        var v = await db.Volunteers.IgnoreQueryFilters().Where(x => x.EmailAddress == email).Select(x => new { x.VolunteerId, x.TenantId, x.Type }).ToListAsync();
        return v.Count == 0 ? null : (v[^1].VolunteerId, v[^1].TenantId, v[^1].Type);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_Register_Creates_Person_And_Opens_Clock_Page(string kind)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var form = Form(kind, "Newly", "Registered");

        using var response = await client.PostFormAsync(PagePath(kind), form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var created = await FindByEmailAsync(kind, form["Input.EmailAddress"]);
        Assert.NotNull(created);
        Assert.Equal(_app.Data.Alpha.Id, created.Value.TenantId);
        Assert.Equal(Title(kind), created.Value.Type);
        Assert.Equal($"/{kind}-clockin/{created.Value.Id}", response.Headers.Location!.OriginalString);

        var clockPage = await client.GetPageAsync(response.Headers.Location.OriginalString);
        Assert.Contains($"{Title(kind)} registered successfully!", clockPage.Body!.TextContent);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_Register_Existing_Email_Is_Refused(string kind)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var existing = kind == "staff" ? _app.Data.Alpha.Staff[0].Email : _app.Data.Alpha.Volunteers[0].Email;

        using var response = await client.PostFormAsync(PagePath(kind), Form(kind, "Other", "Person", email: existing.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/{Title(kind)}Attendance", response.Headers.Location!.OriginalString);
        var page = await client.GetPageAsync(PagePath(kind));
        Assert.Contains($"A {Noun(kind)} with this email address is already registered.", page.Body!.TextContent);
    }

    [Theory]
    [InlineData("staff", "6145550101")]
    [InlineData("volunteer", "6145550201")]
    public async Task Kiosk_Register_Existing_Phone_Is_Refused(string kind, string seededPhone)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostFormAsync(PagePath(kind), Form(kind, "Other", "Person", phone: seededPhone));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var page = await client.GetPageAsync(PagePath(kind));
        Assert.Contains($"A {Noun(kind)} with this phone number is already registered.", page.Body!.TextContent);
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_Register_Same_Name_Asks_Is_This_You_Then_Allows_Different_Person(string kind)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var existing = kind == "staff" ? _app.Data.Alpha.Staff[1] : _app.Data.Alpha.Volunteers[1];
        var form = Form(kind, existing.FirstName, existing.LastName);

        using var ask = await client.PostFormAsync(PagePath(kind), form);

        Assert.Equal(HttpStatusCode.OK, ask.StatusCode);
        var page = await ask.ReadDocumentAsync();
        Assert.Contains($"A {Noun(kind)} named {existing.FullName} is already registered.", page.Body!.CleanText());
        Assert.NotNull(page.QuerySelector($"a[href='/{kind}-clockin/{existing.Id}']"));
        Assert.Null(await FindByEmailAsync(kind, form["Input.EmailAddress"]));

        form["ConfirmDifferentPerson"] = "true";
        using var confirmed = await client.PostFormAsync(PagePath(kind), form);

        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.NotNull(await FindByEmailAsync(kind, form["Input.EmailAddress"]));
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_Register_Several_Same_Name_Shows_Masked_Emails(string kind)
    {
        var ids = new List<long>();
        for (var i = 0; i < 2; i++)
        {
            ids.Add(kind == "staff"
                ? await _app.AddStaffAsync(_app.Data.Alpha, "Twin", "Namesake")
                : await _app.AddVolunteerAsync(_app.Data.Alpha, "Twin", "Namesake"));
        }
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostFormAsync(PagePath(kind), Form(kind, "Twin", "Namesake"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadDocumentAsync();
        foreach (var id in ids)
        {
            var link = page.QuerySelector($"a[href='/{kind}-clockin/{id}']");
            Assert.NotNull(link);
            // e.g. "staff-1a2b3c4d5e@alpha.test" is shown as "st***5e@alpha.test".
            Assert.Matches(@"^[a-z]{2}\*\*\*[0-9a-f]{2}@alpha\.test$", link!.CleanText());
        }
    }

    [Theory]
    [InlineData("staff")]
    [InlineData("volunteer")]
    public async Task Kiosk_Register_Validation_Errors_Redisplay_Form(string kind)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var form = Form(kind, "", "Registered");
        form["Input.PhoneNumber"] = "12345";

        using var response = await client.PostFormAsync(PagePath(kind), form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = (await response.ReadDocumentAsync()).Body!.CleanText();
        Assert.Contains("First name is required", text);
        Assert.Contains("Phone Number cannot be less than 10 digits", text);
        Assert.Null(await FindByEmailAsync(kind, form["Input.EmailAddress"]));
    }

    [Theory]
    [InlineData("", "Select a volunteer category")]
    [InlineData("Mandated Community Hours", "Please select which mandate applies")]
    [InlineData("Educational Purposes", "Name of institution is required")]
    [InlineData("Corporate Volunteering", "Place of work is required")]
    public async Task Kiosk_Register_Volunteer_Category_Rules(string category, string expectedError)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var form = Form("volunteer", "Category", "Checker");
        form["Input.VolunteerCategory"] = category;

        using var response = await client.PostFormAsync("/VolunteerAttendance", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedError, (await response.ReadDocumentAsync()).Body!.CleanText());
        Assert.Null(await FindByEmailAsync("volunteer", form["Input.EmailAddress"]));
    }

    [Fact]
    public async Task Kiosk_Register_Mandated_Volunteer_With_Mandate_Type_Is_Accepted()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);
        var form = Form("volunteer", "Mandated", "Volunteer");
        form["Input.VolunteerCategory"] = "Mandated Community Hours";
        form["Input.MandateType"] = "Diversion";

        using var response = await client.PostFormAsync("/VolunteerAttendance", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = _app.CreateDbContext();
        var saved = await db.Volunteers.IgnoreQueryFilters().SingleAsync(v => v.EmailAddress == form["Input.EmailAddress"]);
        Assert.Equal("Mandated Community Hours", saved.VolunteerCategory);
        Assert.Equal("Diversion", saved.MandateType);
    }
}
