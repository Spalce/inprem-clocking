using System.Net;
using InpremClockingApp.Hosting;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using Xunit;

namespace Inprem.Platform.Tests.Integration.HostIsolation;

/// <summary>
/// Domain cutover step C.2 (C3-11): with LegacyRedirect on, every App-site route asked for on the old
/// host answers 301 to the same path and query on app.; other paths, other hosts and the setting
/// off change nothing. The list of rules is the same as in the web team's runbook.
/// </summary>
public class LegacyRedirectTests : IAsyncLifetime
{
    private const string Old = "inpremclockin.org";
    private const string Target = "https://app.inpremclockin.org";
    private TestDatabase _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync(nameof(LegacyRedirectTests));

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private InpremAppFactory On() => SiteHosts.Factory(_db.ConnectionString, "App", extra: new Dictionary<string, string?>
    {
        ["LegacyRedirect:Enabled"] = "true",
        ["LegacyRedirect:FromHosts:0"] = Old,
        ["LegacyRedirect:FromHosts:1"] = "www." + Old,
        ["LegacyRedirect:TargetBaseUrl"] = Target,
    });

    [Fact]
    public async Task Every_App_Route_On_The_Old_Host_Is_Moved_Permanently_With_Its_Query()
    {
        await using var app = On();
        var urls = app.Endpoints().Where(e => e.Area != SiteMap.ProviderArea && e.Pattern != "app-probe") // app-probe: the tests' own stand-in (SiteHosts)
            .Select(e => SiteHosts.SampleUrl(e.Pattern)).OfType<string>().Where(u => u != "/").Distinct().ToList();
        Assert.True(urls.Count > 40, $"Only {urls.Count} App routes found; the enumeration looks broken.");

        using var client = app.Client(Old);
        var notMoved = new List<string>();
        foreach (var url in urls)
        {
            using var response = await client.GetAsync(url + "?from=old&n=1");
            if (response.StatusCode != HttpStatusCode.MovedPermanently || response.Headers.Location?.ToString() != Target + url + "?from=old&n=1")
                notMoved.Add($"{url} -> {(int)response.StatusCode} {response.Headers.Location}");
        }
        Assert.True(notMoved.Count == 0, "Not redirected (add a rule to LegacyPaths and to root-redirects.md):\n" + string.Join("\n", notMoved));
    }

    [Theory]
    [InlineData("/StaffAttendance")]
    [InlineData("/staff-clockin/12")]
    [InlineData("/volunteer-clockin/7")]
    [InlineData("/backoffice")]
    [InlineData("/Identity/Account/Login?ReturnUrl=%2FBackOffice")]
    [InlineData("/api/controls/staff-clockin/clockout/3")]
    public async Task Old_Links_Keep_Their_Path_And_Query(string url)
    {
        await using var app = On();
        foreach (var host in new[] { Old, "www." + Old })
        {
            using var client = app.Client(host);
            using var get = await client.GetAsync(url);
            Assert.Equal((HttpStatusCode.MovedPermanently, Target + url), (get.StatusCode, get.Headers.Location?.ToString()));
        }
        using var post = await app.Client(Old).PostAsync(url, new StringContent(""));
        Assert.Equal(HttpStatusCode.MovedPermanently, post.StatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/pricing")]
    [InlineData("/about-us")]
    [InlineData("/lib/jquery/dist/jquery.min.js")]
    public async Task Other_Paths_Are_Left_For_The_Website(string url)
    {
        await using var app = On();
        using var response = await app.Client(Old).GetAsync(url);
        Assert.NotEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.False(response.Headers.Location?.ToString().StartsWith(Target, StringComparison.Ordinal) ?? false);
    }

    [Fact]
    public async Task The_New_Host_And_The_Setting_Off_Are_Unchanged()
    {
        await using (var app = On())
        {
            using var onApp = await app.Client("app.inpremclockin.org").GetAsync("/Identity/Account/Login");
            Assert.Equal(HttpStatusCode.OK, onApp.StatusCode); // no loop on the target host
        }

        await using var off = SiteHosts.Factory(_db.ConnectionString, "App");
        using var onOld = await off.Client(Old).GetAsync("/Identity/Account/Login");
        Assert.Equal(HttpStatusCode.OK, onOld.StatusCode);
    }

    [Theory]
    [InlineData("", "https://app.inpremclockin.org", "FromHosts")]
    [InlineData("inpremclockin.org", "http://app.inpremclockin.org", "TargetBaseUrl")]
    [InlineData("inpremclockin.org", "https://app.inpremclockin.org/x", "TargetBaseUrl")]
    [InlineData("app.inpremclockin.org", "https://app.inpremclockin.org", "redirect to itself")]
    public void Wrong_Settings_Are_Refused_At_Startup(string from, string target, string message)
    {
        var result = new LegacyRedirectOptionsValidator().Validate(null, new LegacyRedirectOptions
        {
            Enabled = true,
            FromHosts = from.Length == 0 ? Array.Empty<string>() : new[] { from },
            TargetBaseUrl = target,
        });
        Assert.True(result.Failed);
        Assert.Contains(message, result.FailureMessage);
        Assert.True(new LegacyRedirectOptionsValidator().Validate(null, new LegacyRedirectOptions()).Succeeded); // off: nothing to check
    }

    [Fact]
    public void The_Runbook_Lists_Exactly_The_Rules_In_The_Code()
    {
        var runbook = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "multitenancy", "runbooks", "root-redirects.md"));
        var start = runbook.IndexOf("<!-- rules:start -->", StringComparison.Ordinal);
        var end = runbook.IndexOf("<!-- rules:end -->", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "root-redirects.md needs the <!-- rules:start --> ... <!-- rules:end --> block.");
        var listed = System.Text.RegularExpressions.Regex.Matches(runbook[start..end], @"^\| `/([^`]+)` \|", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(LegacyPaths.Rules.OrderBy(r => r, StringComparer.Ordinal), listed.OrderBy(r => r, StringComparer.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InpremClockingApp.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
