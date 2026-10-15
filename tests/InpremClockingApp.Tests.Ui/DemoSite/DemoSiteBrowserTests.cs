using System.Net;
using System.Text.RegularExpressions;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace InpremClockingApp.Tests.Ui.DemoSite;

/// <summary>The app with the demo website on, generous limits, and published plans.</summary>
public sealed class DemoSiteFixture : KestrelAppFixture
{
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Demo:EnableDemoSite"] = "true",
        ["Demo:CaptchaTestToken"] = FakeCaptchaVerifier.Pass,
        ["PublicApi:PublicPerMinute"] = "1000",
        ["PublicApi:WritePerMinute"] = "200",
        ["PublicApi:TokenPerMinute"] = "1000",
        // The website's billing page: the test sends the browser to the same path on its own server.
        ["Links:WebsiteBaseUrl"] = WebsiteBase,
    };

    public const string WebsiteBase = "https://demo-website.test/demo-site";

    public PlanPrice Monthly { get; private set; } = null!;
    public PlanPrice Yearly { get; private set; } = null!;

    public async Task PrepareAsync()
    {
        if (Monthly != null) return;
        var origin = ServerAddress.GetLeftPart(UriPartial.Authority);
        PayPal.AllowedOrigins.Add(origin);

        using var scope = Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
        var id = (await plans.CreateAsync("demo-standard", new("Demo Standard", "For the demo", "Kiosk\nReports", 100, 14, true), BillingCycle.Monthly, 25m, "USD", "test")).PlanId!.Value;
        await plans.AddPriceAsync(id, BillingCycle.Yearly, 250m, "USD", "test");
        Assert.True((await scope.ServiceProvider.GetRequiredService<PlanPublisher>().PublishAsync(id, "test")).Succeeded);
        var plan = (await plans.FindAsync(id))!;
        Monthly = plan.Prices.Single(p => p.Cycle == BillingCycle.Monthly);
        Yearly = plan.Prices.Single(p => p.Cycle == BillingCycle.Yearly);
    }
}

/// <summary>
/// Phase 3 step 3.6 (D29; P3-1 with the pretend PayPal): journeys J1 to J5 through the demo website
/// in a real browser. PayPal's approve page is played by the test: it marks the subscription as
/// PayPal would and sends the browser back to the website's return or cancel page.
/// </summary>
[Trait("Category", "Ui")]
public class DemoSiteBrowserTests : IClassFixture<DemoSiteFixture>, IAsyncLifetime
{
    private const string Password = "Demo-Owner-Pass-2026!";
    private readonly DemoSiteFixture _app;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public DemoSiteBrowserTests(DemoSiteFixture app) => _app = app;

    public async Task InitializeAsync()
    {
        await _app.PrepareAsync();
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
    }

    private string Origin => _app.ServerAddress.GetLeftPart(UriPartial.Authority);

