using System.Net;
using System.Security.Cryptography;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Infrastructure.Persistence;
using InpremClockingApp.Data;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Provider;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using InpremClockingApp.Tests.Support.TestData;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>A clock the tests move forward (link expiry, rate limits, the Level 4 delay).</summary>
public sealed class TestClock : IClock
{
    private TimeSpan _offset = TimeSpan.Zero;

    public DateTime UtcNow => DateTime.UtcNow + _offset;

    public void Advance(TimeSpan by) => _offset += by;
}

/// <summary>Authenticator-app codes (RFC 6238: HMAC-SHA1, 30-second steps, 6 digits), as Identity checks them.</summary>
public static class Totp
{
    public static string Code(string base32Key, DateTime? utc = null)
    {
        var key = FromBase32(base32Key.Replace(" ", string.Empty).ToUpperInvariant());
        var step = (long)((utc ?? DateTime.UtcNow) - DateTime.UnixEpoch).TotalSeconds / 30;
        var counter = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(step));
        var hash = new HMACSHA1(key).ComputeHash(counter);
        var offset = hash[^1] & 0xf;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] FromBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}

/// <summary>Submits Provider forms like a browser: GET the page, keep its hidden fields (antiforgery, token), POST.</summary>
public static class ProviderForms
{
    public static async Task<IHtmlDocument> GetDocumentAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path}: {(int)response.StatusCode} {response.Headers.Location}");
        return new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, params (string Name, string Value)[] fields) =>
        PostFormAsync(client, path, (Func<string, bool>?)null, fields);

    public static Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string actionContains, params (string Name, string Value)[] fields) =>
        PostFormAsync(client, path, a => a.Contains(actionContains, StringComparison.OrdinalIgnoreCase), fields);

    /// <summary>
    /// Submits a POST form of the page: the first one, or the first whose action matches
    /// <paramref name="select"/>. The console layout's sign-out form is never picked.
    /// </summary>
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, Func<string, bool>? select, params (string Name, string Value)[] fields)
    {
        var document = await GetDocumentAsync(client, path);
        var form = document.Forms.First(f => f.Method.Equals("post", StringComparison.OrdinalIgnoreCase)
            && f.GetAttribute("data-role") != "sign-out"
            && (select == null || select(f.GetAttribute("action") ?? "")));
        var values = form.Elements.OfType<IHtmlInputElement>()
            .Where(i => i.Type == "hidden" && !string.IsNullOrEmpty(i.Name))
            .Select(i => new KeyValuePair<string, string>(i.Name!, i.Value)).ToList();
        values.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value)));
        var action = string.IsNullOrEmpty(form.GetAttribute("action")) ? path : form.GetAttribute("action")!;
        return await client.PostAsync(action, new FormUrlEncodedContent(values));
    }

    /// <summary>Presses the console layout's Sign out button.</summary>
    public static async Task<HttpResponseMessage> SignOutAsync(HttpClient client, string page = "/Provider")
    {
        var document = await GetDocumentAsync(client, page);
        var form = document.Forms.Single(f => f.GetAttribute("data-role") == "sign-out");
        var token = form.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
        return await client.PostAsync(form.GetAttribute("action"), new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    }

    public static string Location(HttpResponseMessage response) => response.Headers.Location?.OriginalString ?? "";

    public static async Task<string> BodyAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    /// <summary>The path and query of the last link in the last email to <paramref name="to"/> that has one.</summary>
    public static string LinkSentTo(CapturingEmailSender emails, string to, string marker = "token=")
    {
        var body = emails.To(to).Last(e => e.HtmlBody.Contains(marker, StringComparison.Ordinal)).HtmlBody;
        // The link whose address contains the marker: the href just before it.
        var start = body.LastIndexOf("href=\"", body.IndexOf(marker, StringComparison.Ordinal), StringComparison.Ordinal) + "href=\"".Length;
        var url = WebUtility.HtmlDecode(body[start..body.IndexOf('"', start)]);
        return new Uri(url).PathAndQuery;
    }

    public static string TokenOf(string link) => Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);
}

/// <summary>
/// The app with SiteMode All (console under /Provider) over its own seeded database, with
/// test-only bootstrap and break-glass codes, two approvers and a security mailbox.
/// </summary>
public class ProviderFixture : IAsyncLifetime
{
    public const string BootstrapCode = "TEST1-BOOTS-TRAPC-ODE22-33344-45566";
    public const string BreakGlassCode = "TEST9-GLASS-BREAK-CODE7-77888-89999";
    public const string Approver1 = "approver.one@provider.test";
    public const string Approver2 = "approver.two@provider.test";
    public const string Mailbox = "security@provider.test";
    // Generated per run: no password literal in the repository.
    public static readonly string StrongPassword = "Pp1!" + Guid.NewGuid().ToString("N");

    private TestDatabase _db = null!;
    private InpremAppFactory _app = null!;

    public TestClock Clock { get; } = new();
    public SeedCatalog Data { get; private set; } = null!;
    public CapturingEmailSender Emails => _app.Emails;

