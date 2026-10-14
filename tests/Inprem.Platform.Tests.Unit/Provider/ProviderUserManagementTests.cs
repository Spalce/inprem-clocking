using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Provider;
using Xunit;
using static Inprem.Platform.Tests.Unit.Provider.ProviderTestRig;

namespace Inprem.Platform.Tests.Unit.Provider;

/// <summary>Phase 1 step 1.4: inviting, changing roles, disabling and resending setup links.</summary>
public class ProviderUserManagementTests
{
    private const string Base = "https://admin.inprem.test";

    [Fact]
    public async Task Invite_Creates_An_Invited_User_With_A_24_Hour_Link()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner("owner@inprem.test");

        var result = await rig.Service.InviteProviderUserAsync(owner.Id, "billing@inprem.test", ProviderRole.Billing, Base);

        Assert.True(result.Succeeded, result.Message);
        var account = rig.Accounts.ByEmail("billing@inprem.test");
        Assert.Null(account.Password);
        var user = rig.Store.Users.Single(u => u.UserId == account.Id);
        Assert.Equal((ProviderRole.Billing, ProviderUserStatus.Invited), (user.Role, user.Status));
        Assert.Equal(TimeSpan.FromHours(24), rig.Store.Invitations.Single().ExpiresUtc - rig.Store.Invitations.Single().CreatedUtc);
        Assert.Contains(rig.Store.Audit, a => a.Action == "provider.user.invited");
        Assert.Single(rig.Email.To(Mailbox));
        Assert.NotNull(rig.TokenSentTo("billing@inprem.test"));
    }

    [Fact]
    public async Task Invite_Refusals()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner("owner@inprem.test");
        var support = rig.AddOwner("support@inprem.test", ProviderRole.Support);
        rig.Accounts.Add("admin@alpha.test");

        Assert.Equal("refused.not-owner", (await rig.Service.InviteProviderUserAsync(support.Id, "x@inprem.test", ProviderRole.Support, Base)).Code);
        Assert.Equal("refused.invalid-email", (await rig.Service.InviteProviderUserAsync(owner.Id, "nope", ProviderRole.Support, Base)).Code);
        Assert.Equal("refused.already-provider", (await rig.Service.InviteProviderUserAsync(owner.Id, "support@inprem.test", ProviderRole.Owner, Base)).Code);
        Assert.Equal("refused.account-exists", (await rig.Service.InviteProviderUserAsync(owner.Id, "admin@alpha.test", ProviderRole.Support, Base)).Code);
        Assert.Empty(rig.Store.Invitations);

        var noBase = new ProviderTestRig(o => o.AdminBaseUrl = null);
        var noBaseOwner = noBase.AddOwner("o@inprem.test");
        Assert.Equal("refused.no-link-base", (await noBase.Service.InviteProviderUserAsync(noBaseOwner.Id, "new@inprem.test", ProviderRole.Support, null)).Code);
    }

    [Fact]
    public async Task Role_Changes_Protect_The_Last_Owner_And_Yourself()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner("owner@inprem.test");
        var support = rig.AddOwner("support@inprem.test", ProviderRole.Support);

        Assert.Equal("refused.self", (await rig.Service.ChangeProviderRoleAsync(owner.Id, owner.Id, ProviderRole.Support)).Code);
        Assert.Equal("refused.no-change", (await rig.Service.ChangeProviderRoleAsync(owner.Id, support.Id, ProviderRole.Support)).Code);
        Assert.Equal("refused.not-owner", (await rig.Service.ChangeProviderRoleAsync(support.Id, owner.Id, ProviderRole.Support)).Code);

        Assert.True((await rig.Service.ChangeProviderRoleAsync(owner.Id, support.Id, ProviderRole.Owner)).Succeeded);
        Assert.True(rig.Store.Users.Single(u => u.UserId == support.Id).IsActiveOwner);

        // The new owner steps the first one down; the first, now Billing, can no longer manage roles.
        Assert.True((await rig.Service.ChangeProviderRoleAsync(support.Id, owner.Id, ProviderRole.Billing)).Succeeded);
        Assert.Equal("refused.not-owner", (await rig.Service.ChangeProviderRoleAsync(owner.Id, support.Id, ProviderRole.Support)).Code);
        Assert.Equal(2, rig.Store.Audit.Count(a => a.Action == "provider.user.role-changed"));
    }

    [Fact]
    public async Task Only_Active_Owners_Manage_And_Owners_Never_Run_Out()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner("owner@inprem.test");
        var pending = rig.Accounts.Add("pending@inprem.test", password: null, twoFactor: false);
        rig.Store.Users.Add(ProviderUser.Invite(pending.Id, ProviderRole.Owner, rig.Clock.UtcNow));
        var billing = rig.AddOwner("billing@inprem.test", ProviderRole.Billing);

        // Invited (not yet set up) owners and non-owners cannot manage anyone.
        Assert.Equal("refused.not-owner", (await rig.Service.DisableProviderUserAsync(pending.Id, billing.Id, "x")).Code);
        Assert.Equal("refused.not-owner", (await rig.Service.ChangeProviderRoleAsync(billing.Id, billing.Id, ProviderRole.Owner)).Code);

        // The acting owner can never act on themselves, so at least one active owner always remains.
        Assert.Equal("refused.self", (await rig.Service.ChangeProviderRoleAsync(owner.Id, owner.Id, ProviderRole.Support)).Code);
        Assert.Equal("refused.self", (await rig.Service.DisableProviderUserAsync(owner.Id, owner.Id, "x")).Code);
        Assert.True((await rig.Service.DisableProviderUserAsync(owner.Id, billing.Id, "Left")).Succeeded);
        Assert.Single(rig.Store.Users, u => u.IsActiveOwner);
    }

    [Fact]
    public async Task Disable_Ends_Access_And_Is_Announced()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner("owner@inprem.test");
        var other = rig.AddOwner("other@inprem.test");
        var support = rig.AddOwner("support@inprem.test", ProviderRole.Support);

        Assert.Equal("refused.self", (await rig.Service.DisableProviderUserAsync(owner.Id, owner.Id, "x")).Code);
        Assert.Equal("refused.no-reason", (await rig.Service.DisableProviderUserAsync(owner.Id, support.Id, " ")).Code);

        var result = await rig.Service.DisableProviderUserAsync(owner.Id, support.Id, "Contract ended");
        Assert.True(result.Succeeded, result.Message);
        Assert.True(support.Disabled);
        Assert.Equal(1, support.Stamp);
        Assert.Equal(ProviderUserStatus.Disabled, rig.Store.Users.Single(u => u.UserId == support.Id).Status);
        Assert.Contains(rig.Store.Audit, a => a.Action == "provider.user.disabled" && a.Details!.Contains("Contract ended"));
        Assert.Contains(rig.Email.To("other@inprem.test"), e => e.Subject.Contains("disabled"));
        Assert.Equal("refused.no-target", (await rig.Service.DisableProviderUserAsync(owner.Id, support.Id, "again")).Code);

        // Owners can disable each other (the one acting always remains).
        Assert.True((await rig.Service.DisableProviderUserAsync(owner.Id, other.Id, "Left")).Succeeded);
    }

    [Fact]
    public async Task Resend_Replaces_The_Setup_Link_Only_For_Invited_Users()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner("owner@inprem.test");
        await rig.Service.InviteProviderUserAsync(owner.Id, "late@inprem.test", ProviderRole.Support, Base);
        var first = rig.TokenSentTo("late@inprem.test");
        var lateId = rig.Accounts.ByEmail("late@inprem.test").Id;

        rig.Clock.Advance(TimeSpan.FromHours(30)); // the first link expired
        Assert.True((await rig.Service.ResendProviderInvitationAsync(owner.Id, lateId, Base)).Succeeded);
        var second = rig.TokenSentTo("late@inprem.test");
        Assert.NotEqual(first, second);
        Assert.Null(await rig.Service.FindInvitationAsync(first));
        Assert.NotNull(await rig.Service.FindInvitationAsync(second));

        Assert.Equal("refused.not-invited", (await rig.Service.ResendProviderInvitationAsync(owner.Id, owner.Id, Base)).Code);
        Assert.Contains(rig.Store.Audit, a => a.Action == "provider.user.invitation-resent");
    }
}
