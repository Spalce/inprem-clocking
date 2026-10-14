using System.Net;
using AngleSharp.Html.Parser;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Memberships;
using InpremClockingApp.Helpers;
using InpremClockingApp.Models.Identity;
using InpremClockingApp.Tests.Support.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inprem.Platform.Tests.Integration.Memberships;

/// <summary>
/// Step 0.3: the organization comes from the signed-in membership, re-checked on every request.
/// Single-membership admins are covered by the whole behaviour baseline; these tests cover the
/// rest: several memberships (chooser), revoked memberships, legacy accounts without a
/// membership, platform operators, and accounts without any organization.
/// </summary>
public class MembershipSignInTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _app;

    public MembershipSignInTests(AppFixture app) => _app = app;

    private const string Login = "https://localhost/Identity/Account/Login";

    private async Task<(string UserId, string Email)> CreateAccountAsync(int? tenantId, string? role = IdentitySeeder.AdminRole)
    {
        var email = $"m-{Guid.NewGuid():N}"[..18] + "@members.test";
        using var scope = _app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, TenantId = tenantId, Type = role };
        Assert.True((await users.CreateAsync(user, _app.Data.Password)).Succeeded);
        if (role != null) Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return (user.Id, email);
    }

    private async Task<Guid> AddMembershipAsync(string userId, int tenantId, MembershipStatus status = MembershipStatus.Active)
    {
        var membership = TenantMembership.CreateActive(userId, tenantId, MembershipRole.Admin, DateTime.UtcNow);
        if (status == MembershipStatus.Revoked) membership.Revoke();
        await using var db = _app.CreatePlatformDbContext();
        db.TenantMemberships.Add(membership);
        await db.SaveChangesAsync();
        return membership.Id;
    }

    private async Task RevokeAsync(Guid membershipId, bool invalidateCache)
    {
        await using (var db = _app.CreatePlatformDbContext())
        {
            var membership = await db.TenantMemberships.SingleAsync(m => m.Id == membershipId);
            membership.Revoke();
            await db.SaveChangesAsync();
        }
        if (invalidateCache)
        {
            using var scope = _app.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IMembershipService>().Invalidate(membershipId);
        }
    }

    [Fact]
    public async Task Membership_Several_Memberships_Show_The_Chooser_Then_Work_In_The_Chosen_Organization()
    {
        var (userId, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        await AddMembershipAsync(userId, _app.Data.Alpha.Id);
        var betaMembership = await AddMembershipAsync(userId, _app.Data.Beta.Id);
        using var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);

        using var backOffice = await client.GetAsync("/Staff");
        Assert.Equal(HttpStatusCode.Redirect, backOffice.StatusCode);
        Assert.Equal("/Account/ChooseOrganization?returnUrl=%2FStaff", backOffice.Headers.Location!.OriginalString);

        using var api = await client.PostAsync("/api/reports/staff", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);

        var chooser = new HtmlParser().ParseDocument(await (await client.GetAsync("/Account/ChooseOrganization?returnUrl=%2FStaff")).Content.ReadAsStringAsync());
        var options = chooser.QuerySelectorAll("[data-testid=organizations] button").Select(b => b.QuerySelector("span")!.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Alpha", "Beta" }, options);

        using var choose = await ChooseAsync(client, betaMembership, "/Staff");
        Assert.Equal(HttpStatusCode.Redirect, choose.StatusCode);
        Assert.Equal("/Staff", choose.Headers.Location!.OriginalString);

        var staff = await (await client.GetAsync("/Staff")).Content.ReadAsStringAsync();
        Assert.Contains("staff1@beta.test", staff);
        Assert.DoesNotContain("staff1@alpha.test", staff);
    }

    [Fact]
    public async Task Membership_Chooser_Ignores_A_Membership_That_Is_Not_The_Users()
    {
        var (userId, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        await AddMembershipAsync(userId, _app.Data.Alpha.Id);
        await AddMembershipAsync(userId, _app.Data.Gamma.Id);
        var (otherUserId, _) = await CreateAccountAsync(_app.Data.Beta.Id);
        var someoneElses = await AddMembershipAsync(otherUserId, _app.Data.Beta.Id);
        using var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);

        using var choose = await ChooseAsync(client, someoneElses);

        Assert.Equal(HttpStatusCode.Redirect, choose.StatusCode);
        Assert.StartsWith("/Account/ChooseOrganization", choose.Headers.Location!.OriginalString);
        using var staff = await client.GetAsync("/Staff");
        Assert.StartsWith("/Account/ChooseOrganization", staff.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Membership_Chooser_Rejects_A_Post_Without_The_Antiforgery_Token()
    {
        var (userId, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        await AddMembershipAsync(userId, _app.Data.Alpha.Id);
        var beta = await AddMembershipAsync(userId, _app.Data.Beta.Id);
        using var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);

        using var forged = await client.PostAsync("/Account/ChooseOrganization",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["membershipId"] = beta.ToString() }));

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }

    [Fact]
    public async Task Membership_Chooser_With_One_Membership_Just_Continues()
    {
        using var client = await NewAdminSessionAsync();

        using var response = await client.GetAsync("/Account/ChooseOrganization?returnUrl=%2FStaff");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Staff", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Membership_Revoked_Signs_Out_On_The_Next_Request()
    {
        var (userId, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        var membership = await AddMembershipAsync(userId, _app.Data.Alpha.Id);
        using var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Staff")).StatusCode);

        await RevokeAsync(membership, invalidateCache: true);

        using var next = await client.GetAsync("/Staff");
        Assert.Equal(HttpStatusCode.Redirect, next.StatusCode);
        Assert.StartsWith(Login, next.Headers.Location!.ToString());
        Assert.Contains(next.Headers.GetValues("Set-Cookie"), c => c.StartsWith(".Inprem.App=;", StringComparison.Ordinal));
        using var after = await client.GetAsync("/StaffAttendance");
        Assert.StartsWith(Login, after.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Membership_Revocation_Without_Cache_Invalidation_Takes_Effect_Within_Five_Minutes()
    {
        // Records the cache rule: a change that does not invalidate the cache is seen once the
        // 5-minute entry expires. Every code path that changes a membership must call Invalidate.
        var (userId, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        var membership = await AddMembershipAsync(userId, _app.Data.Alpha.Id);
        using var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Staff")).StatusCode);

        await RevokeAsync(membership, invalidateCache: false);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Staff")).StatusCode);
        Assert.Equal(TimeSpan.FromMinutes(5), MembershipService.CacheDuration);
    }

    [Fact]
    public async Task Membership_Revoked_Account_Cannot_Work_After_Signing_In_Again()
    {
        var (userId, email) = await CreateAccountAsync(_app.Data.Alpha.Id);
        await AddMembershipAsync(userId, _app.Data.Alpha.Id, MembershipStatus.Revoked);
        using var client = _app.CreateClient();

        await SignIn.AsAsync(client, email, _app.Data.Password);

        using var next = await client.GetAsync("/Staff");
        Assert.StartsWith(Login, next.Headers.Location!.ToString());
        await using var db = _app.CreatePlatformDbContext();
        // The revoked membership is not replaced by a new Active one.
        Assert.Equal(MembershipStatus.Revoked, (await db.TenantMemberships.SingleAsync(m => m.UserId == userId)).Status);
    }

    [Fact]
    public async Task Membership_Legacy_Account_Without_Membership_Gets_One_At_Sign_In()
    {
        // e.g. an admin created on the Admins page before step 0.4, or by the old Platform pages.
        var (userId, email) = await CreateAccountAsync(_app.Data.Beta.Id);
        using var client = _app.CreateClient();

        await SignIn.AsAsync(client, email, _app.Data.Password);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Staff")).StatusCode);
        await using var db = _app.CreatePlatformDbContext();
        var membership = await db.TenantMemberships.SingleAsync(m => m.UserId == userId);
        Assert.Equal((_app.Data.Beta.Id, MembershipRole.Admin, MembershipStatus.Active), (membership.TenantId, membership.Role, membership.Status));
    }

    [Fact]
    public async Task Membership_Platform_Operator_Is_Pointed_To_The_Provider_Console()
    {
        // Phase 1 step 1.7: the old /Platform pages are gone; such an account is refused on the App site.
        var (_, email) = await CreateAccountAsync(tenantId: null, role: IdentitySeeder.SuperAdminRole);
        using var client = _app.CreateClient();
        using var response = await SignIn.PostLoginAsync(client, email, _app.Data.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Provider Console", await response.Content.ReadAsStringAsync());
        Assert.Contains("/Identity/Account/Login", response.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Platform/Tenants")).StatusCode);
    }

    [Fact]
    public async Task Membership_Account_Without_Any_Organization_Is_Signed_Out()
    {
        // Not a platform operator and no organization: nothing to work in. (Today's data has no
        // such account; before step 0.3 it could have used the kiosk pages.)
        var (_, email) = await CreateAccountAsync(tenantId: null);
        using var client = _app.CreateClient();
        await SignIn.AsAsync(client, email, _app.Data.Password);

        using var response = await client.GetAsync("/StaffAttendance");

        Assert.StartsWith(Login, response.Headers.Location!.ToString());
    }

    /// <summary>Submits the chooser the way a browser does: the form's own hidden fields, with the given membership.</summary>
    private static async Task<HttpResponseMessage> ChooseAsync(HttpClient client, Guid membershipId, string? returnUrl = null)
    {
        var path = "/Account/ChooseOrganization" + (returnUrl == null ? "" : "?returnUrl=" + Uri.EscapeDataString(returnUrl));
        var page = new HtmlParser().ParseDocument(await (await client.GetAsync(path)).Content.ReadAsStringAsync());
        var form = page.QuerySelectorAll("[data-testid=organizations] form").First();
        var fields = form.QuerySelectorAll("input[type=hidden]")
            .Select(i => new KeyValuePair<string, string>(i.GetAttribute("name")!, i.GetAttribute("value") ?? ""))
            .Where(f => f.Key != "membershipId")
            .Append(new KeyValuePair<string, string>("membershipId", membershipId.ToString()))
            .ToList();
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private async Task<HttpClient> NewAdminSessionAsync()
    {
        var client = _app.CreateClient();
        await SignIn.AsAsync(client, _app.Data.Alpha.Admin!.Email, _app.Data.Password);
        return client;
    }
}
