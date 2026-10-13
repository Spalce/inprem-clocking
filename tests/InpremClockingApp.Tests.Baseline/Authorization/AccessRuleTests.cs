using System.Net;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.Authorization;

/// <summary>
/// Access rules as they are today:
/// <list type="bullet">
/// <item>anonymous: only the home page and the Identity pages; everything else redirects to login (APIs too);</item>
/// <item>signed in without the Admin role: kiosk pages and the kiosk/search APIs only;</item>
/// <item>Admin: everything except the SuperAdmin-only /Platform pages and invoice PDFs.</item>
/// </list>
/// Own fixture: the no-role account it creates would change the admin counts other tests read.
/// </summary>
public class AccessRuleTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public AccessRuleTests(AppFixture app) => _app = app;

    private const string Login = "https://localhost/Identity/Account/Login";
    private const string AccessDenied = "https://localhost/Identity/Account/AccessDenied";

    public static TheoryData<string> AdminOnlyGets => new()
    {
        "/BackOffice", "/Staff", "/StaffDetails?id=1", "/Volunteer", "/VolunteerDetails?id=1", "/User",
        "/StaffClocking", "/VolunteerClocking", "/StaffReport", "/StaffClockingReport", "/VolunteerReport",
        "/VolunteerClockingReport", "/HoursWorked?type=staff&id=1", "/Identity/Account/Register",
        "/Staff?handler=ExportCsv", "/BackOffice?handler=DownloadReport",
        "/api/people/emails?q=a", "/api/people/staff?q=a", "/api/people/volunteers?q=a",
        "/api/people/staff-hours?id=1", "/api/people/volunteer-hours?id=1",
        "/api/people/staff-hours-pdf?id=1", "/api/people/volunteer-hours-pdf?id=1",
        "/api/people/staff-clocking-report-pdf", "/api/people/volunteer-clocking-report-pdf",
        "/api/people/staff-list-pdf", "/api/people/volunteer-list-pdf",
        "/api/staff/all-staff", "/api/staff/all-Volunteer",
    };

    public static TheoryData<string> AdminOnlyPosts => new()
    {
        "/api/reports/staff", "/api/reports/volunteer", "/api/reports/staff-clocking", "/api/reports/staff-clocking-one",
        "/api/reports/volunteer-clocking", "/api/reports/volunteer-clocking-one",
        "/api/reports/export/staff-clocking", "/api/reports/export/volunteer-clocking",
        "/api/staff/save", "/api/staff/move",
    };

    public static TheoryData<string> SignedInGets => new()
    {
        "/StaffAttendance", "/VolunteerAttendance", "/staff-clockin/1", "/volunteer-clockin/1", "/Privacy",
        "/api/controls/staff-clockout/1", "/api/controls/volunteer-clockout/1",
        "/api/controls/staff-leavein/1", "/api/controls/volunteer-leaveout/1",
        "/api/search/staff?term=a", "/api/search/volunteer?term=a",
    };

    public static TheoryData<string> SuperAdminOnlyGets => new()
    {
        "/Platform/Tenants", "/Platform/Tenants/1", "/Platform/Invoices", "/api/invoices/1/pdf",
    };

    [Theory]
    [MemberData(nameof(AdminOnlyGets))]
    [MemberData(nameof(SignedInGets))]
    [MemberData(nameof(SuperAdminOnlyGets))]
    public async Task Anonymous_Get_Redirects_To_Login(string path)
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(path);

        AssertRedirect(response, Login);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyPosts))]
    public async Task Anonymous_Post_Redirects_To_Login(string path)
    {
        using var client = _app.CreateClient();

        using var response = await client.PostJsonAsync(path, new { });

        AssertRedirect(response, Login);
    }

    [Theory]
    [InlineData("/")]
    [InlineData(SignIn.LoginPath)]
    [InlineData("/Identity/Account/ForgotPassword")]
    public async Task Anonymous_Can_Open_Public_Pages(string path)
    {
        using var client = _app.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyGets))]
    [MemberData(nameof(SuperAdminOnlyGets))]
    public async Task Signed_In_Without_Admin_Role_Is_Denied_Admin_Gets(string path)
    {
        using var client = await _app.NoRoleUserOfAsync(_app.Data.Alpha);

        using var response = await client.GetAsync(path);

        AssertRedirect(response, AccessDenied);
    }

    [Theory]
    [MemberData(nameof(AdminOnlyPosts))]
    public async Task Signed_In_Without_Admin_Role_Is_Denied_Admin_Posts(string path)
    {
        using var client = await _app.NoRoleUserOfAsync(_app.Data.Alpha);

        using var response = await client.PostJsonAsync(path, new { });

        AssertRedirect(response, AccessDenied);
    }

    [Theory]
    [MemberData(nameof(SignedInGets))]
    public async Task Signed_In_Without_Admin_Role_Can_Use_The_Kiosk(string path)
    {
        using var client = await _app.NoRoleUserOfAsync(_app.Data.Alpha);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(SuperAdminOnlyGets))]
    public async Task Admin_Is_Denied_SuperAdmin_Pages(string path)
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.GetAsync(path);

        AssertRedirect(response, AccessDenied);
    }

    [Fact]
    public async Task Admin_Can_Open_The_Register_Page()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.GetAsync("/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_Without_Return_Url_Goes_To_Volunteer_Kiosk_And_With_One_Goes_Back()
    {
        using var plain = _app.CreateClient();
        using var withReturn = _app.CreateClient();

        Assert.Equal("/VolunteerAttendance", await SignIn.AsAsync(plain, _app.Data.Alpha.Owner!.Email, _app.Data.Password));
        Assert.Equal("/StaffReport", await SignIn.AsAsync(withReturn, _app.Data.Alpha.Owner!.Email, _app.Data.Password, "/StaffReport"));
    }

    // Changed in Phase 0 step 0.4 (C3-7). Until then this test recorded that failed sign-ins never
    // locked an account. Now 5 failures lock it for 15 minutes.
    [Fact]
    public async Task Login_Five_Failures_Lock_The_Account()
    {
        using var client = _app.CreateClient();
        for (var i = 0; i < 4; i++)
        {
            using var failed = await SignIn.PostLoginAsync(client, _app.Data.Beta.Owner!.Email, "Wrong-Passw0rd!");
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }

        // The 5th failure locks the account and goes straight to the lockout page...
        using var fifth = await SignIn.PostLoginAsync(client, _app.Data.Beta.Owner!.Email, "Wrong-Passw0rd!");
        Assert.Equal((HttpStatusCode.Redirect, "/Identity/Account/Lockout"), (fifth.StatusCode, fifth.Headers.Location!.OriginalString));

        // ...and even the right password is refused while it lasts.
        using var locked = await SignIn.PostLoginAsync(client, _app.Data.Beta.Owner!.Email, _app.Data.Password);
        Assert.Equal((HttpStatusCode.Redirect, "/Identity/Account/Lockout"), (locked.StatusCode, locked.Headers.Location!.OriginalString));
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<InpremClockingApp.Models.Identity.AppUser>>();
            var user = await users.FindByEmailAsync(_app.Data.Beta.Owner.Email);
            Assert.InRange(user!.LockoutEnd!.Value, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
        }
    }

    [Fact]
    public async Task Login_Fewer_Than_Five_Failures_Then_Success_Resets_The_Count()
    {
        using var client = _app.CreateClient();
        for (var i = 0; i < 4; i++)
        {
            using var failed = await SignIn.PostLoginAsync(client, _app.Data.Alpha.Owner!.Email, "Wrong-Passw0rd!");
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }

        Assert.Equal("/VolunteerAttendance", await SignIn.AsAsync(client, _app.Data.Alpha.Owner!.Email, _app.Data.Password));
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<InpremClockingApp.Models.Identity.AppUser>>();
        Assert.Equal(0, (await users.FindByEmailAsync(_app.Data.Alpha.Owner.Email))!.AccessFailedCount);
    }

    [Fact]
    public async Task Logout_Ends_The_Session()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var logout = await client.PostAsync("/Identity/Account/Logout", new FormUrlEncodedContent(Array.Empty<KeyValuePair<string, string>>()));
        using var after = await client.GetAsync("/BackOffice");

        AssertRedirect(after, Login);
    }

    private static void AssertRedirect(HttpResponseMessage response, string target)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(target, response.Headers.Location!.ToString());
    }
}
