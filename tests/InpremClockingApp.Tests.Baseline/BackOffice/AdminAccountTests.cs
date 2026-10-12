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

    [Fact]
    [Trait(Traits.ChangedBy, "D2 / C3-6 (Phase 0 step 0.4)")]
    public async Task Admin_Same_Email_In_Two_Organizations_Signs_In_By_Password()
    {
        // Today an admin email is unique per organization only: Alpha can add an admin with the
        // same email as one of Beta's, and the login page tells them apart by password.
        var email = $"shared-{Guid.NewGuid():N}"[..20] + "@shared.test";
        var alphaPassword = $"A{Guid.NewGuid():N}!x";
        var betaPassword = $"B{Guid.NewGuid():N}!y";

        using (var alpha = await _app.AdminOfAsync(_app.Data.Alpha))
        using (var r = await alpha.PostFormAsync("/User", Form(email, alphaPassword)))
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        using (var beta = await _app.AdminOfAsync(_app.Data.Beta))
        using (var r = await beta.PostFormAsync("/User", Form(email, betaPassword)))
            Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        using var asAlpha = _app.CreateClient();
        await SignIn.AsAsync(asAlpha, email, alphaPassword);
        Assert.Contains("staff1@alpha.test", (await asAlpha.GetPageAsync("/Staff")).Body!.CleanText());

        using var asBeta = _app.CreateClient();
        await SignIn.AsAsync(asBeta, email, betaPassword);
        Assert.Contains("staff1@beta.test", (await asBeta.GetPageAsync("/Staff")).Body!.CleanText());
    }
}
