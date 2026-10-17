using System.Net;
using AngleSharp.Html.Parser;
using Inprem.Platform.Domain.Provider;
using InpremClockingApp.Tests.Support.Hosting;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>
/// Signing out of the console always ends on the console's sign-in page (developer's report,
/// 2026-10-17: "HTTP ERROR 400" instead), also when the page's form token no longer matches the
/// browser's sign-in: after the session timed out, or after signing in to the app in another tab.
/// </summary>
public class ConsoleSignOutTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public ConsoleSignOutTests(ProviderFixture app) => _app = app;

    private async Task<HttpClient> ConsoleAsync()
    {
        var email = $"so-{Guid.NewGuid():N}"[..14] + "@console.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, ProviderRole.Owner);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return client;
    }

    /// <summary>The sign-out form as the console page rendered it (its token belongs to that moment).</summary>
    private static async Task<(string Action, string Token)> SignOutFormAsync(HttpClient client)
    {
        var page = new HtmlParser().ParseDocument(await (await client.GetAsync("/Provider")).Content.ReadAsStringAsync());
        var form = page.Forms.Single(f => f.GetAttribute("data-role") == "sign-out");
        return (form.GetAttribute("action")!, form.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!);
    }

    private static Task<HttpResponseMessage> PressAsync(HttpClient client, (string Action, string Token) form) =>
        client.PostAsync(form.Action, new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = form.Token }));

    [Fact]
    public async Task Sign_Out_Goes_To_The_Console_Sign_In_Page()
    {
        using var client = await ConsoleAsync();
        var signedOut = await PressAsync(client, await SignOutFormAsync(client));
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.EndsWith("/Provider/Login", Location(signedOut));
        Assert.EndsWith("/Provider/Login", Location(await client.GetAsync("/Provider"))); // really signed out
    }

    [Fact]
    public async Task Only_Sign_Out_Goes_Without_The_Form_Token()
    {
        using var client = await ConsoleAsync();
        var note = await client.PostAsync($"/Provider/Tenants/AddNote/{_app.Data.Alpha.Id}", new FormUrlEncodedContent(new Dictionary<string, string> { ["Text"] = "no token" }));
        Assert.Equal(HttpStatusCode.BadRequest, note.StatusCode);
    }

    [Fact]
    public async Task Sign_Out_Still_Works_After_The_Session_Timed_Out()
    {
        using var client = await ConsoleAsync();
        var form = await SignOutFormAsync(client);
        _app.Clock.Advance(TimeSpan.FromMinutes(31)); // past the 30 idle minutes
        try
        {
            var signedOut = await PressAsync(client, form);
            Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
            Assert.EndsWith("/Provider/Login", Location(signedOut));
        }
        finally
        {
            _app.Clock.Advance(TimeSpan.FromMinutes(-31));
        }
    }

    [Fact]
    public async Task Sign_Out_Still_Works_After_Signing_In_To_The_App_In_Another_Tab()
    {
        using var client = await ConsoleAsync();
        var form = await SignOutFormAsync(client);
        await SignIn.AsAsync(client, _app.Data.Alpha.Admin!.Email, _app.Data.Password); // same browser, the app

        var signedOut = await PressAsync(client, form);
        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.EndsWith("/Provider/Login", Location(signedOut));
    }
}
