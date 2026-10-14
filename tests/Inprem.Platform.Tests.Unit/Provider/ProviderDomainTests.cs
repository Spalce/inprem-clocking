using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Provider;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Provider;

/// <summary>Phase 0 step 0.7 domain rules: what the provider entities refuse, and their state changes.</summary>
public class ProviderDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LocalNow = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Local);

    [Fact]
    public void AuditEntry_Needs_Actor_Action_And_Utc()
    {
        Assert.Throws<ArgumentException>(() => AuditEntry.Record(" ", "a", null, null, Now));
        Assert.Throws<ArgumentException>(() => AuditEntry.Record("x", "", null, null, Now));
        Assert.Throws<ArgumentException>(() => AuditEntry.Record("x", "a", null, null, LocalNow));
        var entry = AuditEntry.Record("x", "a", "t", "d", Now, tenantId: 7);
        Assert.Equal(("x", "a", "t", "d", 7), (entry.Actor, entry.Action, entry.Target, entry.Details, entry.TenantId));
    }

    [Fact]
    public void Invitation_Refuses_Incomplete_Data()
    {
        var day = TimeSpan.FromDays(1);
        Assert.Throws<ArgumentException>(() => Invitation.Create("", "u", InvitationPurpose.ProviderSetup, "h", day, "c", Now));
        Assert.Throws<ArgumentException>(() => Invitation.Create("e@x.test", " ", InvitationPurpose.ProviderSetup, "h", day, "c", Now));
        Assert.Throws<ArgumentException>(() => Invitation.Create("e@x.test", "u", InvitationPurpose.ProviderSetup, "", day, "c", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => Invitation.Create("e@x.test", "u", InvitationPurpose.ProviderSetup, "h", TimeSpan.Zero, "c", Now));
        Assert.Throws<ArgumentException>(() => Invitation.Create("e@x.test", "u", InvitationPurpose.ProviderSetup, "h", day, "c", LocalNow));
    }

    [Fact]
    public void Invitation_Works_Once_And_A_Used_One_Cannot_Be_Revoked()
    {
        var invitation = Invitation.Create("e@x.test", "u", InvitationPurpose.ProviderSetup, "h", TimeSpan.FromHours(1), "c", Now);
        Assert.True(invitation.IsUsable(Now));
        invitation.Consume(Now);
        Assert.Throws<InvalidOperationException>(() => invitation.Consume(Now));
        invitation.Revoke(Now);
        Assert.Null(invitation.RevokedUtc);

        var revoked = Invitation.Create("e@x.test", "u", InvitationPurpose.ProviderSetup, "h2", TimeSpan.FromHours(1), "c", Now);
        revoked.Revoke(Now);
        Assert.False(revoked.IsUsable(Now));
        Assert.Throws<InvalidOperationException>(() => revoked.Consume(Now));

        var expired = Invitation.Create("e@x.test", "u", InvitationPurpose.ProviderSetup, "h3", TimeSpan.FromHours(1), "c", Now);
        Assert.False(expired.IsUsable(Now.AddHours(1)));
    }

    [Fact]
    public void ProviderUser_Rules()
    {
        Assert.Throws<ArgumentException>(() => ProviderUser.Invite("", ProviderRole.Owner, Now));
        Assert.Throws<ArgumentException>(() => ProviderUser.Invite("u", ProviderRole.Owner, LocalNow));

        var user = ProviderUser.Invite("u", ProviderRole.Owner, Now);
        Assert.False(user.IsActiveOwner);
        user.Activate();
        Assert.True(user.IsActiveOwner);
        user.Disable();
        Assert.False(user.IsActiveOwner);
        Assert.Throws<InvalidOperationException>(() => user.Activate());

        var support = ProviderUser.Invite("s", ProviderRole.Support, Now);
        support.Activate();
        Assert.False(support.IsActiveOwner);
    }

    [Fact]
    public void RecoveryRecord_Refuses_Invalid_Requests()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecoveryRecord.Immediate(RecoveryLevel.BreakGlassReissue, "u", "e", "a", "r", Now));
        Assert.Throws<ArgumentException>(() => RecoveryRecord.Immediate(RecoveryLevel.PeerReset, "u", "e", "a", " ", Now));
        Assert.Throws<ArgumentException>(() => RecoveryRecord.Immediate(RecoveryLevel.PeerReset, "u", "e", "a", "r", LocalNow));
        Assert.Throws<ArgumentException>(() => RecoveryRecord.BreakGlassReplace("u", "e", "n", "a", " ", "a1", "h1", "a2", "h2", Now));
        Assert.Throws<ArgumentException>(() => RecoveryRecord.BreakGlassReplace("u", "e", "n", "a", "ref", "same@x", "h1", "SAME@x", "h2", Now));

        var immediate = RecoveryRecord.Immediate(RecoveryLevel.SelfService, "u", "e", "a", "r", Now);
        Assert.Equal((RecoveryStatus.Completed, Now), (immediate.Status, immediate.CompletedUtc));
    }

    [Fact]
    public void RecoveryRecord_Approval_Rules()
    {
        var reissue = RecoveryRecord.BreakGlassReissue("u", "e", "a", "ap@x", "h1", Now);
        Assert.Equal(1, reissue.ApproverFor("h1"));
        Assert.Equal(0, reissue.ApproverFor("other"));
        Assert.False(reissue.CanComplete(Now));
        Assert.Throws<InvalidOperationException>(() => reissue.Complete(Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => reissue.Approve(2, false, Now)); // there is no second approver
        Assert.True(reissue.Approve(1, waiveDelay: true, Now));
        Assert.Equal(Now, reissue.NotBeforeUtc);                                        // Level 3 has no delay
        Assert.Throws<InvalidOperationException>(() => reissue.Approve(1, false, Now)); // not pending any more
        reissue.Complete(Now);
        Assert.Equal(RecoveryStatus.Completed, reissue.Status);

        var replace = RecoveryRecord.BreakGlassReplace("u", "e", "n", "a", "ref", "a1@x", "h1", "a2@x", "h2", Now);
        Assert.Equal(2, replace.ApproverFor("h2"));
        Assert.False(replace.Approve(2, waiveDelay: true, Now));
        Assert.True(replace.Approve(1, waiveDelay: true, Now));
        Assert.Equal(Now, replace.NotBeforeUtc);                                        // both waived

        var expired = RecoveryRecord.BreakGlassReissue("u", "e", "a", "ap@x", "h9", Now);
        Assert.False(expired.Approve(1, false, Now + RecoveryRecord.ApprovalWindow));
        Assert.Equal(RecoveryStatus.Expired, expired.Status);
    }
}
