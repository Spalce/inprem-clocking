using System.Net;
using System.Text.RegularExpressions;
using Inprem.Platform.Application.Notifications;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>
/// Phase 5 step 5.2: an organization's admins can be asked for a texted code after their password
/// (a console setting, off by default). Off: nothing changes. On: no page of the organization opens
/// until a code is approved; a person without a confirmed number adds one with the same code and is
/// emailed about it; a code works once (P5-1).
/// </summary>
public class AdminSmsSignInTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public AdminSmsSignInTests(ProviderFixture app) => _app = app;

    private CapturingOtpService Codes => (CapturingOtpService)_app.Services.GetRequiredService<IOtpService>();

    private static string TokenFrom(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;

    private async Task SwitchAsync(int tenantId, bool on)
    {
        var email = $"sic-{Guid.NewGuid():N}"[..14] + "@console.test";
        var key = await ProviderFixture.CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email);
        using var console = _app.CreateClient();
        await ProviderFixture.SignInAsync(console, email, key);
        var saved = on
            ? await PostAsync(console, $"/Provider/Tenants/SignInCodes/{tenantId}", ("Required", "true"))
            : await PostAsync(console, $"/Provider/Tenants/SignInCodes/{tenantId}");
        Assert.EndsWith($"/Provider/Tenants/Details/{tenantId}", Location(saved));
    }

    private async Task<HttpClient> AppSignInAsync(string email)
    {
        var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);
        return client;
    }

    private static async Task<HttpResponseMessage> PostCodeAsync(HttpClient client, HttpResponseMessage sentPage, string code, string? phone = null)
    {
        var fields = new Dictionary<string, string> { ["Code"] = code, ["ReturnUrl"] = "/BackOffice", ["__RequestVerificationToken"] = TokenFrom(await BodyAsync(sentPage)) };
        if (phone != null) fields["Phone"] = phone;
        return await client.PostAsync("/Account/SmsSignIn", new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task Off_By_Default_Nothing_Changes()
    {
        using var admin = await AppSignInAsync(_app.Data.Alpha.Admin!.Email);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/BackOffice")).StatusCode);
    }

    [Fact]
    public async Task On_The_Admin_Adds_A_Number_Proves_It_And_Is_Told_By_Email()
    {
        var beta = _app.Data.Beta;
        await SwitchAsync(beta.Id, on: true);
        try
        {
            using var owner = await AppSignInAsync(beta.Owner!.Email);
            Assert.Equal("/Account/SmsSignIn?returnUrl=%2FBackOffice", Location(await owner.GetAsync("/BackOffice")));
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync("/Staff?handler=Create", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);

            var phone = "+1202555" + Random.Shared.Next(1000, 9999);
            var sent = await PostFormAsync(owner, "/Account/SmsSignIn?returnUrl=%2FBackOffice", "handler=Send", ("Phone", phone));
            Assert.Contains("We sent a code to", await BodyAsync(sent));
            var code = Codes.LastCode(phone)!;

            var wrong = await PostCodeAsync(owner, sent, code == "000000" ? "111111" : "000000", phone);
            Assert.Contains("not valid", await BodyAsync(wrong));
            Assert.Equal("/BackOffice", Location(await PostCodeAsync(owner, wrong, code, phone)));
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/BackOffice")).StatusCode);

            Assert.Contains(_app.Emails.To(beta.Owner.Email), e => e.Subject == "A phone number was added to your Inprem Clock-in account");
            var user = await _app.FindUserAsync(beta.Owner.Email);
            Assert.Equal((phone, true), (user.PhoneNumber, user.PhoneNumberConfirmed));

            // The next sign-in texts the confirmed number; the old code no longer works (P5-1).
            using var again = await AppSignInAsync(beta.Owner.Email);
            var sentAgain = await PostFormAsync(again, "/Account/SmsSignIn?returnUrl=%2FBackOffice", "handler=Send");
            Assert.Contains("We sent a code to", await BodyAsync(sentAgain));
            Assert.Contains(phone[^4..], await BodyAsync(sentAgain));
            var newCode = Codes.LastCode(phone)!;
            Assert.Contains("not valid", await BodyAsync(await PostCodeAsync(again, sentAgain, code == newCode ? "999999" : code)));
            Assert.Equal("/BackOffice", Location(await PostCodeAsync(again, sentAgain, newCode)));
        }
        finally
        {
            await SwitchAsync(beta.Id, on: false);
        }

        using var after = await AppSignInAsync(beta.Owner!.Email);
        Assert.Equal(HttpStatusCode.OK, (await after.GetAsync("/BackOffice")).StatusCode); // off again: password only

        await using var db = _app.CreatePlatformDbContext();
        var actions = await db.AuditEntries.AsNoTracking().Where(a => a.TenantId == beta.Id && a.Action.StartsWith("organization.sms-sign-in")).Select(a => a.Action).ToListAsync();
        Assert.Contains("organization.sms-sign-in-on", actions);
        Assert.Contains("organization.sms-sign-in-off", actions);
    }

    [Fact]
    public async Task The_Console_Switch_Needs_A_Recent_Two_Factor_Code_And_An_Owner()
    {
        var email = $"sid-{Guid.NewGuid():N}"[..14] + "@console.test";
        var key = await ProviderFixture.CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, Inprem.Platform.Domain.Provider.ProviderRole.Billing);
        using var billing = _app.CreateClient();
        await ProviderFixture.SignInAsync(billing, email, key);
        Assert.EndsWith("/Provider/AccessDenied", Location(await billing.GetAsync($"/Provider/Tenants/SignInCodes/{_app.Data.Alpha.Id}")));

        var ownerEmail = $"sio-{Guid.NewGuid():N}"[..14] + "@console.test";
        var ownerKey = await ProviderFixture.CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, ownerEmail);
        using var owner = _app.CreateClient();
        await ProviderFixture.SignInAsync(owner, ownerEmail, ownerKey);
        _app.Clock.Advance(TimeSpan.FromMinutes(6));
        try
        {
            var saved = await PostAsync(owner, $"/Provider/Tenants/SignInCodes/{_app.Data.Alpha.Id}", ("Required", "true"));
            Assert.Contains("/Provider/Reauthenticate", Location(saved));
        }
        finally
        {
            _app.Clock.Advance(TimeSpan.FromMinutes(-6));
        }
        using var scope = _app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<MessagingSettingsService>().SmsSignInRequiredAsync(_app.Data.Alpha.Id));
    }
}
