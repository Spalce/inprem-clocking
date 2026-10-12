using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit;

namespace InpremClockingApp.Tests.Ui.Kiosk;

/// <summary>
/// The JavaScript-driven parts of the approved app in a real browser: the kiosk clock buttons
/// (jQuery AJAX + toast messages + redirect back to the attendance page), kiosk registration,
/// and the Manage Staff editor dialog.
/// </summary>
[Trait("Category", "Ui")]
public class KioskBrowserTests : IClassFixture<KestrelAppFixture>, IAsyncLifetime
{
    private readonly KestrelAppFixture _app;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public KioskBrowserTests(KestrelAppFixture app) => _app = app;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    private async Task<IPage> SignedInPageAsync()
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.ServerAddress.ToString() });
        var page = await context.NewPageAsync();
        await page.GotoAsync(SignIn.LoginPath);
        await page.FillAsync("input[name='Input.Email']", _app.Data.Alpha.Admin!.Email);
        await page.FillAsync("input[name='Input.Password']", _app.Data.Password);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync("**/VolunteerAttendance");
        return page;
    }

    private async Task<long> AddStaffAsync(string first, string last)
    {
        await using var db = _app.CreateDbContext();
        var staff = new Models.Staff
        {
            TenantId = _app.Data.Alpha.Id, EmailAddress = $"ui-{Guid.NewGuid():N}"[..15] + "@alpha.test",
            FirstName = first, LastName = last, ZipCode = "43229", Gender = 'F', Type = "Staff",
            PhoneNumber = "6145551234", Address = "3 Browser Road, Testville", CreatedAt = DateTime.UtcNow,
        };
        db.Staffs.Add(staff);
        await db.SaveChangesAsync();
        return staff.StaffId;
    }

    private static async Task<string> ToastAsync(IPage page, string kind)
    {
        var toast = page.Locator($".alerts .alert.alert-{kind}");
        await toast.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        return (await toast.InnerTextAsync()).Trim();
    }

    [Fact]
    public async Task Ui_Kiosk_Staff_Clock_Buttons_Show_Messages_And_Return_To_Attendance()
    {
        var id = await AddStaffAsync("Button", "Presser");
        var page = await SignedInPageAsync();

        await page.GotoAsync($"/staff-clockin/{id}");
        await page.ClickAsync("#clockin");
        Assert.Contains("You have successfully clocked in", await ToastAsync(page, "success"));
        await page.WaitForURLAsync("**/StaffAttendance");

        await page.GotoAsync($"/staff-clockin/{id}");
        await page.ClickAsync("#clockin");
        Assert.Contains("You have already clocked in today.", await ToastAsync(page, "warning"));

        await page.ClickAsync("#leavein");
        Assert.Contains("You have successfully clocked for a leave out", await ToastAsync(page, "success"));
        await page.WaitForURLAsync("**/StaffAttendance");

        await page.GotoAsync($"/staff-clockin/{id}");
        await page.ClickAsync("#leaveout");
        Assert.Contains("You have successfully clocked to have returned fom break", await ToastAsync(page, "success"));
        await page.WaitForURLAsync("**/StaffAttendance");

        await page.GotoAsync($"/staff-clockin/{id}");
        await page.ClickAsync("#clockout");
        Assert.Contains("You have successfully clocked out", await ToastAsync(page, "success"));

        await using var db = _app.CreateDbContext();
        var row = await db.ClockingsStaff.IgnoreQueryFilters().SingleAsync(c => c.StafId == id);
        Assert.NotNull(row.ClockOutTime);
        Assert.Equal((row.ClockOutTime - row.ClockInTime) - (row.ReturnOnBreakTime - row.LeaveOnBreakTime), row.WorkingHours);
    }

    [Fact]
    public async Task Ui_Kiosk_Volunteer_Clock_In_Button()
    {
        long id;
        await using (var db = _app.CreateDbContext())
        {
            var volunteer = new Models.Volunteer
            {
                TenantId = _app.Data.Alpha.Id, EmailAddress = $"uiv-{Guid.NewGuid():N}"[..15] + "@alpha.test",
                FirstName = "Volunteer", LastName = "Presser", ZipCode = "43229", Gender = 'M', Type = "Volunteer",
                PhoneNumber = "6145554321", Address = "4 Browser Road, Testville", CreatedAt = DateTime.UtcNow,
                VolunteerCategory = Models.VolunteerCategories.PersonalOrIndividualVolunteering,
            };
            db.Volunteers.Add(volunteer);
            await db.SaveChangesAsync();
            id = volunteer.VolunteerId;
        }
        var page = await SignedInPageAsync();

        await page.GotoAsync($"/volunteer-clockin/{id}");
        await page.ClickAsync("#clockin");

        Assert.Contains("You have successfully clocked in", await ToastAsync(page, "success"));
        await page.WaitForURLAsync("**/VolunteerAttendance");
    }

    [Fact]
    public async Task Ui_Kiosk_Staff_Registration_Opens_Clock_Page_With_Welcome()
    {
        var page = await SignedInPageAsync();
        var email = $"uireg-{Guid.NewGuid():N}"[..15] + "@alpha.test";

        await page.GotoAsync("/StaffAttendance");
        var form = page.Locator("#registerForm");
        await form.Locator("input[name='Input.FirstName']").FillAsync("Browser");
        await form.Locator("input[name='Input.LastName']").FillAsync("Registrant");
        await form.Locator("input[name='Input.EmailAddress']").FillAsync(email);
        await form.Locator("input[name='Input.PhoneNumber']").FillAsync("6145556789");
        await form.Locator("input[name='Input.ZipCode']").FillAsync("43229");
        await form.Locator("select[name='Input.Gender']").SelectOptionAsync("F");
        await page.ClickAsync("#registerSubmit");

        await page.WaitForURLAsync("**/staff-clockin/*");
        Assert.Contains("Staff registered successfully!", await ToastAsync(page, "success"));
        await using var db = _app.CreateDbContext();
        Assert.Equal(_app.Data.Alpha.Id, (await db.Staffs.IgnoreQueryFilters().SingleAsync(s => s.EmailAddress == email)).TenantId);
    }

    [Fact]
    public async Task Ui_Manage_Staff_Add_New_Dialog_Creates_Staff()
    {
        var page = await SignedInPageAsync();
        var email = $"uiadd-{Guid.NewGuid():N}"[..15] + "@alpha.test";

        await page.GotoAsync("/Staff");
        await page.ClickAsync("#btnAdd");
        await page.FillAsync("#FirstName", "Dialog");
        await page.FillAsync("#LastName", "Created");
        await page.FillAsync("#EmailAddress", email);
        await page.SelectOptionAsync("#Gender", "F");
        await page.FillAsync("#PhoneNumber", "6145550099");
        await page.FillAsync("#ZipCode", "43229");
        await page.FillAsync("#Address", "9 Dialog Street, Testville");
        await page.ClickAsync("#btnSave");

        await page.Locator($"text={email}").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await using var db = _app.CreateDbContext();
        var saved = await db.Staffs.IgnoreQueryFilters().SingleAsync(s => s.EmailAddress == email);
        Assert.Equal((_app.Data.Alpha.Id, "Dialog", 'F'), (saved.TenantId, saved.FirstName, saved.Gender));
    }
}
