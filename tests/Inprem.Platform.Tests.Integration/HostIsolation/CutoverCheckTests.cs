using InpremClockingApp.Tests.Support.Database;
using Xunit;

namespace Inprem.Platform.Tests.Integration.HostIsolation;

/// <summary>
/// Domain cutover step C.3: the answers that scripts/deploy/check-cutover.ps1 expects from the live
/// sites are the answers the app gives (App and Admin site, hosts set as in production). Keep the two
/// in step: change the script when one of these changes.
/// </summary>
public class CutoverCheckTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync(nameof(CutoverCheckTests));

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Theory]
    [InlineData("/Identity/Account/Login", 200)]
    [InlineData("/StaffAttendance", 302)]
    [InlineData("/Provider", 404)]
    [InlineData("/webhooks/paypal", 405)]
    [InlineData("/webhooks/twilio/status", 405)]
    public async Task The_App_Site_Answers_As_The_Check_Expects(string path, int expected)
    {
        await using var app = SiteHosts.Factory(_db.ConnectionString, "App", adminHost: "admin.inpremclockin.org");
        using var client = app.Client("app.inpremclockin.org");
        Assert.Equal(expected, (int)(await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/Login", 200)]
    [InlineData("/Identity/Account/Login", 404)]
    public async Task The_Admin_Site_Answers_As_The_Check_Expects(string path, int expected)
    {
        await using var app = SiteHosts.Factory(_db.ConnectionString, "Admin", appHost: "app.inpremclockin.org", adminHost: "admin.inpremclockin.org");
        using var client = app.Client("admin.inpremclockin.org");
        Assert.Equal(expected, (int)(await client.GetAsync(path)).StatusCode);
    }
}
