using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Provider;
using Xunit;
using static Inprem.Platform.Tests.Unit.Provider.ProviderTestRig;

namespace Inprem.Platform.Tests.Unit.Provider;

/// <summary>Phase 0 step 0.7: bootstrap and recovery use cases, every refusal path (design section 5).</summary>
public class ProviderSecurityServiceTests
{
    private const string Owner = "owner@inprem.test";
    private const string Operator = "operator:test@server";

    private static BootstrapRequest Boot(string email = Owner, string code = BootstrapCode, bool dryRun = false) => new(email, code, Operator, dryRun);

    private static BreakGlassRequest Reissue(string target = Owner, string code = BreakGlassCode, string approver = Approver1, bool dryRun = false) =>
        new(target, code, new[] { approver }, Operator, dryRun);

    private static BreakGlassRequest Replace(string newEmail = "new.owner@inprem.test", string code = BreakGlassCode, bool dryRun = false) =>
        new(Owner, code, new[] { Approver1, Approver2 }, Operator, dryRun, Replace: true, NewEmail: newEmail, ApprovalReference: "Signed letter 2026-10-13");

    // ------------------------------------------------------------ bootstrap

    [Fact]
    public async Task Bootstrap_Creates_Owner_Without_Password_And_Sends_A_24_Hour_Link()
    {
        var rig = new ProviderTestRig();
        var result = await rig.Service.BootstrapAsync(Boot());

        Assert.True(result.Succeeded, result.Message);
        var account = rig.Accounts.ByEmail(Owner);
        Assert.Null(account.Password);
        var user = Assert.Single(rig.Store.Users);
        Assert.Equal((account.Id, ProviderRole.Owner, ProviderUserStatus.Invited), (user.UserId, user.Role, user.Status));

        var invitation = Assert.Single(rig.Store.Invitations);
        Assert.Equal(TimeSpan.FromHours(24), invitation.ExpiresUtc - invitation.CreatedUtc);
        var token = rig.TokenSentTo(Owner);
        Assert.Equal(SecretTokens.Hash(token), invitation.TokenHash);
        Assert.Contains("https://admin.inprem.test/AcceptInvitation?token=", rig.Email.To(Owner).Single().Body);

        // Only hashes are kept; the message never contains the code or the link.
        Assert.Equal(new[] { SecretTokens.Hash(BootstrapCode) }, rig.Store.UsedCodes.Keys);
        Assert.DoesNotContain(BootstrapCode, result.Message);
        Assert.DoesNotContain(token, result.Message);
        Assert.Contains(rig.Store.Audit, a => a.Action == "provider.bootstrap.succeeded");
        Assert.All(rig.Store.Audit, a => Assert.DoesNotContain(BootstrapCode, a.Details ?? ""));
        Assert.Single(rig.Email.To(Mailbox));
    }

    [Theory]
    [InlineData("WRONG-CODE", "refused.wrong-code")]
    [InlineData("", "refused.wrong-code")]
    public async Task Bootstrap_Refuses_A_Wrong_Code_And_Counts_It(string code, string expected)
    {
        var rig = new ProviderTestRig();
        var result = await rig.Service.BootstrapAsync(Boot(code: code));

        Assert.Equal(expected, result.Code);
        Assert.Empty(rig.Accounts.All);
        Assert.Single(rig.Store.Attempts, a => !a.Succeeded);
        Assert.Contains(rig.Store.Audit, a => a.Action == "provider.bootstrap.refused");
    }

    [Fact]
    public async Task Bootstrap_Refuses_When_Not_Configured()
    {
        var rig = new ProviderTestRig(o => o.BootstrapCodeHash = null);
        Assert.Equal("refused.not-configured", (await rig.Service.BootstrapAsync(Boot())).Code);
        Assert.False(await rig.Service.IsSetupAvailableAsync());
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData("Name <owner@inprem.test>")]
    public async Task Bootstrap_Refuses_An_Invalid_Email(string email)
    {
        var rig = new ProviderTestRig();
        Assert.Equal("refused.invalid-email", (await rig.Service.BootstrapAsync(Boot(email))).Code);
        Assert.Empty(rig.Store.Attempts); // not counted as a failed code
    }

