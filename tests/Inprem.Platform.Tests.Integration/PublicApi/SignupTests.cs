using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Inprem.Platform.Application.Billing;
using Inprem.Platform.Domain.Billing;
using Inprem.Platform.Domain.Notifications;
using Inprem.Platform.Domain.Memberships;
using Inprem.Platform.Domain.Organizations;
using Inprem.Platform.Domain.PublicApi;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.PublicApi;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.PublicApi;

/// <summary>
/// Phase 3 step 3.3 (J1, J2; P3-3, P3-4, P3-5, P3-8): website registration end to end: signup,
/// verification email, the app's verification page, sign-in only after verification; validation,
/// CAPTCHA, conflicts, idempotency, token scope, resend limit.
/// </summary>
public class SignupTests : IClassFixture<PublicApiFixture>
{
    private const string Api = "/api/public/v1";
    private const string StrongPassword = "Signup-Pass-2026!";
    private readonly PublicApiFixture _app;

    public SignupTests(PublicApiFixture app) => _app = app;

    private async Task<PlanPrice> PriceAsync(bool isPublic = true, int trialDays = 30)
    {
        using var scope = _app.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<PlanService>();
        var id = (await plans.CreateAsync($"su-{Guid.NewGuid():N}"[..12], new("Signup plan", null, null, null, trialDays, isPublic), BillingCycle.Monthly, 15m, "USD", "test")).PlanId!.Value;
        return (await plans.FindAsync(id))!.Prices[0];
    }

    private static object Body(Guid priceId, string mode = "trial", string? email = null, string? slug = null, string captcha = FakeCaptchaVerifier.Pass) => new
    {
        organizationName = "Website Centre",
        slug = slug ?? $"web-{Guid.NewGuid():N}"[..14],
        timeZone = "America/New_York",
        owner = new { firstName = "Ama", lastName = "Owusu", email = email ?? $"owner-{Guid.NewGuid():N}"[..16] + "@signup.test", mobile = "+15551234567" },
        consent = new { acceptTerms = true, acceptPrivacyPolicy = true, smsOptIn = true },
        planPriceId = priceId,
        mode,
        captchaToken = captcha,
    };

