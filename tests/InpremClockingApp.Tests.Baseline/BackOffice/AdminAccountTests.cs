using System.Net;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Baseline.Helpers;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InpremClockingApp.Tests.Baseline.BackOffice;

/// <summary>The Admins page (/User): creating back-office admin accounts.</summary>
public class AdminAccountTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public AdminAccountTests(AppFixture app) => _app = app;

    private Dictionary<string, string> Form(string email, string? password = null) => new()
    {
        ["Input.Email"] = email,
        ["Input.FirstName"] = "New",
        ["Input.LastName"] = "Admin",
        ["Input.Password"] = password ?? _app.Data.Password,
    };

    [Fact]
    public async Task Admin_Create_Adds_An_Admin_To_The_Organization_Who_Can_Sign_In()
    {
        var email = $"newadmin-{Guid.NewGuid():N}"[..20] + "@alpha.test";
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await client.PostFormAsync("/User", Form(email));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/User", response.Headers.Location!.OriginalString);
        Assert.Contains($"Admin account created for {email}.", (await client.GetPageAsync("/User")).Body!.CleanText());

        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == email);
            Assert.Equal(_app.Data.Alpha.Id, user.TenantId);
            Assert.Equal("Admin", user.Type);
            Assert.True(await users.IsInRoleAsync(user, IdentitySeeder.AdminRole));
        }

        using var newSession = _app.CreateClient();
        await SignIn.AsAsync(newSession, email, _app.Data.Password);
        using var backOffice = await newSession.GetAsync("/BackOffice");
        Assert.Equal(HttpStatusCode.OK, backOffice.StatusCode);
    }

    [Fact]
    public async Task Admin_Create_Validation()
    {
        using var client = await _app.AdminOfAsync(_app.Data.Alpha);

        using var shortPassword = await client.PostFormAsync("/User", Form("short@alpha.test", "Ab1!"));
        using var sameOrgDuplicate = await client.PostFormAsync("/User", Form("admin@alpha.test"));

        Assert.Equal(HttpStatusCode.OK, shortPassword.StatusCode);
        Assert.Contains("The field Password must be a string with a minimum length of 6 and a maximum length of 100.",
            (await shortPassword.ReadDocumentAsync()).Body!.CleanText());
        Assert.Equal(HttpStatusCode.OK, sameOrgDuplicate.StatusCode);
        Assert.Contains("Username 'admin@alpha.test' is already taken.", (await sameOrgDuplicate.ReadDocumentAsync()).Body!.CleanText());
    }

    // Changed in Phase 0 step 0.4 (decision D2, C3-6). Until then this test recorded that Alpha and
    // Beta could each have an admin account with the same email, told apart at login by password.
    // Now an email is one account platform-wide: adding an existing email invites that account.
    [Fact]
    public async Task Admin_Existing_Email_From_Another_Organization_Is_Invited_Not_Duplicated()
    {
        var email = $"shared-{Guid.NewGuid():N}"[..20] + "@shared.test";
        using (var alpha = await _app.AdminOfAsync(_app.Data.Alpha))
        using (var r = await alpha.PostFormAsync("/User", Form(email)))
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        // Beta adds the same email, with a different password.
        using var beta = await _app.AdminOfAsync(_app.Data.Beta);
        using var invite = await beta.PostFormAsync("/User", Form(email, $"B{Guid.NewGuid():N}!y"));

        Assert.Equal(HttpStatusCode.Redirect, invite.StatusCode);
        var message = (await beta.GetPageAsync("/User")).Body!.CleanText();
        Assert.Contains($"{email} already has an account, so no new account was created. We have sent them an invitation to join this organization;", message);
        string userId;
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var accounts = await users.Users.IgnoreQueryFilters().Where(u => u.Email == email).ToListAsync();
            userId = Assert.Single(accounts).Id;
        }
        var invitation = Assert.Single(_app.Emails.To(email));
        Assert.Contains("/Account/Invitations", invitation.HtmlBody);
        Assert.Contains("Beta", invitation.HtmlBody);

        // The person keeps their own password and works in Alpha until they accept.
        using var person = _app.CreateClient();
        await SignIn.AsAsync(person, email, _app.Data.Password);
        Assert.Contains("staff1@alpha.test", (await person.GetPageAsync("/Staff")).Body!.CleanText());

        // Accepting: two organizations now, so they choose; then they work in Beta.
        var page = await person.GetPageAsync("/Account/Invitations");
        var accept = page.QuerySelectorAll("form").First(f => (f.GetAttribute("action") ?? "").Contains("handler=Accept", StringComparison.OrdinalIgnoreCase));
        var fields = accept.QuerySelectorAll("input[type=hidden]").ToDictionary(i => i.GetAttribute("name")!, i => i.GetAttribute("value") ?? "");
        using var accepted = await person.PostFormAsync(accept.GetAttribute("action")!, fields);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);

        using var next = await person.GetAsync("/Staff");
        Assert.StartsWith("/Account/ChooseOrganization", next.Headers.Location!.OriginalString);
        var chooser = await person.GetPageAsync(next.Headers.Location.OriginalString);
        var betaForm = chooser.QuerySelectorAll("[data-testid=organizations] form").First(f => f.TextContent.Contains("Beta"));
        var choose = betaForm.QuerySelectorAll("input[type=hidden]").ToDictionary(i => i.GetAttribute("name")!, i => i.GetAttribute("value") ?? "");
        using var chosen = await person.PostFormAsync("/Account/ChooseOrganization", choose);
        Assert.Contains("staff1@beta.test", (await person.GetPageAsync("/Staff")).Body!.CleanText());

        // And Beta's Admins page now lists them.
        Assert.Contains(email, (await beta.GetPageAsync("/User")).Body!.CleanText());
        Assert.False(string.IsNullOrEmpty(userId));
    }

    [Fact]
    public async Task Admin_Existing_Email_Already_In_This_Organization_Is_Refused_As_Before()
    {
        using var alpha = await _app.AdminOfAsync(_app.Data.Alpha);

        using var response = await alpha.PostFormAsync("/User", Form(_app.Data.Alpha.Owner!.Email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Username '{_app.Data.Alpha.Owner.Email}' is already taken.", (await response.ReadDocumentAsync()).Body!.CleanText());
        Assert.Empty(_app.Emails.To(_app.Data.Alpha.Owner.Email));
    }
}