    [Fact]
    public async Task Bootstrap_Refuses_Once_An_Owner_Exists_And_The_Setup_Page_Closes()
    {
        var rig = new ProviderTestRig();
        Assert.True(await rig.Service.IsSetupAvailableAsync());
        await rig.Service.BootstrapAsync(Boot());

        Assert.False(await rig.Service.IsSetupAvailableAsync());
        Assert.Equal("refused.owner-exists", (await rig.Service.BootstrapAsync(Boot("second@inprem.test"))).Code);
        Assert.Single(rig.Store.Users);
    }

    [Fact]
    public async Task Bootstrap_Code_Works_Once_Even_After_The_Link_Expired()
    {
        var rig = new ProviderTestRig();
        await rig.Service.BootstrapAsync(Boot());
        rig.Clock.Advance(TimeSpan.FromHours(25));

        // Setup reopens (the invited owner never acted), but the used code is refused.
        Assert.True(await rig.Service.IsSetupAvailableAsync());
        Assert.Equal("refused.code-used", (await rig.Service.BootstrapAsync(Boot())).Code);
    }

    [Fact]
    public async Task Bootstrap_With_A_New_Code_Reinvites_The_Same_Unused_Owner()
    {
        var rig = new ProviderTestRig();
        await rig.Service.BootstrapAsync(Boot());
        rig.Clock.Advance(TimeSpan.FromHours(25));
        rig.Options.BootstrapCodeHash = SecretTokens.Hash("NEW-CODE");

        var result = await rig.Service.BootstrapAsync(Boot(code: "NEW-CODE"));
        Assert.True(result.Succeeded, result.Message);
        Assert.Single(rig.Accounts.All);
        Assert.Single(rig.Store.Users);
        Assert.Equal(2, rig.Store.Invitations.Count);
    }

    [Fact]
    public async Task Bootstrap_Refuses_An_Email_That_Already_Has_An_Account()
    {
        var rig = new ProviderTestRig();
        rig.Accounts.Add(Owner); // e.g. a tenant admin
        Assert.Equal("refused.account-exists", (await rig.Service.BootstrapAsync(Boot())).Code);
        Assert.Empty(rig.Store.UsedCodes);
    }

    [Fact]
    public async Task Bootstrap_Refuses_Without_A_Link_Base()
    {
        var rig = new ProviderTestRig(o => o.AdminBaseUrl = null);
        Assert.Equal("refused.no-link-base", (await rig.Service.BootstrapAsync(Boot())).Code);

        // The setup page passes its own base.
        var result = await rig.Service.BootstrapAsync(Boot() with { LinkBase = "https://test.example/Provider" });
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("https://test.example/Provider/AcceptInvitation?token=", rig.Email.To(Owner).Single().Body);
    }

    [Fact]
    public async Task Bootstrap_Is_Rate_Limited_After_Five_Failures_An_Hour()
    {
        var rig = new ProviderTestRig();
        for (var i = 0; i < ProviderSecurityService.BootstrapFailuresPerHour; i++) await rig.Service.BootstrapAsync(Boot(code: "WRONG"));

        Assert.Equal("refused.rate-limited", (await rig.Service.BootstrapAsync(Boot())).Code);
        rig.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.True((await rig.Service.BootstrapAsync(Boot())).Succeeded);
    }

    [Fact]
    public async Task Bootstrap_Dry_Run_Changes_Nothing()
    {
        var rig = new ProviderTestRig();
        var before = rig.Store.Snapshot();

        var ok = await rig.Service.BootstrapAsync(Boot(dryRun: true));
        var refused = await rig.Service.BootstrapAsync(Boot(code: "WRONG", dryRun: true));

        Assert.True(ok.Succeeded);
        Assert.NotEmpty(ok.Plan);
        Assert.Equal("refused.wrong-code", refused.Code);
        Assert.Equal(before, rig.Store.Snapshot());
        Assert.Empty(rig.Accounts.All);
        Assert.Empty(rig.Email.Sent);
    }

