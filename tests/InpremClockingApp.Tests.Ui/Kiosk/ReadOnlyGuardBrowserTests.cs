using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace InpremClockingApp.Tests.Ui.Kiosk;

/// <summary>
/// The read-only guard (developer's request, 2026-10-17): in a suspended organization, Add New,
/// Edit, Create and the manual clock-in say at once that the account is suspended, and no form
/// opens; search and reports keep working. In a real browser, because the guard is JavaScript.
/// </summary>
[Trait("Category", "Ui")]
public class ReadOnlyGuardBrowserTests : IClassFixture<KestrelAppFixture>, IAsyncLifetime
{
    private readonly KestrelAppFixture _app;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public ReadOnlyGuardBrowserTests(KestrelAppFixture app) => _app = app;

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

    private async Task<IPage> SignedInAsync()
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.ServerAddress.ToString() });
        context.SetDefaultTimeout(30_000);
        var page = await context.NewPageAsync();
        await page.GotoAsync(SignIn.LoginPath);
        await page.FillAsync("input[name='Input.Email']", _app.Data.Beta.Admin!.Email);
        await page.FillAsync("input[name='Input.Password']", _app.Data.Password);
        await page.ClickAsync("button[type='submit']");
        // The kiosk page when active; the "kiosk is paused" page when suspended.
        // Wait for the address only: the page's full load also waits for Google Fonts, which made this
        // flaky on a slow connection (found 2026-10-20; the page itself was there and complete).
        await page.WaitForURLAsync(url => !url.Contains("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase), new() { WaitUntil = WaitUntilState.Commit });
        return page;
    }

    private async Task SuspendBetaAsync(bool suspend)
    {
        using var scope = _app.Services.CreateScope();
        var organizations = scope.ServiceProvider.GetRequiredService<Inprem.Platform.Application.Organizations.OrganizationService>();
        var result = suspend
            ? await organizations.SuspendAsync(_app.Data.Beta.Id, "UI test", "provider:test")
            : await organizations.ReactivateAsync(_app.Data.Beta.Id, "UI test", "provider:test");
        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task Ui_A_Suspended_Organization_Is_Told_At_Once_And_No_Form_Opens()
    {
        await SuspendBetaAsync(true);
        try
        {
            var page = await SignedInAsync();
            var notice = page.Locator("[data-testid=readonly-notice]");

            // Add New on Manage Staff: the notice, not the editor.
            await page.GotoAsync("/Staff");
            await page.ClickAsync("#btnAdd");
            await Assertions.Expect(notice).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("[data-testid=readonly-notice-text]")).ToHaveTextAsync("Your organization's account is suspended. You cannot add new staff.");
            await Assertions.Expect(page.Locator("#editorModal")).ToBeHiddenAsync();
            await page.ClickAsync("[data-testid=readonly-notice-ok]");
            await Assertions.Expect(notice).ToBeHiddenAsync();

            // Edit on a row.
            await page.Locator("button:text-is('Edit')").First.ClickAsync();
            await Assertions.Expect(page.Locator("[data-testid=readonly-notice-text]")).ToHaveTextAsync("Your organization's account is suspended. You cannot edit staff.");
            await Assertions.Expect(page.Locator("#editorModal")).ToBeHiddenAsync();
            await page.Keyboard.PressAsync("Escape");

            // Search still works (not a change).
            await page.ClickAsync("#btnSearch");
            await Assertions.Expect(notice).ToBeHiddenAsync();

            // Forms on the page are greyed out with the notice: Create Admin Account.
            await page.GotoAsync("/User");
            await Assertions.Expect(page.Locator("[data-testid=readonly-form-note]")).ToContainTextAsync("You cannot add new admins");
            await Assertions.Expect(page.Locator("form[method=post] input[type=email], form[method=post] input[name*=Email]").First).ToBeDisabledAsync();

            // Manual clock-in on Staff Clockings.
            await page.GotoAsync("/StaffClocking");
            await page.ClickAsync("#btnClockIn");
            await Assertions.Expect(page.Locator("[data-testid=readonly-notice-text]")).ToHaveTextAsync("Your organization's account is suspended. You cannot clock in.");
        }
        finally
        {
            await SuspendBetaAsync(false);
        }

        // Back to normal: Add New opens the editor, no notice on the page.
        var normal = await SignedInAsync();
        await normal.GotoAsync("/Staff");
        Assert.Equal(0, await normal.Locator("[data-testid=readonly-notice]").CountAsync());
        await normal.ClickAsync("#btnAdd");
        await Assertions.Expect(normal.Locator("#editorModal")).ToBeVisibleAsync();
    }
}
