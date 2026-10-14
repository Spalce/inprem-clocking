using System.Net;
using Inprem.Platform.Domain.Provider;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>Phase 1 step 1.4: provider users (invite, role, disable, resend) and the Level 2 peer reset screen.</summary>
public class ProviderUserPagesTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public ProviderUserPagesTests(ProviderFixture app) => _app = app;

    private async Task<(HttpClient Client, string Key, string UserId, string Email)> OwnerAsync()
    {
        var email = $"own-{Guid.NewGuid():N}"[..14] + "@team.test";
        var key = await CreateEnrolledProviderUserAsync(_app.Services, _app.CreatePlatformDbContext, email, ProviderRole.Owner);
        var client = _app.CreateClient();
        await SignInAsync(client, email, key);
        return (client, key, (await _app.FindUserAsync(email)).Id, email);
    }

    private async Task<List<string>> AuditAsync(string targetUserId)
    {
        await using var db = _app.CreatePlatformDbContext();
        return await db.AuditEntries.Where(a => a.Target == targetUserId).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync();
    }

    [Fact]
    public async Task Team_Invite_Role_Reset_And_Disable_Through_The_Pages()
    {
        var (owner, key, ownerId, ownerEmail) = await OwnerAsync();
        var newEmail = $"new-{Guid.NewGuid():N}"[..14] + "@team.test";

        // Invite (sign-in two-factor is still fresh): the new owner gets a setup link and completes it.
        var invited = await PostAsync(owner, "/Provider/Users/Invite", ("Email", newEmail), ("Role", "Owner"));
        Assert.Equal("/Provider/Users", Location(invited));
        var (newClient, _, _) = await _app.CompleteInvitationAsync(newEmail);
        var newId = (await _app.FindUserAsync(newEmail)).Id;
        await GetDocumentAsync(newClient, "/Provider");

        // An organization admin's email cannot become a provider account.
        Assert.Contains("Provider accounts are separate", await BodyAsync(await PostAsync(owner, "/Provider/Users/Invite", ("Email", _app.Data.Alpha.Owner!.Email), ("Role", "Support"))));

        // Yourself: no actions offered.
        var self = await GetDocumentAsync(owner, $"/Provider/Users/Details/{ownerId}");
        Assert.Null(self.QuerySelector("[data-testid=disable]"));
        Assert.Null(self.QuerySelector("[data-testid=role]"));

        // Changing a role after more than 5 minutes asks for a code first, then returns.
        _app.Clock.Advance(TimeSpan.FromMinutes(6));
        var rolePage = $"/Provider/Users/Role/{newId}";
        var asked = await PostAsync(owner, rolePage, ("Role", "Billing"));
        Assert.Contains("/Provider/Reauthenticate", Location(asked));
        await PostAsync(owner, Location(asked), ("Code", Totp.Code(key)));
        Assert.Equal($"/Provider/Users/Details/{newId}", Location(await PostAsync(owner, rolePage, ("Role", "Billing"))));

        // Level 2: reset the other person's two-factor with a reason and your own code.
        var resetPage = $"/Provider/Users/ResetTwoFactor/{newId}";
        Assert.Contains("not valid", await BodyAsync(await PostAsync(owner, resetPage, ("Reason", "Lost phone"), ("Code", "000000"))));
        Assert.Equal($"/Provider/Users/Details/{newId}", Location(await PostAsync(owner, resetPage, ("Reason", "Lost phone and codes"), ("Code", Totp.Code(key)))));
        Assert.EndsWith("/Provider/Login", Location(await newClient.GetAsync("/Provider"))); // signed out at once

        // Disable: a reason is required; then the account cannot sign in.
        var disablePage = $"/Provider/Users/Disable/{newId}";
        Assert.Contains("Give a reason", await BodyAsync(await PostAsync(owner, disablePage, ("Reason", " "))));
        Assert.Equal($"/Provider/Users/Details/{newId}", Location(await PostAsync(owner, disablePage, ("Reason", "Left the team"))));
        using (var retry = _app.CreateClient())
        {
            Assert.Contains("Invalid sign-in attempt.", await BodyAsync(await PostAsync(retry, "/Provider/Login", ("Email", newEmail), ("Password", StrongPassword))));
        }

        Assert.Equal(new[] { "provider.user.invited", "provider.invitation.accepted", "provider.two-factor.enrolled", "provider.user.role-changed", "provider.recovery.level2", "provider.user.disabled" },
            (await AuditAsync(newId)).Where(a => !a.EndsWith(".refused")));
        Assert.Contains(_app.Emails.To(Mailbox), e => e.Subject.Contains("Provider user disabled"));
        Assert.Contains(_app.Emails.To(Mailbox), e => e.Subject.Contains("Level 2"));
    }

    [Fact]
    public async Task Team_Second_Owner_Removes_The_Go_Live_Warning()
    {
        // The fixture database may already hold owners from other tests; count from what the page says.
        var (owner, _, _, _) = await OwnerAsync();
        var dashboard = await GetDocumentAsync(owner, "/Provider");
        if (dashboard.QuerySelector("[data-testid=owners-warning]") != null)
        {
            var secondEmail = $"two-{Guid.NewGuid():N}"[..14] + "@team.test";
            await PostAsync(owner, "/Provider/Users/Invite", ("Email", secondEmail), ("Role", "Owner"));
            await _app.CompleteInvitationAsync(secondEmail);
        }
        Assert.Null((await GetDocumentAsync(owner, "/Provider")).QuerySelector("[data-testid=owners-warning]"));
        Assert.Equal("Healthy", await (await owner.GetAsync("/health")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Team_Resend_Gives_A_New_Setup_Link()
    {
        var (owner, _, _, _) = await OwnerAsync();
        var email = $"late-{Guid.NewGuid():N}"[..15] + "@team.test";
        await PostAsync(owner, "/Provider/Users/Invite", ("Email", email), ("Role", "Support"));
        var first = LinkSentTo(_app.Emails, email);
        var userId = (await _app.FindUserAsync(email)).Id;

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(owner, $"/Provider/Users/Details/{userId}", a => a.Contains("handler=Resend"))).StatusCode);
        var second = LinkSentTo(_app.Emails, email);
        Assert.NotEqual(first, second);
        using var visitor = _app.CreateClient();
        Assert.NotNull((await GetDocumentAsync(visitor, first)).QuerySelector("[data-testid=invalid]"));
        Assert.Null((await GetDocumentAsync(visitor, second)).QuerySelector("[data-testid=invalid]"));
        Assert.Contains("provider.user.invitation-resent", await AuditAsync(userId));
    }
}
