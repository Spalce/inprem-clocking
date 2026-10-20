using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Notifications;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace InpremClockingApp.Tests.Ui.Kiosk;

/// <summary>
/// Phase 6 step 6.2 (C3-8) in a real browser, because the back-office editor saves by script: with staff
/// alerts on, Manage Staff's editor shows the texts box; ticking it with a number that has no country
/// code is stopped with a message; with the country code, saving records the agreement; the box is
/// cleared when the editor opens for the next person.
/// </summary>
[Trait("Category", "Ui")]
public class StaffConsentBrowserTests : IClassFixture<KestrelAppFixture>, IAsyncLifetime
{
    private readonly KestrelAppFixture _app;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public StaffConsentBrowserTests(KestrelAppFixture app) => _app = app;

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

    private async Task AlertsAsync(bool on)
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StaffAlertSettings>().SaveAsync(_app.Data.Beta.Id, StaffAlertChoices.Defaults with { Enabled = on }, "test");
    }

    [Fact]
    public async Task Ui_The_Editors_Texts_Box_Needs_A_Country_Code_And_Is_Recorded_On_Save()
    {
        await AlertsAsync(true);
        try
        {
            var context = await _browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.ServerAddress.ToString() });
            context.SetDefaultTimeout(30_000);
            var page = await context.NewPageAsync();
            page.Dialog += async (_, dialog) => await dialog.DismissAsync(); // an alert would mean "not recorded": the assertions below catch it
            await page.GotoAsync(SignIn.LoginPath);
            await page.FillAsync("input[name='Input.Email']", _app.Data.Beta.Owner!.Email);
            await page.FillAsync("input[name='Input.Password']", _app.Data.Password);
            await page.ClickAsync("button[type='submit']");
            await page.WaitForURLAsync(url => !url.Contains("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase), new() { WaitUntil = WaitUntilState.Commit });

            await page.GotoAsync("/Staff");
            await page.ClickAsync("#btnAdd");
            await Assertions.Expect(page.Locator("#editorModal [data-testid=sms-consent-box]")).ToBeVisibleAsync();

            var email = $"ui-{Guid.NewGuid():N}"[..12] + "@consent.test";
            await page.FillAsync("#FirstName", "Consenting");
            await page.FillAsync("#LastName", "Person");
            await page.FillAsync("#EmailAddress", email);
            await page.SelectOptionAsync("#Gender", "F");
            await page.FillAsync("#PhoneNumber", "614 516 1812");
            await page.FillAsync("#ZipCode", "43229");
            await page.FillAsync("#Address", "1 Main Street, Columbus"); // the editor's own rule: at least 10 characters
            await page.CheckAsync("#editorModal [data-testid=sms-consent-box]");

            // No country code: stopped, with the message; nothing saved.
            await page.ClickAsync("#btnSave");
            await Assertions.Expect(page.Locator("[data-testid=sms-consent-error]")).ToBeVisibleAsync();
            await using (var app = _app.CreateDbContext())
                Assert.False(await app.Staffs.IgnoreQueryFilters().AnyAsync(s => s.EmailAddress == email));

            // With the country code: saved, and the agreement recorded before the page reloads.
            var phone = "+1202555" + Random.Shared.Next(1000, 9999);
            await page.FillAsync("#PhoneNumber", phone);
            await page.ClickAsync("#btnSave");
            await page.WaitForLoadStateAsync(LoadState.Load);
            await Assertions.Expect(page.Locator("#editorModal")).ToBeHiddenAsync();
            await using (var platform = _app.CreatePlatformDbContext())
            {
                var consent = await platform.ConsentRecords.AsNoTracking().SingleAsync(c => c.Phone == phone);
                Assert.Equal((ConsentSources.BackOffice, _app.Data.Beta.Id), (consent.Source, consent.TenantId));
            }

            // The next person: the box starts unticked.
            await page.ClickAsync("#btnAdd");
            await Assertions.Expect(page.Locator("#editorModal [data-testid=sms-consent-box]")).Not.ToBeCheckedAsync();
        }
        finally
        {
            await AlertsAsync(false);
        }
    }
}
