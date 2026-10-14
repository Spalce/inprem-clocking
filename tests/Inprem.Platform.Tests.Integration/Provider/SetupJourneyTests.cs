using System.Net;
using Inprem.Platform.Application.Provider;
using InpremClockingApp.Tests.Support.Database;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>
/// Phase 0 step 0.7 end to end, as a host without a console would do it (D13): the one-time
/// setup page, the owner's invitation, two-factor enrolment, provider sign-in, Level 1.
/// </summary>
public class SetupJourneyTests : IClassFixture<ProviderFixture>
{
    private const string Owner = "first.owner@provider.test";
    private readonly ProviderFixture _app;

    public SetupJourneyTests(ProviderFixture app) => _app = app;

    [Fact]
    public async Task Provider_First_Owner_Journey_Through_The_Setup_Page()
    {
        using var visitor = _app.CreateClient();

        // 1. Before any owner exists the setup page is there; nothing of the console is reachable.
        await GetDocumentAsync(visitor, "/Provider/setup");
        Assert.EndsWith("/Provider/Login", Location(await visitor.GetAsync("/Provider")));

        // 2. A wrong code is refused, counted and audited (anonymous, by address); nothing is created.
        var wrong = await PostAsync(visitor, "/Provider/setup", ("Email", Owner), ("Code", "WRONG-CODE"));
        Assert.Contains("not valid", await BodyAsync(wrong));
        await using (var platform = _app.CreatePlatformDbContext())
        {
            Assert.Contains(await platform.AuditEntries.ToListAsync(), a => a.Action == "provider.bootstrap.refused" && a.Actor.StartsWith("anonymous:"));
            Assert.Equal(1, await platform.SecurityAttempts.CountAsync(a => !a.Succeeded));
            Assert.Empty(await platform.ProviderUsers.ToListAsync());
        }

        // 3. The right code invites the owner; the page is gone for good (404, also for POST).
        var done = await PostAsync(visitor, "/Provider/setup", ("Email", Owner), ("Code", BootstrapCode));
        Assert.Contains("setup link was emailed", await BodyAsync(done));
        Assert.Equal(HttpStatusCode.NotFound, (await visitor.GetAsync("/Provider/setup")).StatusCode);
        // (No form to take an antiforgery token from: a bare POST is refused before the page runs.)
        var again = await visitor.PostAsync("/Provider/setup", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "x@y.test", ["Code"] = BootstrapCode }));
        Assert.Contains(again.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });

        // 4. Codes and link tokens are stored only as hashes.
        var link = LinkSentTo(_app.Emails, Owner);
        var token = TokenOf(link);
        await using (var platform = _app.CreatePlatformDbContext())
        {
            Assert.Equal(SecretTokens.Hash(token), (await platform.ProviderInvitations.SingleAsync()).TokenHash);
            Assert.Equal(SecretTokens.Hash(BootstrapCode), (await platform.UsedCodes.SingleAsync()).CodeHash);
        }
        await AssertNoStoredSecretAsync(token, BootstrapCode);
        Assert.False(await HasPasswordAsync(Owner));

        // 5. The link sets the password; the console still needs two-factor.
        using var owner = _app.CreateClient();
        Assert.Contains(Owner, (await GetDocumentAsync(owner, link)).Body!.TextContent);
        var mismatch = await PostAsync(owner, link, ("Password", StrongPassword), ("ConfirmPassword", "Different-Pass-2!"));
        Assert.Contains("do not match", await BodyAsync(mismatch));
        var accepted = await PostAsync(owner, link, ("Password", StrongPassword), ("ConfirmPassword", StrongPassword));
        Assert.EndsWith("/Provider/EnrolTwoFactor", Location(accepted));
        Assert.EndsWith("/Provider/EnrolTwoFactor", Location(await owner.GetAsync("/Provider")));
        Assert.NotNull((await GetDocumentAsync(_app.CreateClient(), link)).QuerySelector("[data-testid=invalid]")); // the link worked once

        // 6. Enrolment: a wrong code is refused; the right one turns two-factor on and shows ten codes once.
        var enrolPage = await GetDocumentAsync(owner, "/Provider/EnrolTwoFactor");
        var key = enrolPage.QuerySelector("[data-testid=shared-key]")!.TextContent.Trim();
        Assert.Contains("not valid", await BodyAsync(await PostAsync(owner, "/Provider/EnrolTwoFactor", ("Code", "000000"))));
        var (sameKey, recoveryCodes) = await EnrolAsync(owner);
        Assert.Equal(key, sameKey);
        Assert.Equal(HttpStatusCode.Redirect, (await owner.GetAsync("/Provider/EnrolTwoFactor")).StatusCode); // not shown again

        // 7. Console: reachable, and it warns that a second owner is missing (D16); so does /health.
        var console = await GetDocumentAsync(owner, "/Provider");
        Assert.NotNull(console.QuerySelector("[data-testid=owners-warning]"));
        Assert.Equal("Degraded", await (await visitor.GetAsync("/health")).Content.ReadAsStringAsync());

        // 8. Sign out; sign in again needs the authenticator code.
        await PostAsync(owner, "/Provider", Array.Empty<(string, string)>());
        Assert.EndsWith("/Provider/Login", Location(await owner.GetAsync("/Provider")));
        var password = await PostAsync(owner, "/Provider/Login", ("Email", Owner), ("Password", StrongPassword));
        Assert.EndsWith("/Provider/LoginWith2fa", Location(password));
        Assert.EndsWith("/Provider/Login", Location(await owner.GetAsync("/Provider"))); // password alone is not enough
        Assert.Contains("not valid", await BodyAsync(await PostAsync(owner, "/Provider/LoginWith2fa", ("Code", "000000"))));
        Assert.EndsWith("/Provider", Location(await PostAsync(owner, "/Provider/LoginWith2fa", ("Code", Totp.Code(key)))));
        await GetDocumentAsync(owner, "/Provider");

        // 9. Level 1: a recovery code signs in, but only to enrol a new authenticator; the old key is dead.
        using var lostPhone = _app.CreateClient();
        await PostAsync(lostPhone, "/Provider/Login", ("Email", Owner), ("Password", StrongPassword));
        var recovered = await PostAsync(lostPhone, "/Provider/LoginWithRecoveryCode", ("RecoveryCode", recoveryCodes[0]));
        Assert.EndsWith("/Provider/EnrolTwoFactor", Location(recovered));
        Assert.EndsWith("/Provider/EnrolTwoFactor", Location(await lostPhone.GetAsync("/Provider")));
        Assert.Contains(_app.Emails.To(Owner), e => e.Subject.Contains("recovery code was used"));
        await using (var platform = _app.CreatePlatformDbContext())
        {
            Assert.Contains(await platform.AuditEntries.ToListAsync(), a => a.Action == "provider.recovery.level1");
        }
        var (newKey, newCodes) = await EnrolAsync(lostPhone);
        Assert.NotEqual(key, newKey);
        await GetDocumentAsync(lostPhone, "/Provider");

        using var oldPhone = _app.CreateClient();
        await PostAsync(oldPhone, "/Provider/Login", ("Email", Owner), ("Password", StrongPassword));
        Assert.Contains("not valid", await BodyAsync(await PostAsync(oldPhone, "/Provider/LoginWith2fa", ("Code", Totp.Code(key)))));
        Assert.Contains("not valid", await BodyAsync(await PostAsync(oldPhone, "/Provider/LoginWithRecoveryCode", ("RecoveryCode", recoveryCodes[1]))));
        Assert.EndsWith("/Provider/EnrolTwoFactor", Location(await PostAsync(oldPhone, "/Provider/LoginWithRecoveryCode", ("RecoveryCode", newCodes[0]))));
    }

    [Fact]
    public async Task Provider_Login_Refuses_Tenant_Accounts_And_Keeps_Their_App_Session()
    {
        var alphaOwner = _app.Data.Alpha.Owner!;
        using var client = _app.CreateClient();

        var refused = await PostAsync(client, "/Provider/Login", ("Email", alphaOwner.Email), ("Password", _app.Data.Password));
        Assert.Contains("Invalid sign-in attempt.", await BodyAsync(refused));

        // Signed in to the App site (shared cookie with SiteMode All): no console ("No access"), App session kept.
        await SignIn.AsAsync(client, alphaOwner.Email, _app.Data.Password);
        Assert.EndsWith("/Provider/AccessDenied", Location(await client.GetAsync("/Provider")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/VolunteerAttendance")).StatusCode);
    }

    [Fact]
    public async Task Provider_Forms_Need_The_Antiforgery_Token()
    {
        using var client = _app.CreateClient();
        var posted = await client.PostAsync("/Provider/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "a@b.test", ["Password"] = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, posted.StatusCode);
    }

    [Fact]
    public async Task Provider_Pages_Send_No_Referrer()
    {
        using var client = _app.CreateClient();
        var response = await client.GetAsync("/Provider/Login");
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Provider_Setup_Page_Does_Not_Exist_Without_A_Bootstrap_Code()
    {
        await using var db = await TestDatabase.CreateAsync(nameof(Provider_Setup_Page_Does_Not_Exist_Without_A_Bootstrap_Code));
        await using var app = new InpremAppFactory(db.ConnectionString, new Dictionary<string, string?> { ["Site:SiteMode"] = "All" });
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Provider/setup")).StatusCode);
    }

    private async Task<bool> HasPasswordAsync(string email)
    {
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<InpremClockingApp.Models.Identity.AppUser>>();
        return await users.HasPasswordAsync((await users.FindByEmailAsync(email))!);
    }

    /// <summary>No text column of any platform table contains the given secrets.</summary>
    private async Task AssertNoStoredSecretAsync(params string[] secrets)
    {
        await using var platform = _app.CreatePlatformDbContext();
        var connection = platform.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var columns = connection.CreateCommand();
        columns.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'platform' AND DATA_TYPE IN ('nvarchar','varchar')";
        var list = new List<(string Table, string Column)>();
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync()) list.Add((reader.GetString(0), reader.GetString(1)));
        }
        Assert.NotEmpty(list);
        foreach (var (table, column) in list)
        {
            foreach (var secret in secrets)
            {
                await using var check = connection.CreateCommand();
                check.CommandText = $"SELECT COUNT(*) FROM [platform].[{table}] WHERE [{column}] LIKE '%' + @s + '%'";
                var parameter = check.CreateParameter();
                parameter.ParameterName = "@s";
                parameter.Value = secret;
                check.Parameters.Add(parameter);
                Assert.True((int)(await check.ExecuteScalarAsync())! == 0, $"platform.{table}.{column} contains a secret.");
            }
        }
    }
}
