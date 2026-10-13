using System.Net;
using System.Text;
using AngleSharp.Html.Parser;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Memberships;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Memberships;

/// <summary>
/// Step 0.4: one account per email platform-wide, invitations instead of duplicate accounts,
/// standard password reset. (Lockout and the Admins page are covered in the behaviour baseline.)
/// </summary>
public class AccountRulesTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public AccountRulesTests(AppFixture app) => _app = app;

    private async Task<(string UserId, string Email)> CreateAccountAsync(int? tenantId)
    {
        var email = $"a-{Guid.NewGuid():N}"[..18] + "@rules.test";
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, TenantId = tenantId, Type = IdentitySeeder.AdminRole };
        Assert.True((await users.CreateAsync(user, _app.Data.Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, IdentitySeeder.AdminRole)).Succeeded);
        return (user.Id, email);
    }

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient client, string pagePath, string handler)
    {
        var page = new HtmlParser().ParseDocument(await (await client.GetAsync(pagePath)).Content.ReadAsStringAsync());
        var form = page.QuerySelectorAll("form").First(f => (f.GetAttribute("action") ?? "").Contains($"handler={handler}", StringComparison.OrdinalIgnoreCase));
        var fields = form.QuerySelectorAll("input[type=hidden]").ToDictionary(i => i.GetAttribute("name")!, i => i.GetAttribute("value") ?? "");
        return await client.PostAsync(form.GetAttribute("action"), new FormUrlEncodedContent(fields));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Account_Second_Account_With_The_Same_Email_Is_Refused_Platform_Wide(bool differentCase)
    {
        var (_, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        var again = differentCase ? email.ToUpperInvariant() : email;
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var result = await users.CreateAsync(new AppUser { UserName = again, Email = again, TenantId = _app.Data.Beta.Id }, _app.Data.Password);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "DuplicateUserName");
    }

    [Fact]
    public async Task Account_A_Different_Email_Is_Fine_And_Email_Uniqueness_Covers_Changed_Emails()
    {
        var (_, first) = await CreateAccountAsync(_app.Data.Alpha.Id);
        var (secondId, _) = await CreateAccountAsync(_app.Data.Beta.Id);
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var second = await users.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == secondId);

        // Taking someone else's email by changing your own is refused too.
        var result = await users.SetEmailAsync(second, first);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "DuplicateEmail");
    }

    [Fact]
    public async Task Account_Invitation_Can_Be_Declined()
    {
        var (userId, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        Guid invitationId;
        using (var scope = _app.Services.CreateScope())
        {
            var memberships = scope.ServiceProvider.GetRequiredService<IMembershipService>();
            await memberships.InviteAsync(userId, _app.Data.Beta.Id, MembershipRole.Admin, _app.Data.Beta.Owner!.UserId);
            invitationId = (await memberships.ListInvitationsAsync(userId)).Single().MembershipId;
        }
        using var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);

        using var declined = await SubmitAsync(client, "/Account/Invitations", "Decline");

        Assert.Equal(HttpStatusCode.Redirect, declined.StatusCode);
        var page = await (await client.GetAsync("/Account/Invitations")).Content.ReadAsStringAsync();
        Assert.Contains("You declined the invitation from Beta.", page);
        Assert.Contains("You have no pending invitations.", page);
        await using var db = _app.CreatePlatformDbContext();
        Assert.Equal(MembershipStatus.Revoked, (await db.TenantMemberships.SingleAsync(m => m.Id == invitationId)).Status);
        // Still works in Alpha.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Staff")).StatusCode);
    }

    [Fact]
    public async Task Account_Revoked_Member_Is_Invited_Again_From_The_Admins_Page()
    {
        var (userId, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        await using (var db = _app.CreatePlatformDbContext())
        {
            var revoked = TenantMembership.CreateActive(userId, _app.Data.Beta.Id, MembershipRole.Admin, DateTime.UtcNow);
            revoked.Revoke();
            db.TenantMemberships.Add(revoked);
            await db.SaveChangesAsync();
        }
        using var beta = _app.CreateClient();
        await SignIn.AsAsync(beta, _app.Data.Beta.Admin!.Email, _app.Data.Password);

        using var response = await beta.PostAsync("/User", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email, ["Input.FirstName"] = "Again", ["Input.LastName"] = "Invited", ["Input.Password"] = _app.Data.Password,
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var check = _app.CreatePlatformDbContext();
        Assert.Equal(MembershipStatus.Invited, (await check.TenantMemberships.SingleAsync(m => m.UserId == userId && m.TenantId == _app.Data.Beta.Id)).Status);
        Assert.Single(_app.Emails.To(email));
    }

    [Fact]
    public async Task Account_With_Only_An_Invitation_Can_Still_Accept_It()
    {
        // e.g. an account whose own organization revoked it, invited elsewhere.
        var (userId, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        await using (var db = _app.CreatePlatformDbContext())
        {
            var revoked = TenantMembership.CreateActive(userId, _app.Data.Alpha.Id, MembershipRole.Admin, DateTime.UtcNow);
            revoked.Revoke();
            db.TenantMemberships.Add(revoked);
            db.TenantMemberships.Add(TenantMembership.CreateInvited(userId, _app.Data.Gamma.Id, MembershipRole.Admin, _app.Data.Beta.Owner!.UserId, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        using var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);
        Assert.StartsWith("https://localhost/Identity/Account/Login", (await client.GetAsync("/Staff")).Headers.Location!.ToString());
        await SignIn.AsAsync(client, email, _app.Data.Password);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Invitations")).StatusCode);
        using var accepted = await SubmitAsync(client, "/Account/Invitations", "Accept");

        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Staff")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/BackOffice")).StatusCode);
    }

    [Fact]
    public async Task Account_Password_Reset_Uses_The_Single_Account()
    {
        var (userId, email) = await CreateAccountAsync(_app.Data.Beta.Id);
        string code;
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
        }
        var newPassword = $"N{Guid.NewGuid():N}!z";
        using var client = _app.CreateClient();
        var form = new HtmlParser().ParseDocument(await (await client.GetAsync($"/Identity/Account/ResetPassword?code={code}")).Content.ReadAsStringAsync());
        var fields = form.QuerySelectorAll("form[method=post] input").Where(i => !string.IsNullOrEmpty(i.GetAttribute("name")))
            .ToDictionary(i => i.GetAttribute("name")!, i => i.GetAttribute("value") ?? "");
        fields["Input.Email"] = email;
        fields["Input.Password"] = newPassword;
        fields["Input.ConfirmPassword"] = newPassword;

        using var reset = await client.PostAsync("/Identity/Account/ResetPassword", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.EndsWith("ResetPasswordConfirmation", reset.Headers.Location!.OriginalString);
        using var signIn = _app.CreateClient();
        Assert.Equal("/VolunteerAttendance", await SignIn.AsAsync(signIn, email, newPassword));
    }
}
