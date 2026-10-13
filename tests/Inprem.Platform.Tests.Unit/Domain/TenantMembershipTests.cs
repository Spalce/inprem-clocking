using Inprem.Platform.Domain.Memberships;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Domain;

public class TenantMembershipTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateActive_Sets_Every_Field()
    {
        var membership = TenantMembership.CreateActive("user-1", 7, MembershipRole.Owner, Now);

        Assert.NotEqual(Guid.Empty, membership.Id);
        Assert.Equal(("user-1", 7, MembershipRole.Owner, MembershipStatus.Active, Now), (membership.UserId, membership.TenantId, membership.Role, membership.Status, membership.CreatedUtc));
        Assert.Null(membership.InvitedByUserId);
        Assert.True(membership.IsActive);
    }

    [Fact]
    public void CreateInvited_Is_Not_Active_Until_Accepted()
    {
        var membership = TenantMembership.CreateInvited("user-2", 7, MembershipRole.Admin, "owner-1", Now);

        Assert.Equal(MembershipStatus.Invited, membership.Status);
        Assert.False(membership.IsActive);
        Assert.Equal("owner-1", membership.InvitedByUserId);

        membership.Activate();
        Assert.True(membership.IsActive);
    }

    [Theory]
    [InlineData("", 7)]
    [InlineData("  ", 7)]
    public void A_Membership_Needs_A_User(string userId, int tenantId)
    {
        Assert.Throws<ArgumentException>(() => TenantMembership.CreateActive(userId, tenantId, MembershipRole.Admin, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Membership_Needs_An_Organization(int tenantId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TenantMembership.CreateActive("user", tenantId, MembershipRole.Admin, Now));
    }

    [Fact]
    public void Times_Must_Be_Utc()
    {
        Assert.Throws<ArgumentException>(() => TenantMembership.CreateActive("user", 1, MembershipRole.Admin, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public void An_Invitation_Needs_The_Inviter()
    {
        Assert.Throws<ArgumentException>(() => TenantMembership.CreateInvited("user", 1, MembershipRole.Admin, " ", Now));
    }

    [Fact]
    public void Revoked_Memberships_Cannot_Be_Reactivated_Or_Change_Role()
    {
        var membership = TenantMembership.CreateActive("user", 1, MembershipRole.Admin, Now);

        membership.Revoke();

        Assert.Equal(MembershipStatus.Revoked, membership.Status);
        Assert.False(membership.IsActive);
        Assert.Throws<InvalidOperationException>(() => membership.Activate());
        Assert.Throws<InvalidOperationException>(() => membership.ChangeRole(MembershipRole.Owner));
    }

    [Fact]
    public void ChangeRole_On_An_Active_Membership()
    {
        var membership = TenantMembership.CreateActive("user", 1, MembershipRole.Admin, Now);

        membership.ChangeRole(MembershipRole.Owner);

        Assert.Equal(MembershipRole.Owner, membership.Role);
    }

    [Fact]
    public void Reinvite_Renews_A_Revoked_Membership_But_Not_An_Active_One()
    {
        var membership = TenantMembership.CreateActive("user", 1, MembershipRole.Admin, Now);
        Assert.Throws<InvalidOperationException>(() => membership.Reinvite("owner"));

        membership.Revoke();
        membership.Reinvite("owner-2");

        Assert.Equal((MembershipStatus.Invited, "owner-2"), (membership.Status, membership.InvitedByUserId));
        Assert.Throws<ArgumentException>(() => membership.Reinvite(""));
    }

    [Fact]
    public void Only_An_Invitation_Can_Be_Declined()
    {
        var invitation = TenantMembership.CreateInvited("user", 1, MembershipRole.Admin, "owner", Now);
        invitation.Decline();
        Assert.Equal(MembershipStatus.Revoked, invitation.Status);

        Assert.Throws<InvalidOperationException>(() => invitation.Decline());
        Assert.Throws<InvalidOperationException>(() => TenantMembership.CreateActive("user", 1, MembershipRole.Admin, Now).Decline());
    }
}
