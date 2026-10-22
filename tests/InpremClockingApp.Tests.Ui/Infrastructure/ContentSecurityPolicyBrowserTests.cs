using System.Net;
using System.Security.Cryptography;
using Inprem.Platform.Domain.Provider;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace InpremClockingApp.Tests.Ui.Infrastructure;

/// <summary>The app and the console on one site, with the demo website and the API docs page on, the policy enforced.</summary>
public sealed class CspFixture : KestrelAppFixture
{
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Site:SiteMode"] = "All",
        ["SecurityHeaders:ContentSecurityPolicy"] = "Enforce",
        ["Demo:EnableDemoSite"] = "true",
        ["PublicApi:EnableDocs"] = "true",
    };
}

/// <summary>
/// Step 7.6: every page of the app, the console, the demo website and the API docs page opens with
/// the Content-Security-Policy enforced and the browser blocks nothing. A page that needs something
/// the policy does not allow fails here, with the page and what was blocked.
/// </summary>
[Trait("Category", "Ui")]
public class ContentSecurityPolicyBrowserTests : IClassFixture<CspFixture>, IAsyncLifetime
{
    private const string ProviderPassword = "Csp-Provider-Pass-2026!";
    private const string Recorder = """
        window.__csp = [];
        document.addEventListener('securitypolicyviolation', e => window.__csp.push(e.effectiveDirective + ' blocked ' + (e.blockedURI || 'inline')));
        """;

    private readonly CspFixture _app;
    private readonly ITestOutputHelper _output;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public ContentSecurityPolicyBrowserTests(CspFixture app, ITestOutputHelper output)
    {
        _app = app;
        _output = output;
    }

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

