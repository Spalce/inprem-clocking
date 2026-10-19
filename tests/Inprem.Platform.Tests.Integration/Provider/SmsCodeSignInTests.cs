using System.Net;
using Inprem.Platform.Application.Notifications;
using Inprem.Platform.Domain.Provider;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>
/// Phase 5 step 5.2 (D14): a console user confirms a phone with a texted code, then can sign in with
/// a texted code instead of the authenticator app. A code works once (P5-1: reuse rejected); the
/// option is offered only with a confirmed phone; changing the phone needs a recent two-factor code.
/// </summary>
public class SmsCodeSignInTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public SmsCodeSignInTests(ProviderFixture app) => _app = app;

    private CapturingOtpService Codes => (CapturingOtpService)_app.Services.GetRequiredService<IOtpService>();

    private static string NewPhone() => "+1202555" + Random.Shared.Next(1000, 9999);

    private async Task<(HttpClient Client, string Email, string Key)> SignedInAsync(ProviderRole role = ProviderRole.Support)
    {
        var email = $"sms-{Guid.NewGuid():N}"[..14] + "@console.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, role);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return (client, email, key);
    }

    private async Task ConfirmPhoneAsync(HttpClient client, string phone)
    {
        var send = await PostFormAsync(client, "/Provider/MyPhone", "handler=Send", ("Phone", phone));
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);
        Assert.Contains("Confirm this phone", await BodyAsync(send));
        var confirm = await client.PostAsync("/Provider/MyPhone?handler=Confirm", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Phone"] = phone, ["Code"] = Codes.LastCode(phone)!, ["__RequestVerificationToken"] = TokenFrom(await BodyAsync(send)),
        }));
        Assert.Equal(HttpStatusCode.Redirect, confirm.StatusCode);
    }

    private static string TokenFrom(string html) =>
        System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

    /// <summary>Posts a code on the page that said it was sent (that page holds the code form).</summary>
    private static async Task<HttpResponseMessage> PostCodeAsync(HttpClient client, HttpResponseMessage sentPage, string code) =>
        await client.PostAsync("/Provider/LoginWithSmsCode", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Code"] = code, ["__RequestVerificationToken"] = TokenFrom(await BodyAsync(sentPage)),
        }));

    /// <summary>Password step only: the browser is half signed in, on the two-factor step.</summary>
    private async Task<HttpClient> PasswordOnlyAsync(string email)
    {
        var client = _app.CreateClient();
        var login = await PostAsync(client, "/Provider/Login", ("Email", email), ("Password", StrongPassword));
        Assert.EndsWith("/Provider/LoginWith2fa", Location(login));
        return client;
    }

    [Fact]
    public async Task A_Confirmed_Phone_Allows_Signing_In_With_A_Texted_Code_Once()
    {
        var (client, email, _) = await SignedInAsync();
        var phone = NewPhone();
        await ConfirmPhoneAsync(client, phone);
        var page = await GetDocumentAsync(client, "/Provider/MyPhone");
        Assert.Contains("•••", page.QuerySelector("[data-testid=confirmed]")!.TextContent);
        Assert.Contains(phone[^4..], page.QuerySelector("[data-testid=confirmed]")!.TextContent);

        using var next = await PasswordOnlyAsync(email);
        Assert.NotNull((await GetDocumentAsync(next, "/Provider/LoginWith2fa")).QuerySelector("[data-testid=use-text]"));
        var sent = await PostFormAsync(next, "/Provider/LoginWithSmsCode", "handler=Send");
        Assert.Contains("We sent a code to", await BodyAsync(sent));
        var code = Codes.LastCode(phone)!;
        var signedIn = await PostCodeAsync(next, sent, code);
        Assert.Equal("/Provider", Location(signedIn));
        Assert.Equal(HttpStatusCode.OK, (await next.GetAsync("/Provider")).StatusCode);

        // P5-1: the same code a second time is refused.
        using var again = await PasswordOnlyAsync(email);
        var sentAgain = await PostFormAsync(again, "/Provider/LoginWithSmsCode", "handler=Send");
        var newCode = Codes.LastCode(phone)!;
        var reused = await PostCodeAsync(again, sentAgain, code == newCode ? "000000" : code);
        Assert.Equal(HttpStatusCode.OK, reused.StatusCode);
        Assert.Contains("not valid", await BodyAsync(reused));
        Assert.EndsWith("/Provider/Login", Location(await again.GetAsync("/Provider"))); // still not signed in

        await using var db = _app.CreatePlatformDbContext();
        var last4 = phone[^4..];
        var actions = await db.AuditEntries.AsNoTracking().Where(a => a.Target != null && a.Target.EndsWith(last4) && a.Action.StartsWith("security.")).Select(a => a.Action).ToListAsync();
        Assert.Contains("security.phone-confirmed", actions);
        Assert.Contains("security.sms-code-approved", actions);
        Assert.Contains("security.sms-code-rejected", actions);
    }

    [Fact]
    public async Task Without_A_Confirmed_Phone_No_Text_Is_Offered()
    {
        var (_, email, _) = await SignedInAsync();
        using var next = await PasswordOnlyAsync(email);
        Assert.Null((await GetDocumentAsync(next, "/Provider/LoginWith2fa")).QuerySelector("[data-testid=use-text]"));
        Assert.EndsWith("/Provider/LoginWith2fa", Location(await next.GetAsync("/Provider/LoginWithSmsCode")));

        using var anonymous = _app.CreateClient();
        Assert.EndsWith("/Provider/Login", Location(await anonymous.GetAsync("/Provider/LoginWithSmsCode"))); // the password comes first
    }

    [Fact]
    public async Task A_Wrong_Code_Does_Not_Confirm_A_Phone()
    {
        var (client, _, _) = await SignedInAsync();
        var phone = NewPhone();
        var send = await PostFormAsync(client, "/Provider/MyPhone", "handler=Send", ("Phone", phone));
        var wrong = Codes.LastCode(phone) == "000000" ? "111111" : "000000";
        var confirm = await client.PostAsync("/Provider/MyPhone?handler=Confirm", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Phone"] = phone, ["Code"] = wrong, ["__RequestVerificationToken"] = TokenFrom(await BodyAsync(send)),
        }));
        Assert.Contains("not valid", await BodyAsync(confirm));
        Assert.Null((await GetDocumentAsync(client, "/Provider/MyPhone")).QuerySelector("[data-testid=confirmed]"));
    }

    [Fact]
    public async Task A_Number_Without_Its_Country_Code_Is_Refused()
    {
        var (client, _, _) = await SignedInAsync();
        var before = Codes.SentCount;
        var send = await PostFormAsync(client, "/Provider/MyPhone", "handler=Send", ("Phone", "202 555 0123"));
        Assert.Contains("country code", await BodyAsync(send));
        Assert.Equal(before, Codes.SentCount);
    }

    [Fact]
    public async Task Changing_The_Phone_Needs_A_Recent_Two_Factor_Code()
    {
        var (client, _, _) = await SignedInAsync();
        _app.Clock.Advance(TimeSpan.FromMinutes(6));
        try
        {
            var send = await PostFormAsync(client, "/Provider/MyPhone", "handler=Send", ("Phone", NewPhone()));
            Assert.Contains("/Provider/Reauthenticate", Location(send));
        }
        finally
        {
            _app.Clock.Advance(TimeSpan.FromMinutes(-6));
        }
    }

    [Fact]
    public async Task Removing_The_Phone_Ends_The_Text_Option()
    {
        var (client, email, _) = await SignedInAsync(ProviderRole.Billing);
        await ConfirmPhoneAsync(client, NewPhone());
        var removed = await PostFormAsync(client, "/Provider/MyPhone", "handler=Remove");
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.Contains("removed", (await GetDocumentAsync(client, "/Provider/MyPhone")).QuerySelector("[data-testid=message]")!.TextContent);

        using var next = await PasswordOnlyAsync(email);
        Assert.Null((await GetDocumentAsync(next, "/Provider/LoginWith2fa")).QuerySelector("[data-testid=use-text]"));
    }

    [Fact]
    public async Task Every_Console_Role_Can_Open_My_Phone()
    {
        foreach (var role in new[] { ProviderRole.Support, ProviderRole.Billing, ProviderRole.Owner })
        {
            var (client, _, _) = await SignedInAsync(role);
            var page = await GetDocumentAsync(client, "/Provider/MyPhone");
            Assert.NotNull(page.QuerySelector("[data-testid=stand-in]"));
            Assert.NotNull(page.QuerySelector("[data-testid=my-phone]"));
            client.Dispose();
        }
    }
}
