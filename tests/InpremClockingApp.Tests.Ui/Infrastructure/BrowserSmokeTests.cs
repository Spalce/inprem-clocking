using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.Playwright;
using Xunit;

namespace InpremClockingApp.Tests.Ui.Infrastructure;

/// <summary>
/// Proves the browser harness: a real Chromium reaches the app over Kestrel and can sign in
/// through the login form. Run with <c>dotnet test --filter Category=Ui</c>; browsers are
/// installed once with <c>pwsh bin/Debug/net8.0/playwright.ps1 install chromium</c>.
/// </summary>
[Trait("Category", "Ui")]
public class BrowserSmokeTests : IClassFixture<KestrelAppFixture>, IAsyncLifetime
{
    private readonly KestrelAppFixture _app;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public BrowserSmokeTests(KestrelAppFixture app) => _app = app;

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

    [Fact]
    public async Task Browser_Signs_In_Through_Login_Form_And_Lands_On_Kiosk()
    {
        var page = await _browser.NewPageAsync(new BrowserNewPageOptions { BaseURL = _app.ServerAddress.ToString() });

        await page.GotoAsync(SignIn.LoginPath);
        await page.FillAsync("input[name='Input.Email']", _app.Data.Alpha.Admin!.Email);
        await page.FillAsync("input[name='Input.Password']", _app.Data.Password);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync("**/VolunteerAttendance");

        Assert.EndsWith("/VolunteerAttendance", new Uri(page.Url).AbsolutePath);
    }
}