    /// <summary>The pretend PayPal (nothing reaches PayPal in tests).</summary>
    public FakePaymentProvider PayPal => _app.PayPal;
    public IServiceProvider Services => _app.Services;
    public string ConnectionString => _db.ConnectionString;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync(GetType().Name);
        await Jobs.HangfireSchema.InstallAsync(_db.ConnectionString); // the console links to the jobs dashboard
        _app = new InpremAppFactory(_db.ConnectionString, new Dictionary<string, string?>
        {
            ["Site:SiteMode"] = "All",
            ["Bootstrap:CodeHash"] = SecretTokens.Hash(BootstrapCode),
            ["Security:BreakGlassCodeHash"] = SecretTokens.Hash(BreakGlassCode),
            ["Security:RecoveryApprovers:0"] = Approver1,
            ["Security:RecoveryApprovers:1"] = Approver2,
            ["Security:Mailbox"] = Mailbox,
            ["Links:AdminBaseUrl"] = "https://localhost/Provider",
        }, configureServices: services => services.AddSingleton<IClock>(Clock));
        _ = _app.Server;
        Data = await TestDataSeeder.SeedAsync(_app.Services);
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _db.DisposeAsync();
    }

    public HttpClient CreateClient() => _app.CreateClient();

    public PlatformDbContext CreatePlatformDbContext() => _db.CreatePlatformDbContext();

    public ApplicationDbContext CreateDbContext() => _db.CreateDbContext();

    /// <summary>Runs a server command the way the operator would; returns the exit code and what it printed.</summary>
    public async Task<(int ExitCode, string Output)> RunCommandAsync(string[] args, string? typedInput = null)
    {
        var output = new StringWriter();
        var code = await ProviderCommands.RunAsync(_app.Services, args, output, new StringReader(typedInput ?? ""));
        return (code, output.ToString());
    }

    public async Task<AppUser> FindUserAsync(string email)
    {
        using var scope = Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email))!;
    }

    /// <summary>
    /// Completes an invitation like the person would: set the password through the link, then enrol
    /// the authenticator. Returns the client (signed in to the console with two-factor) and the key.
    /// </summary>
    public async Task<(HttpClient Client, string Key, IReadOnlyList<string> RecoveryCodes)> CompleteInvitationAsync(string email)
    {
        var client = CreateClient();
        var link = ProviderForms.LinkSentTo(Emails, email);
        var accepted = await ProviderForms.PostAsync(client, link, ("Password", StrongPassword), ("ConfirmPassword", StrongPassword));
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.EndsWith("/Provider/EnrolTwoFactor", ProviderForms.Location(accepted));
        var (key, codes) = await EnrolAsync(client);
        return (client, key, codes);
    }

    public static async Task<(string Key, IReadOnlyList<string> RecoveryCodes)> EnrolAsync(HttpClient client)
    {
        var page = await ProviderForms.GetDocumentAsync(client, "/Provider/EnrolTwoFactor");
        var key = page.QuerySelector("[data-testid=shared-key]")!.TextContent.Trim();
        Assert.NotNull(page.QuerySelector("[data-testid=qr-code] svg")); // scannable, no typing needed
        var enrolled = await ProviderForms.PostAsync(client, "/Provider/EnrolTwoFactor", ("Code", Totp.Code(key)));
        var codes = new HtmlParser().ParseDocument(await enrolled.Content.ReadAsStringAsync())
            .QuerySelectorAll("[data-testid=recovery-codes] code").Select(c => c.TextContent.Trim()).ToList();
        Assert.Equal(10, codes.Count);
        return (key, codes);
    }

    /// <summary>
    /// An active Provider Owner with password and two-factor, created directly (as after bootstrap,
    /// invitation and enrolment). Returns the authenticator key.
    /// </summary>
    public Task<string> CreateEnrolledOwnerAsync(string email) => CreateEnrolledProviderUserAsync(Services, CreatePlatformDbContext, email);

    /// <summary>Same as <see cref="CreateEnrolledOwnerAsync(string)"/> for any app host and provider role.</summary>
    public static async Task<string> CreateEnrolledProviderUserAsync(IServiceProvider services, Func<PlatformDbContext> platformDb, string email,
        Inprem.Platform.Domain.Provider.ProviderRole role = Inprem.Platform.Domain.Provider.ProviderRole.Owner)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, StrongPassword)).Succeeded);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;

        await using var platform = platformDb();
        var provider = Inprem.Platform.Domain.Provider.ProviderUser.Invite(user.Id, role, DateTime.UtcNow);
        provider.Activate();
        platform.ProviderUsers.Add(provider);
        await platform.SaveChangesAsync();
        return key;
    }

    /// <summary>Signs in on the Provider login with password and authenticator code.</summary>
    /// <param name="prefix">"/Provider" with SiteMode All; "" on an Admin-only site.</param>
    public static async Task SignInAsync(HttpClient client, string email, string key, string prefix = "/Provider")
    {
        var login = await ProviderForms.PostAsync(client, $"{prefix}/Login", ("Email", email), ("Password", StrongPassword));
        Assert.EndsWith($"{prefix}/LoginWith2fa", ProviderForms.Location(login));
        var second = await ProviderForms.PostAsync(client, $"{prefix}/LoginWith2fa", ("Code", Totp.Code(key)));
        Assert.Equal(prefix.Length == 0 ? "/" : prefix, ProviderForms.Location(second));
    }
}