    /// <summary>A page whose trips to PayPal are answered by <paramref name="paypal"/>: approve (Active), or cancel.</summary>
    private async Task<IPage> PageAsync(bool approve = true)
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = Origin });
        var page = await context.NewPageAsync();
        await page.RouteAsync("https://www.sandbox.paypal.com/**", async route =>
        {
            var url = route.Request.Url;
            var id = Regex.Match(url, @"(?:BA-|id=)(I-\d+)").Groups[1].Value;
            if (approve && id.Length > 0) _app.PayPal.SetState(id, PayPalSubscriptionState.Active, DateTime.UtcNow.AddMonths(1));
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 302,
                Headers = new Dictionary<string, string> { ["Location"] = $"{Origin}/demo-site/{(approve ? "return" : "cancel")}.html?subscription_id={id}" },
            });
        });
        return page;
    }

    private async Task FillSignupAsync(IPage page, string email, bool subscribe)
    {
        await page.GotoAsync("/demo-site/");
        await page.Locator("[data-testid=choose]").First.ClickAsync();
        await page.FillAsync("#org", "Demo Centre");
        await page.FillAsync("#slug", $"demo-{Guid.NewGuid():N}"[..14]);
        await page.FillAsync("#first", "Abena");
        await page.FillAsync("#last", "Boateng");
        await page.FillAsync("#email", email);
        await page.CheckAsync("#terms");
        await page.CheckAsync("#privacy");
        if (subscribe) await page.CheckAsync("#mode-subscribe");
        await page.ClickAsync("[data-testid=register]");
    }

    private string LinkSentTo(string email, string marker)
    {
        var body = _app.Emails.To(email).Last(e => e.HtmlBody.Contains(marker, StringComparison.Ordinal)).HtmlBody;
        var start = body.LastIndexOf("href=\"", body.IndexOf(marker, StringComparison.Ordinal), StringComparison.Ordinal) + 6;
        return WebUtility.HtmlDecode(body[start..body.IndexOf('"', start)]);
    }

    private static string Email(string journey) => $"{journey}-{Guid.NewGuid():N}"[..14] + "@demo.test";

    [Fact]
    public async Task J1_Trial_Signup_Then_Verify_Email_And_Choose_A_Password()
    {
        var email = Email("j1");
        var page = await PageAsync();
        await FillSignupAsync(page, email, subscribe: false);
        await Assertions.Expect(page.Locator("[data-testid=check-email]")).ToBeVisibleAsync();

        await page.GotoAsync(LinkSentTo(email, "/Account/VerifyEmail"));
        await page.FillAsync("#Password", Password);
        await page.FillAsync("#ConfirmPassword", Password);
        await page.ClickAsync("[data-testid=confirm]");
        await Assertions.Expect(page.Locator("[data-testid=done]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task J2_Subscribe_Now_Pays_At_PayPal_And_Is_Active_After_Confirmation()
    {
        var page = await PageAsync(approve: true);
        await FillSignupAsync(page, Email("j2"), subscribe: true);
        await Assertions.Expect(page.Locator("[data-testid=result]")).ToHaveAttributeAsync("data-status", "Active", new() { Timeout = 30000 });
        await Assertions.Expect(page.Locator("#title")).ToHaveTextAsync("You are subscribed");
    }

    [Fact]
    public async Task J5_Cancel_At_PayPal_Shows_Checkout_Not_Completed()
    {
        var page = await PageAsync(approve: false);
        await FillSignupAsync(page, Email("j5"), subscribe: true);
        await Assertions.Expect(page.Locator("[data-testid=result]")).ToHaveAttributeAsync("data-status", "Cancelled", new() { Timeout = 30000 });
    }

    [Fact]
    public async Task J3_J4_An_Owner_Gets_A_Link_Converts_The_Trial_Then_Cancels()
    {
        // A trial organization registered on the website; its owner manages it later.
        var email = Email("j3");
        var signup = await PageAsync();
        await FillSignupAsync(signup, email, subscribe: false);
        await Assertions.Expect(signup.Locator("[data-testid=check-email]")).ToBeVisibleAsync();

        // J3: ask for a link (the same answer for anyone), open it from the email.
        var page = await PageAsync(approve: true);
        await page.GotoAsync("/demo-site/manage.html");
        await page.FillAsync("#email", email);
        await page.ClickAsync("[data-testid=send-link]");
        await Assertions.Expect(page.Locator("[data-testid=answer]")).ToContainTextAsync("If this email belongs to an organization owner");
        await _app.RunBillingAccessJobsAsync();
        await page.GotoAsync(LinkSentTo(email, "/billing?code=").Replace(DemoSiteFixture.WebsiteBase, Origin + "/demo-site"));
        await Assertions.Expect(page.Locator("[data-testid=status]")).ToHaveAttributeAsync("data-status", "Trialing");
        Assert.DoesNotContain("code=", page.Url); // the used code is off the address bar

        // J4: pay (convert the trial) through PayPal.
        await page.Locator("[data-testid=pay]").First.ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=result]")).ToHaveAttributeAsync("data-status", "Active", new() { Timeout = 30000 });

        // Back to the subscription with the same tab's token, and cancel.
        await page.GotoAsync("/demo-site/billing.html");
        await Assertions.Expect(page.Locator("[data-testid=status]")).ToHaveAttributeAsync("data-status", "Active");
        await page.ClickAsync("[data-testid=cancel-subscription]");
        await Assertions.Expect(page.Locator("[data-testid=status]")).ToHaveAttributeAsync("data-status", "Canceled");
    }

    [Fact]
    public async Task The_Demo_Site_Is_Off_Unless_Switched_On()
    {
        using var client = new HttpClient { BaseAddress = _app.ServerAddress };
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/demo-site/index.html")).StatusCode);

        // The ordinary test host (no Demo setting): not found.
        var plain = new KestrelAppFixture();
        await plain.InitializeAsync();
        try
        {
            using var other = new HttpClient { BaseAddress = plain.ServerAddress };
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/demo-site/index.html")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/demo-site/config.json")).StatusCode);
        }
        finally
        {
            await plain.DisposeAsync();
        }
    }
}