    // ------------------------------------------------------------ invitations

    [Fact]
    public async Task Invitation_Sets_The_Password_Once_And_Activates_The_Owner()
    {
        var rig = new ProviderTestRig();
        await rig.Service.BootstrapAsync(Boot());
        var token = rig.TokenSentTo(Owner);

        Assert.Equal("refused.password", (await rig.Service.AcceptInvitationAsync(token, "short")).Result.Code);
        var (result, userId) = await rig.Service.AcceptInvitationAsync(token, "Long-Enough-1");
        Assert.True(result.Succeeded);
        Assert.Equal(rig.Accounts.ByEmail(Owner).Id, userId);
        Assert.Equal(ProviderUserStatus.Active, rig.Store.Users.Single().Status);
        Assert.True(rig.Accounts.ByEmail(Owner).EmailConfirmed);

        Assert.Equal("refused.invitation-invalid", (await rig.Service.AcceptInvitationAsync(token, "Another-Pass-2")).Result.Code);
        Assert.Null(await rig.Service.FindInvitationAsync(token));
    }

    [Fact]
    public async Task Invitation_Expires_After_24_Hours_And_Unknown_Tokens_Are_Refused()
    {
        var rig = new ProviderTestRig();
        await rig.Service.BootstrapAsync(Boot());
        var token = rig.TokenSentTo(Owner);
        rig.Clock.Advance(TimeSpan.FromHours(24));

        Assert.Equal("refused.invitation-invalid", (await rig.Service.AcceptInvitationAsync(token, "Long-Enough-1")).Result.Code);
        Assert.Equal("refused.invitation-invalid", (await rig.Service.AcceptInvitationAsync("made-up", "Long-Enough-1")).Result.Code);
        Assert.Null(rig.Accounts.ByEmail(Owner).Password);
    }

    // ------------------------------------------------------------ owners

    [Fact]
    public async Task Owner_Status_Counts_Only_Active_Owners_With_Two_Factor()
    {
        var rig = new ProviderTestRig();
        rig.AddOwner("a@inprem.test");
        rig.AddOwner("b@inprem.test").TwoFactor = false;
        rig.AddOwner("support@inprem.test", ProviderRole.Support);

        var status = await rig.Service.GetOwnerStatusAsync();
        Assert.Equal((2, 1, false), (status.ActiveOwners, status.EnrolledOwners, status.MeetsMinimum));

        rig.Accounts.ByEmail("b@inprem.test").TwoFactor = true;
        Assert.True((await rig.Service.GetOwnerStatusAsync()).MeetsMinimum);
    }

    // ------------------------------------------------------------ Level 1

    [Fact]
    public async Task Level1_Is_Audited_And_Announced()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner(Owner);
        rig.AddOwner("other@inprem.test");

        await rig.Service.RecordSelfServiceRecoveryAsync(owner.Id);