    private static HttpRequestMessage Post(string path, object? body, string? key = null, string? token = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Api + path) { Content = new StringContent(body == null ? "" : JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        if (token != null) request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static HttpRequestMessage Get(string path, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Api + path);
        if (token != null) request.Headers.Authorization = new("Bearer", token);
        return request;
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return (await PublicApiFixture.JsonAsync(response)).GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task The_Text_Box_The_Owner_Ticked_Is_Recorded_With_Its_Wording()
    {
        // Phase 5 step 5.1: texts only to owners who agreed; the consent keeps who, when, how and the wording.
        var price = await PriceAsync();
        using var website = _app.CreateClient();
        var ticked = await website.SendAsync(Post("/signups", Body(price.Id)));
        var notTicked = await website.SendAsync(Post("/signups", new
        {
            organizationName = "Website Centre", slug = $"web-{Guid.NewGuid():N}"[..14], timeZone = "America/New_York",
            owner = new { firstName = "Kojo", lastName = "Mensah", email = $"owner-{Guid.NewGuid():N}"[..16] + "@signup.test", mobile = "+15557654321" },
            consent = new { acceptTerms = true, acceptPrivacyPolicy = true, smsOptIn = false },
            planPriceId = price.Id, mode = "trial", captchaToken = FakeCaptchaVerifier.Pass,
        }));
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (ticked.StatusCode, notTicked.StatusCode));
        var signupId = (await PublicApiFixture.JsonAsync(ticked)).GetProperty("signupId").GetGuid();

        await using var db = _app.CreatePlatformDbContext();
        var signup = await db.Signups.AsNoTracking().SingleAsync(s => s.Id == signupId);
        var consent = await db.ConsentRecords.AsNoTracking().SingleAsync(c => c.Phone == "+15551234567" && c.TenantId == signup.TenantId);
        Assert.Equal((ConsentAction.Grant, ConsentSources.WebsiteSignup, signup.OwnerUserId, $"signup:{signupId}"), (consent.Action, consent.Source, consent.PersonRef, consent.RecordedBy));
        Assert.Equal(Inprem.Platform.Application.Notifications.ConsentWording.OwnerAlerts, consent.Wording);
        Assert.False(await db.ConsentRecords.AnyAsync(c => c.Phone == "+15557654321"));
    }

    [Fact]
    public async Task Inprem_Hears_About_A_New_Signup()
    {
        // Phase 5 step 5.3: "New signup" to the console users who want it (all roles by default).
        var support = $"ns-{Guid.NewGuid():N}"[..13] + "@console.test";
        await Inprem.Platform.Tests.Integration.Provider.ProviderFixture.CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, support, Inprem.Platform.Domain.Provider.ProviderRole.Support);
        var price = await PriceAsync();
        using var website = _app.CreateClient();
        var slug = $"ns-{Guid.NewGuid():N}"[..12];
        Assert.Equal(HttpStatusCode.Created, (await website.SendAsync(Post("/signups", Body(price.Id, slug: slug)))).StatusCode);

        using (var scope = _app.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Inprem.Platform.Application.Notifications.NotificationSender>().SendDueAsync();
        var alert = Assert.Single(_app.Emails.To(support), e => e.Subject == "[Inprem] New signup: Website Centre");
        Assert.Contains("free trial, plan Signup plan", alert.HtmlBody);
    }

    [Fact]
    public async Task Trial_Signup_Verify_Email_Choose_Password_Then_Sign_In()
    {
        var price = await PriceAsync();
        var email = $"j1-{Guid.NewGuid():N}"[..12] + "@signup.test";
        var slug = $"j1-{Guid.NewGuid():N}"[..12];
        using var website = _app.CreateClient();

        var created = await website.SendAsync(Post("/signups", Body(price.Id, email: email, slug: slug)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await PublicApiFixture.JsonAsync(created);
        var signupId = json.GetProperty("signupId").GetGuid();
        var token = json.GetProperty("signupToken").GetString()!;
        Assert.Equal(("PendingVerification", "verify_email"), (json.GetProperty("status").GetString(), json.GetProperty("nextAction").GetString()));
        Assert.True(json.GetProperty("signupTokenExpiresAt").GetDateTime() > DateTime.UtcNow.AddMinutes(100));
        Assert.Equal($"{Api}/signups/{signupId}", created.Headers.Location!.ToString());
        Assert.DoesNotContain("@", await created.Content.ReadAsStringAsync()); // no personal data back

        // Everything exists, waiting for the owner.
        int tenantId;
        await using (var db = _app.CreatePlatformDbContext())
        {
            var signup = await db.Signups.AsNoTracking().SingleAsync(s => s.Id == signupId);
            tenantId = signup.TenantId;
            Assert.Equal((SignupMode.Trial, SignupStatus.PendingVerification, true), (signup.Mode, signup.Status, signup.SmsOptIn));
            Assert.Equal((OrganizationStatus.PendingVerification, slug), await db.TenantProfiles.Where(p => p.TenantId == tenantId).Select(p => ValueTuple.Create(p.Status, p.Slug)).SingleAsync());
            var subscription = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.TenantId == tenantId);
            Assert.Equal((SubscriptionStatus.Trialing, BillingMode.PayPal, price.Id), (subscription.Status, subscription.Mode, subscription.PlanPriceId));
            Assert.Equal(MembershipRole.Owner, (await db.TenantMemberships.SingleAsync(m => m.TenantId == tenantId)).Role);
        }
        using (var scope = _app.Services.CreateScope())
        {
            var account = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email))!;
            Assert.Equal((false, false), (account.EmailConfirmed, await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().HasPasswordAsync(account)));
        }

        // The website polls the status with its token.
        var status = await PublicApiFixture.JsonAsync(await website.SendAsync(Get($"/signups/{signupId}", token)));
        Assert.Equal(("PendingVerification", "Trialing"), (status.GetProperty("status").GetString(), status.GetProperty("subscriptionStatus").GetString()));

        // The owner opens the email link; a weak password is refused without using up the link.
        var link = LinkSentTo(_app.Emails, email, "/Account/VerifyEmail");
        using var browser = _app.CreateBrowserClient();
        var page = new HtmlParser().ParseDocument(await (await browser.GetAsync(link)).Content.ReadAsStringAsync());
        Assert.Contains(email, page.QuerySelector("[data-testid=verify-email]")!.TextContent);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("https://x" + link).Query);
        async Task<string> SubmitAsync(string password) => await (await browser.PostAsync("/Account/VerifyEmail", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Signup"] = query["signup"]!, ["Code"] = query["code"]!, ["Password"] = password, ["ConfirmPassword"] = password,
            ["__RequestVerificationToken"] = page.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!,
        }))).Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"error\"", await SubmitAsync("weak"));
        Assert.Contains("data-testid=\"done\"", await SubmitAsync(StrongPassword));

        // Verified: the organization is Active and the owner can sign in to the back office.
        await using (var db = _app.CreatePlatformDbContext())
        {
            Assert.Equal(OrganizationStatus.Active, (await db.TenantProfiles.AsNoTracking().SingleAsync(p => p.TenantId == tenantId)).Status);
            Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "signup.verified" && a.TenantId == tenantId));
        }
        Assert.Equal("Active", (await PublicApiFixture.JsonAsync(await website.SendAsync(Get($"/signups/{signupId}", token)))).GetProperty("status").GetString());
        using var owner = _app.CreateBrowserClient();
        await SignIn.AsAsync(owner, email, StrongPassword);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/BackOffice")).StatusCode);

        // The link works once.
        Assert.Contains("data-testid=\"done\"", await (await browser.GetAsync(link)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Subscribe_Signup_Waits_For_Payment_Without_Access()
    {
        var price = await PriceAsync();
        using var website = _app.CreateClient();
        var json = await PublicApiFixture.JsonAsync(await website.SendAsync(Post("/signups", Body(price.Id, mode: "subscribe"))));
        Assert.Equal("checkout", json.GetProperty("nextAction").GetString());

        await using var db = _app.CreatePlatformDbContext();
        var signup = await db.Signups.AsNoTracking().SingleAsync(s => s.Id == json.GetProperty("signupId").GetGuid());
        var subscription = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.TenantId == signup.TenantId);
        Assert.Equal(SubscriptionStatus.PendingActivation, subscription.Status);
        Assert.False(subscription.HasFullAccess(DateTime.UtcNow));
    }

    [Fact]
    public async Task Bad_Requests_Get_Their_Codes()
    {
        var price = await PriceAsync();
        using var website = _app.CreateClient();
        website.DefaultRequestHeaders.Add("X-Api-Key", PublicApiFixture.ApiKey); // many requests

        var invalid = await website.SendAsync(Post("/signups", new { organizationName = "x", slug = "Bad Slug", timeZone = "Mars/Base", owner = new { email = "not-an-email", mobile = "555" }, consent = new { acceptTerms = false }, mode = "free" }));
        Assert.Equal(Problems.ValidationFailed, await CodeAsync(invalid));
        var errors = (await PublicApiFixture.JsonAsync(await website.SendAsync(Post("/signups", new { slug = "Bad Slug", owner = new { email = "nope" }, mode = "free" })))).GetProperty("errors");
        foreach (var field in new[] { "organizationName", "slug", "timeZone", "owner.firstName", "owner.email", "consent.acceptTerms", "consent.acceptPrivacyPolicy", "planPriceId", "mode", "captchaToken" })
            Assert.True(errors.TryGetProperty(field, out _), $"{field} has no error");

        Assert.Equal(Problems.CaptchaFailed, await CodeAsync(await website.SendAsync(Post("/signups", Body(price.Id, captcha: "robot")))));
        Assert.Equal(Problems.PlanNotAvailable, await CodeAsync(await website.SendAsync(Post("/signups", Body(Guid.NewGuid())))));
        Assert.Equal(Problems.PlanNotAvailable, await CodeAsync(await website.SendAsync(Post("/signups", Body((await PriceAsync(isPublic: false)).Id)))));
        Assert.Equal(Problems.PlanNotAvailable, await CodeAsync(await website.SendAsync(Post("/signups", Body((await PriceAsync(trialDays: 0)).Id, mode: "trial")))));
        Assert.Equal(Problems.EmailInUse, await CodeAsync(await website.SendAsync(Post("/signups", Body(price.Id, email: _app.Data.Alpha.Admin!.Email)))));

        var slug = $"taken-{Guid.NewGuid():N}"[..14];
        Assert.Equal(HttpStatusCode.Created, (await website.SendAsync(Post("/signups", Body(price.Id, slug: slug)))).StatusCode);
        var taken = await website.SendAsync(Post("/signups", Body(price.Id, slug: slug)));
        Assert.Equal((HttpStatusCode.Conflict, Problems.SlugTaken), (taken.StatusCode, await CodeAsync(taken)));
        Assert.Equal(Problems.ValidationFailed, await CodeAsync(await website.SendAsync(Post("/signups", Body(price.Id, slug: "admin")))));
    }

    [Fact]
    public async Task The_Same_Signup_Twice_Is_One_Organization_And_The_Stored_Answer_Is_Encrypted()
    {
        var price = await PriceAsync();
        var key = Guid.NewGuid().ToString();
        var body = Body(price.Id);
        using var website = _app.CreateClient();
        var first = await website.SendAsync(Post("/signups", body, key));
        var again = await website.SendAsync(Post("/signups", body, key));
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, again.StatusCode));
        var json = await PublicApiFixture.JsonAsync(first);
        Assert.Equal(json.GetProperty("signupId").GetGuid(), (await PublicApiFixture.JsonAsync(again)).GetProperty("signupId").GetGuid());

        await using var db = _app.CreatePlatformDbContext();
        var slug = JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty("slug").GetString();
        Assert.Equal(1, await db.TenantProfiles.CountAsync(p => p.Slug == slug));
        var stored = await db.IdempotencyRecords.AsNoTracking().SingleAsync(r => r.Key == key);
        Assert.DoesNotContain(json.GetProperty("signupToken").GetString()!, stored.ResponseBody!);
    }

    [Fact]
    public async Task The_Same_Key_With_A_Changed_Signup_Is_Refused_Not_Answered_From_The_First()
    {
        // The endpoint reads its JSON body before the idempotency filter runs: the filter must still see that body.
        var price = await PriceAsync();
        var key = Guid.NewGuid().ToString();
        var slug = $"web-{Guid.NewGuid():N}"[..14];
        using var website = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Created, (await website.SendAsync(Post("/signups", Body(price.Id, slug: slug), key))).StatusCode);

        var changed = await website.SendAsync(Post("/signups", Body(price.Id, slug: slug + "-b"), key));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, changed.StatusCode);
        Assert.Equal(Problems.IdempotencyKeyReused, await CodeAsync(changed));
    }

    [Fact]
    public async Task A_Signup_Token_Sees_Only_Its_Own_Signup_And_Expires()
    {
        var price = await PriceAsync();
        using var website = _app.CreateClient();
        var a = await PublicApiFixture.JsonAsync(await website.SendAsync(Post("/signups", Body(price.Id))));
        var b = await PublicApiFixture.JsonAsync(await website.SendAsync(Post("/signups", Body(price.Id))));
        var tokenA = a.GetProperty("signupToken").GetString()!;
        var idB = b.GetProperty("signupId").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await website.SendAsync(Get($"/signups/{a.GetProperty("signupId").GetGuid()}", tokenA))).StatusCode);
        Assert.Equal(Problems.NotFound, await CodeAsync(await website.SendAsync(Get($"/signups/{idB}", tokenA))));
        Assert.Equal(Problems.TokenMissing, await CodeAsync(await website.SendAsync(Get($"/signups/{idB}", null))));
        Assert.Equal(Problems.TokenInvalid, await CodeAsync(await website.SendAsync(Get($"/signups/{idB}", await _app.TokenAsync(billing: true)))));

        _app.Clock.Advance(ApiToken.SignupLifetime + TimeSpan.FromSeconds(1));
        try
        {
            Assert.Equal(Problems.TokenExpired, await CodeAsync(await website.SendAsync(Get($"/signups/{a.GetProperty("signupId").GetGuid()}", tokenA))));
        }
        finally
        {
            _app.Clock.Advance(-(ApiToken.SignupLifetime + TimeSpan.FromSeconds(1)));
        }
    }

    [Fact]
    public async Task Resend_At_Most_Three_Per_Hour_And_Not_After_Verification()
    {
        var price = await PriceAsync();
        var email = $"rs-{Guid.NewGuid():N}"[..12] + "@signup.test";
        using var website = _app.CreateClient();
        var json = await PublicApiFixture.JsonAsync(await website.SendAsync(Post("/signups", Body(price.Id, email: email))));
        var (id, token) = (json.GetProperty("signupId").GetGuid(), json.GetProperty("signupToken").GetString()!);

        for (var i = 0; i < Signup.MaxResendsPerHour; i++) Assert.Equal(HttpStatusCode.Accepted, (await website.SendAsync(Post($"/signups/{id}/resend-verification", null, token: token))).StatusCode);
        Assert.Equal(Problems.ResendLimitReached, await CodeAsync(await website.SendAsync(Post($"/signups/{id}/resend-verification", null, token: token))));
        Assert.Equal(1 + Signup.MaxResendsPerHour, _app.Emails.To(email).Count());

        _app.Clock.Advance(TimeSpan.FromHours(1));
        try
        {
            Assert.Equal(HttpStatusCode.Accepted, (await website.SendAsync(Post($"/signups/{id}/resend-verification", null, token: token))).StatusCode);
        }
        finally
        {
            _app.Clock.Advance(-TimeSpan.FromHours(1));
        }

        using (var scope = _app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<Inprem.Platform.Application.PublicApi.SignupService>().MarkVerifiedAsync(id);
        Assert.Equal(Problems.AlreadyVerified, await CodeAsync(await website.SendAsync(Post($"/signups/{id}/resend-verification", null, token: token))));
    }
}