    private async Task<IPage> NewPageAsync()
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.ServerAddress.ToString() });
        await context.AddInitScriptAsync(Recorder);
        return await context.NewPageAsync();
    }

    /// <summary>Opens each address and lists what the browser blocked, by page (also what it says in the console).</summary>
    /// <param name="signedIn">The pages must not send the browser to a sign-in page (or the check would prove nothing).</param>
    private async Task<List<string>> BlockedAsync(IPage page, IEnumerable<string> urls, bool signedIn = false)
    {
        var console = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase)) console.Add(message.Text);
        };

        var blocked = new List<string>();
        foreach (var url in urls)
        {
            console.Clear();
            try
            {
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
                await page.WaitForTimeoutAsync(300); // late scripts and styles
            }
            catch (PlaywrightException e) when (e.Message.Contains("Download is starting", StringComparison.Ordinal))
            {
                continue; // a file download, not a page
            }

            var landed = new Uri(page.Url).PathAndQuery;
            _output.WriteLine($"{url} -> {landed}");
            if (signedIn && (landed.Contains("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase) || landed.StartsWith("/Provider/Login", StringComparison.OrdinalIgnoreCase)))
                blocked.Add($"{url}: sent to sign-in ({landed}), so not checked");

            var found = await page.EvaluateAsync<string[]>("() => window.__csp || []");
            blocked.AddRange(found.Concat(console).Distinct().Select(b => $"{url}: {b}"));
        }
        return blocked;
    }

    /// <summary>The GET pages of an area (or the app, area null) whose addresses need no value, except signing out.</summary>
    private List<string> PagesWithoutParameters(string? area) =>
        _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.RazorPages.PageActionDescriptor>() != null)
            .Where(e => (e.RoutePattern.RequiredValues.TryGetValue("area", out var a) ? a as string : null) is var found
                        && (area == null ? string.IsNullOrEmpty(found) : found == area))
            .Select(e => "/" + (e.RoutePattern.RawText ?? "").TrimStart('/'))
            .Where(u => !u.Contains('{') && !u.Contains("Logout", StringComparison.OrdinalIgnoreCase) && !u.Contains("SignOut", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderBy(u => u)
            .ToList();

    [Fact]
    public async Task Signed_Out_Pages_Demo_Website_And_Api_Docs_Block_Nothing()
    {
        var page = await NewPageAsync();
        var urls = new List<string>
        {
            "/Identity/Account/Login", "/Identity/Account/ForgotPassword", "/Provider/Login",
            "/Provider/AcceptInvitation", "/Provider/ApproveRecovery", "/Provider/AccessDenied", "/Provider/LoginWithRecoveryCode",
            "/demo-site/", "/demo-site/signup.html", "/demo-site/manage.html",
            "/api/public/docs/index.html",
        };
        var blocked = await BlockedAsync(page, urls);
        Assert.True(blocked.Count == 0, "Blocked by the Content-Security-Policy:\n" + string.Join("\n", blocked));
    }

    [Fact]
    public async Task Every_Page_Of_The_Organization_Owner_Blocks_Nothing()
    {
        var page = await NewPageAsync();
        await page.GotoAsync(SignIn.LoginPath);
        await page.FillAsync("input[name='Input.Email']", _app.Data.Alpha.Owner!.Email);
        await page.FillAsync("input[name='Input.Password']", _app.Data.Password);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync("**/VolunteerAttendance");

        var alpha = _app.Data.Alpha;
        var urls = PagesWithoutParameters(null).Concat(PagesWithoutParameters("Billing")).Concat(PagesWithoutParameters("Settings")).Concat(PagesWithoutParameters("Account"))
            .Concat(new[]
            {
                $"/staff-clockin/{alpha.Staff[0].Id}", $"/volunteer-clockin/{alpha.Volunteers[0].Id}",
                $"/HoursWorked?type=staff&id={alpha.Staff[0].Id}", $"/HoursWorked?type=volunteer&id={alpha.Volunteers[0].Id}",
            })
            .ToList();
        Assert.True(urls.Count > 25, $"Only {urls.Count} pages found; the page list looks broken.");

        var blocked = await BlockedAsync(page, urls, signedIn: true);
        Assert.True(blocked.Count == 0, "Blocked by the Content-Security-Policy:\n" + string.Join("\n", blocked));
    }

    [Fact]
    public async Task Every_Console_Page_Blocks_Nothing()
    {
        var email = $"csp-{Guid.NewGuid():N}"[..12] + "@provider.test";
        var key = await CreateProviderOwnerAsync(email);

        var page = await NewPageAsync();
        await page.GotoAsync("/Provider/Login");
        await page.FillAsync("input[name='Email']", email);
        await page.FillAsync("input[name='Password']", ProviderPassword);
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync("**/Provider/LoginWith2fa**");
        await page.FillAsync("input[name='Code']", Totp(key));
        await page.ClickAsync("button[type='submit']");
        await page.WaitForURLAsync(u => !u.Contains("/Login", StringComparison.OrdinalIgnoreCase));

        // Signed-out pages (checked above), and pages that end or restart the console session.
        var notSignedIn = new[] { "Login", "Setup", "AcceptInvitation", "ApproveRecovery", "AccessDenied", "EnrolTwoFactor", "Reauthenticate" };
        var urls = PagesWithoutParameters("Provider").Where(u => !notSignedIn.Any(n => u.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .Append($"/Provider/Tenants/Details/{_app.Data.Alpha.Id}")
            .ToList();
        Assert.True(urls.Count > 10, $"Only {urls.Count} console pages found; the page list looks broken.");

        var blocked = await BlockedAsync(page, urls, signedIn: true);
        Assert.True(blocked.Count == 0, "Blocked by the Content-Security-Policy:\n" + string.Join("\n", blocked));
    }

    [Fact]
    public async Task Sign_In_Checks_Empty_Fields_In_The_Browser_With_jQuery_Validation_1_22_1()
    {
        // Step 7.6 (ZAP finding): the Identity pages moved from jQuery Validation 1.17.0 to 1.22.1 (cdnjs, or the
        // local copy when cdnjs can't be reached). The approved sign-in form still checks its fields before sending.
        var page = await NewPageAsync();
        var scripts = new List<string>();
        var posts = 0;
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("jquery.validate", StringComparison.Ordinal)) scripts.Add(request.Url);
            if (request.Method == "POST" && request.Url.Contains("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase)) posts++;
        };
        await page.GotoAsync("/Identity/Account/Login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await page.ClickAsync("button[type='submit']");
        await page.Locator("input[name='Input.Email'].input-validation-error").WaitForAsync(new() { Timeout = 10_000 });

        Assert.Equal(0, posts); // checked in the browser, nothing sent
        Assert.Contains(scripts, s => s.Contains("1.22.1/jquery.validate.min.js", StringComparison.Ordinal));
        Assert.DoesNotContain(scripts, s => s.Contains("1.17.0", StringComparison.Ordinal) || s.Contains("/Identity/lib/jquery-validation/", StringComparison.Ordinal));
        Assert.Empty(await page.EvaluateAsync<string[]>("() => window.__csp || []"));
    }

    [Fact]
    public async Task The_Policy_Really_Blocks_What_It_Does_Not_Allow()
    {
        // The check above is only worth something if the recorder sees a real block.
        var page = await NewPageAsync();
        await page.GotoAsync("/Identity/Account/Login");
        await page.EvaluateAsync("() => { const s = document.createElement('script'); s.src = 'https://evil.example/x.js'; document.head.appendChild(s); }");
        await page.WaitForTimeoutAsync(500);
        var found = await page.EvaluateAsync<string[]>("() => window.__csp || []");
        Assert.Contains(found, b => b.StartsWith("script-src", StringComparison.Ordinal) && b.Contains("evil.example", StringComparison.Ordinal));
    }

    private async Task<string> CreateProviderOwnerAsync(string email)
    {
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, ProviderPassword)).Succeeded);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;

        await using var platform = _app.CreatePlatformDbContext();
        var provider = ProviderUser.Invite(user.Id, ProviderRole.Owner, DateTime.UtcNow);
        provider.Activate();
        platform.ProviderUsers.Add(provider);
        await platform.SaveChangesAsync();
        return key;
    }

    /// <summary>Authenticator-app code (RFC 6238), as Identity checks it.</summary>
    private static string Totp(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in base32Key.Replace(" ", "").ToUpperInvariant().TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        var step = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds / 30;
        var hash = new HMACSHA1(bytes.ToArray()).ComputeHash(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(step)));
        var offset = hash[^1] & 0xf;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }
}