        Assert.Contains(rig.Store.Audit, a => a.Action == "provider.recovery.level1" && a.Target == owner.Id);
        Assert.Single(rig.Email.To(Owner));
        Assert.Single(rig.Email.To("other@inprem.test"));
        Assert.Single(rig.Email.To(Mailbox));
    }

    // ------------------------------------------------------------ Level 2

    [Fact]
    public async Task Level2_Resets_Another_Owners_Two_Factor_And_Notifies_Everyone()
    {
        var rig = new ProviderTestRig();
        var actor = rig.AddOwner("actor@inprem.test");
        var target = rig.AddOwner(Owner);

        var result = await rig.Service.PeerResetAsync(actor.Id, target.Id, "Lost phone and codes", actor.TotpCode);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(target.TwoFactor);
        Assert.Equal(1, target.Stamp); // every session signed out
        Assert.Equal(RecoveryLevel.PeerReset, rig.Store.Recoveries.Single().Level);
        Assert.Contains(rig.Store.Audit, a => a.Action == "provider.recovery.level2" && a.Details!.Contains("Lost phone"));
        Assert.Single(rig.Email.To("actor@inprem.test"));
        Assert.Single(rig.Email.To(Owner));
        Assert.Single(rig.Email.To(Mailbox));
    }

    [Fact]
    public async Task Level2_Refusals()
    {
        var rig = new ProviderTestRig();
        var actor = rig.AddOwner("actor@inprem.test");
        var target = rig.AddOwner(Owner);
        var support = rig.AddOwner("support@inprem.test", ProviderRole.Support);
        var tenantAdmin = rig.Accounts.Add("tenant.admin@alpha.test");

        Assert.Equal("refused.self", (await rig.Service.PeerResetAsync(actor.Id, actor.Id, "x", actor.TotpCode)).Code);
        Assert.Equal("refused.no-reason", (await rig.Service.PeerResetAsync(actor.Id, target.Id, " ", actor.TotpCode)).Code);
        Assert.Equal("refused.not-owner", (await rig.Service.PeerResetAsync(support.Id, target.Id, "x", support.TotpCode)).Code);
        Assert.Equal("refused.no-target", (await rig.Service.PeerResetAsync(actor.Id, tenantAdmin.Id, "x", actor.TotpCode)).Code);
        Assert.Equal("refused.reauthentication", (await rig.Service.PeerResetAsync(actor.Id, target.Id, "x", "000000")).Code);

        actor.TwoFactor = false;
        Assert.Equal("refused.not-owner", (await rig.Service.PeerResetAsync(actor.Id, target.Id, "x", actor.TotpCode)).Code);
        Assert.True(target.TwoFactor);
        Assert.Empty(rig.Store.Recoveries);
    }

    [Fact]
    public async Task Level2_At_Most_Once_Per_Account_Per_24_Hours()
    {
        var rig = new ProviderTestRig();
        var actor = rig.AddOwner("actor@inprem.test");
        var target = rig.AddOwner(Owner);
        Assert.True((await rig.Service.PeerResetAsync(actor.Id, target.Id, "first", actor.TotpCode)).Succeeded);

        target.TwoFactor = true;
        Assert.Equal("refused.rate-limited", (await rig.Service.PeerResetAsync(actor.Id, target.Id, "again", actor.TotpCode)).Code);
        rig.Clock.Advance(TimeSpan.FromHours(24.1));
        Assert.True((await rig.Service.PeerResetAsync(actor.Id, target.Id, "again", actor.TotpCode)).Succeeded);
    }

    // ------------------------------------------------------------ Level 3

    [Fact]
    public async Task Level3_Needs_The_Approvers_Link_Then_Resets_And_Reinvites_The_Same_Email()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner(Owner);

        var started = await rig.Service.StartBreakGlassAsync(Reissue());
        Assert.True(started.Succeeded, started.Message);
        Assert.DoesNotContain(BreakGlassCode, started.Message);
        Assert.Equal(new[] { SecretTokens.Hash(BreakGlassCode) }, rig.Store.UsedCodes.Keys);

        // Nothing happens to the owner before approval.
        Assert.Equal(("fake-account-pw", true, 0), (owner.Password, owner.TwoFactor, owner.Stamp));
        Assert.DoesNotContain(rig.Email.To(Owner), e => e.Subject.Contains("password"));
        Assert.Single(rig.Email.To(Mailbox));

        var approval = rig.TokenSentTo(Approver1);
        Assert.Equal(SecretTokens.Hash(approval), rig.Store.Recoveries.Single().FirstApprovalTokenHash);
        Assert.Equal("refused.approval-invalid", (await rig.Service.ApproveRecoveryAsync("forged", false)).Code);

        var approved = await rig.Service.ApproveRecoveryAsync(approval, waiveDelay: false);
        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal((null, false, 1), (owner.Password, owner.TwoFactor, owner.Stamp));
        Assert.Equal(RecoveryStatus.Completed, rig.Store.Recoveries.Single().Status);

        var invitation = rig.Store.Invitations.Single();
        Assert.Equal((Owner, InvitationPurpose.ProviderRecovery, TimeSpan.FromHours(1)), (invitation.Email, invitation.Purpose, invitation.ExpiresUtc - invitation.CreatedUtc));
        Assert.Equal(SecretTokens.Hash(rig.TokenSentTo(Owner)), invitation.TokenHash);
        Assert.Contains(rig.Store.Audit, a => a.Action == "provider.recovery.level3.completed");
        Assert.Equal(2, rig.Email.To(Mailbox).Count()); // started, completed
        Assert.Single(rig.Email.To(Approver1)); // the approval link reached only the approver
        Assert.Equal("refused.approval-closed", (await rig.Service.ApproveRecoveryAsync(approval, false)).Code);
    }

    [Fact]
    public async Task Level3_Approval_Expires_After_One_Hour()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner(Owner);
        await rig.Service.StartBreakGlassAsync(Reissue());
        rig.Clock.Advance(TimeSpan.FromMinutes(61));

        Assert.Equal("refused.approval-expired", (await rig.Service.ApproveRecoveryAsync(rig.TokenSentTo(Approver1), false)).Code);
        Assert.Equal(RecoveryStatus.Expired, rig.Store.Recoveries.Single().Status);
        Assert.Equal("fake-account-pw", owner.Password);
    }

    [Fact]
    public async Task Level3_Revokes_Pending_Links_Of_The_Account()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner(Owner);
        rig.Store.Invitations.Add(Invitation.Create(Owner, owner.Id, InvitationPurpose.ProviderSetup, SecretTokens.Hash("old"), TimeSpan.FromHours(24), "x", rig.Clock.UtcNow));

        await rig.Service.StartBreakGlassAsync(Reissue());
        await rig.Service.ApproveRecoveryAsync(rig.TokenSentTo(Approver1), false);

        Assert.NotNull(rig.Store.Invitations[0].RevokedUtc);
        Assert.Null(await rig.Service.FindInvitationAsync("old"));
    }

    [Theory]
    [InlineData("WRONG", Approver1, "refused.wrong-code")]
    [InlineData(BreakGlassCode, "stranger@inprem.test", "refused.unknown-approver")]
    public async Task Level3_Secret_Failures_Count_Towards_The_Lock(string code, string approver, string expected)
    {
        var rig = new ProviderTestRig();
        rig.AddOwner(Owner);
        Assert.Equal(expected, (await rig.Service.StartBreakGlassAsync(Reissue(code: code, approver: approver))).Code);
        Assert.Single(rig.Store.Attempts, a => a.Kind == ProviderSecurityService.BreakGlassAttempt && !a.Succeeded);
        Assert.Empty(rig.Store.Recoveries);
        Assert.Empty(rig.Email.To(Approver1));
    }

    [Fact]
    public async Task Level3_Locks_For_24_Hours_After_Three_Failures_And_Alerts_Owners()
    {
        var rig = new ProviderTestRig();
        rig.AddOwner(Owner);
        rig.AddOwner("other@inprem.test");
        for (var i = 0; i < ProviderSecurityService.BreakGlassFailuresBeforeLock; i++) await rig.Service.StartBreakGlassAsync(Reissue(code: "WRONG"));

        Assert.Contains(rig.Email.To("other@inprem.test"), e => e.Subject.Contains("locked"));
        Assert.Contains(rig.Email.To(Mailbox), e => e.Subject.Contains("locked"));
        Assert.Equal("refused.locked", (await rig.Service.StartBreakGlassAsync(Reissue())).Code);
        rig.Clock.Advance(TimeSpan.FromHours(24.1));
        Assert.True((await rig.Service.StartBreakGlassAsync(Reissue())).Succeeded);
    }

    [Fact]
    public async Task Level3_Refusals_Without_A_Secret()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner(Owner);
        rig.AddOwner("support@inprem.test", ProviderRole.Support);
        rig.Accounts.Add("tenant.admin@alpha.test");

        Assert.Equal("refused.no-target", (await rig.Service.StartBreakGlassAsync(Reissue(target: "nobody@inprem.test"))).Code);
        Assert.Equal("refused.no-target", (await rig.Service.StartBreakGlassAsync(Reissue(target: "tenant.admin@alpha.test"))).Code);
        Assert.Equal("refused.no-target", (await rig.Service.StartBreakGlassAsync(Reissue(target: "support@inprem.test"))).Code);
        Assert.Equal("refused.approvers", (await rig.Service.StartBreakGlassAsync(Reissue() with { Approvers = Array.Empty<string>() })).Code);
        Assert.Equal("refused.approvers", (await rig.Service.StartBreakGlassAsync(Reissue() with { Approvers = new[] { Approver1, Approver2 } })).Code);

        owner.EmailConfirmed = false;
        Assert.Equal("refused.email-not-verified", (await rig.Service.StartBreakGlassAsync(Reissue())).Code);
        Assert.Empty(rig.Store.Attempts); // none of these counts towards the lock
        Assert.Empty(rig.Store.UsedCodes);
    }

    [Fact]
    public async Task Level3_Approver_Cannot_Be_The_Owner_Being_Recovered()
    {
        var rig = new ProviderTestRig(o => o.RecoveryApprovers = new[] { Owner, Approver1 });
        rig.AddOwner(Owner);
        Assert.Equal("refused.approvers", (await rig.Service.StartBreakGlassAsync(Reissue(approver: Owner))).Code);
    }

    [Fact]
    public async Task Level3_Not_Configured_And_Code_Used()
    {
        var rig = new ProviderTestRig(o => o.BreakGlassCodeHash = null);
        rig.AddOwner(Owner);
        Assert.Equal("refused.not-configured", (await rig.Service.StartBreakGlassAsync(Reissue())).Code);

        var rig2 = new ProviderTestRig();
        rig2.AddOwner(Owner);
        rig2.AddOwner("second@inprem.test");
        await rig2.Service.StartBreakGlassAsync(Reissue());
        Assert.Equal("refused.code-used", (await rig2.Service.StartBreakGlassAsync(Reissue(target: "second@inprem.test"))).Code);
    }

    [Fact]
    public async Task Level3_At_Most_Once_Per_Account_Per_24_Hours()
    {
        var rig = new ProviderTestRig();
        rig.AddOwner(Owner);
        await rig.Service.StartBreakGlassAsync(Reissue());
        rig.Options.BreakGlassCodeHash = SecretTokens.Hash("ROTATED");

        Assert.Equal("refused.rate-limited", (await rig.Service.StartBreakGlassAsync(Reissue(code: "ROTATED"))).Code);
    }

    [Fact]
    public async Task Level3_Dry_Run_Changes_Nothing()
    {
        var rig = new ProviderTestRig();
        var owner = rig.AddOwner(Owner);
        var before = rig.Store.Snapshot();

        var result = await rig.Service.StartBreakGlassAsync(Reissue(dryRun: true));
        await rig.Service.StartBreakGlassAsync(Reissue(code: "WRONG", dryRun: true));

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.Plan);
        Assert.Equal(before, rig.Store.Snapshot());
        Assert.Empty(rig.Email.Sent);
        Assert.Equal(("fake-account-pw", true, 0), (owner.Password, owner.TwoFactor, owner.Stamp));
    }

    // ------------------------------------------------------------ Level 4

    [Fact]
    public async Task Level4_Both_Approvers_Waive_The_Delay_Then_The_Old_Owner_Is_Disabled_And_The_New_One_Invited()
    {
        var rig = new ProviderTestRig();
        var old = rig.AddOwner(Owner);
        old.EmailConfirmed = false; // Level 4 also works when the old mailbox is lost or unverified

        var started = await rig.Service.StartBreakGlassAsync(Replace());
        Assert.True(started.Succeeded, started.Message);

        var first = await rig.Service.ApproveRecoveryAsync(rig.TokenSentTo(Approver1), waiveDelay: true);
        Assert.Equal("recovery.approved-partly", first.Code);
        Assert.False(old.Disabled);

        var second = await rig.Service.ApproveRecoveryAsync(rig.TokenSentTo(Approver2), waiveDelay: true);
        Assert.Equal("recovery.completed", second.Code);
        Assert.True(old.Disabled);
        Assert.Equal(1, old.Stamp);
        Assert.Equal(ProviderUserStatus.Disabled, rig.Store.Users.Single(u => u.UserId == old.Id).Status);

        var created = rig.Accounts.ByEmail("new.owner@inprem.test");
        Assert.Null(created.Password);
        Assert.Contains(rig.Store.Users, u => u.UserId == created.Id && u.Role == ProviderRole.Owner && u.Status == ProviderUserStatus.Invited);
        Assert.Single(rig.Email.To("new.owner@inprem.test"));
        Assert.Contains(rig.Store.Audit, a => a.Action == "provider.recovery.level4.started" && a.Details!.Contains("Signed letter"));
    }

    [Fact]
    public async Task Level4_Keeps_The_24_Hour_Delay_Unless_Both_Waive()
    {
        var rig = new ProviderTestRig();
        var old = rig.AddOwner(Owner);
        await rig.Service.StartBreakGlassAsync(Replace());
        await rig.Service.ApproveRecoveryAsync(rig.TokenSentTo(Approver1), waiveDelay: true);
        var second = await rig.Service.ApproveRecoveryAsync(rig.TokenSentTo(Approver2), waiveDelay: false);

        Assert.Equal("recovery.approved-delayed", second.Code);
        Assert.False(old.Disabled);
        var id = rig.Store.Recoveries.Single().Id;

        Assert.Equal("refused.too-early", (await rig.Service.CompleteRecoveryAsync(id, Operator, dryRun: false)).Code);
        rig.Clock.Advance(TimeSpan.FromHours(24));
        Assert.True((await rig.Service.CompleteRecoveryAsync(id, Operator, dryRun: true)).Succeeded);
        Assert.False(old.Disabled); // dry run

        Assert.Equal("recovery.completed", (await rig.Service.CompleteRecoveryAsync(id, Operator, dryRun: false)).Code);
        Assert.True(old.Disabled);
        Assert.Equal("refused.not-approved", (await rig.Service.CompleteRecoveryAsync(id, Operator, dryRun: false)).Code);
    }

    [Fact]
    public async Task Level4_Refusals()
    {
        var rig = new ProviderTestRig();
        rig.AddOwner(Owner);
        rig.Accounts.Add("existing@alpha.test");

        Assert.Equal("refused.approvers", (await rig.Service.StartBreakGlassAsync(Replace() with { Approvers = new[] { Approver1 } })).Code);
        Assert.Equal("refused.approvers", (await rig.Service.StartBreakGlassAsync(Replace() with { Approvers = new[] { Approver1, Approver1 } })).Code);
        Assert.Equal("refused.no-approval-reference", (await rig.Service.StartBreakGlassAsync(Replace() with { ApprovalReference = " " })).Code);
        Assert.Equal("refused.invalid-email", (await rig.Service.StartBreakGlassAsync(Replace(newEmail: "nope"))).Code);
        Assert.Equal("refused.account-exists", (await rig.Service.StartBreakGlassAsync(Replace(newEmail: "existing@alpha.test"))).Code);
        Assert.Empty(rig.Store.Recoveries);
    }

    [Fact]
    public async Task Level4_Dry_Run_Changes_Nothing()
    {
        var rig = new ProviderTestRig();
        var old = rig.AddOwner(Owner);
        var before = rig.Store.Snapshot();

        Assert.True((await rig.Service.StartBreakGlassAsync(Replace(dryRun: true))).Succeeded);
        Assert.Equal(before, rig.Store.Snapshot());
        Assert.Empty(rig.Email.Sent);
        Assert.False(old.Disabled);
        Assert.Single(rig.Accounts.All);
    }
}
