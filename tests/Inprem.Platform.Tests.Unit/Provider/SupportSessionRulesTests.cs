using Inprem.Platform.Domain.Support;
using Xunit;

namespace Inprem.Platform.Tests.Unit.Provider;

/// <summary>Phase 1 step 1.5: support session rules (length, reason, single-use 60-second hand-off, ending).</summary>
public class SupportSessionRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static SupportSession Start(int minutes = 60) => SupportSession.Start(7, "user", "support@inprem.test", "Help with reports", minutes, "hash", Now);

    [Fact]
    public void Start_Needs_A_Reason_An_Allowed_Length_And_A_Token()
    {
        Assert.Throws<ArgumentException>(() => SupportSession.Start(7, "u", "e", " ", 60, "hash", Now));
        Assert.Throws<ArgumentException>(() => SupportSession.Start(7, "u", "e", new string('x', 501), 60, "hash", Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => SupportSession.Start(7, "u", "e", "r", 45, "hash", Now));
        Assert.Throws<ArgumentException>(() => SupportSession.Start(7, "u", "e", "r", 60, "", Now));
        Assert.Throws<ArgumentException>(() => SupportSession.Start(7, "u", "e", "r", 60, "hash", DateTime.Now));

        var session = Start(15);
        Assert.Equal((Now.AddMinutes(15), Now.AddSeconds(60)), (session.ExpiresUtc, session.HandoffExpiresUtc));
    }

    [Fact]
    public void Handoff_Works_Once_Within_60_Seconds()
    {
        var late = Start();
        Assert.False(late.CanRedeem(Now.AddSeconds(60)));
        Assert.Throws<InvalidOperationException>(() => late.Redeem(Now.AddSeconds(61)));

        var session = Start();
        Assert.True(session.CanRedeem(Now.AddSeconds(59)));
        session.Redeem(Now.AddSeconds(59));
        Assert.False(session.CanRedeem(Now.AddSeconds(59)));
        Assert.Throws<InvalidOperationException>(() => session.Redeem(Now.AddSeconds(59)));
    }

    [Fact]
    public void Ending_Is_Final_And_Never_Later_Than_The_End_Time()
    {
        var session = Start(15);
        Assert.True(session.IsActive(Now.AddMinutes(14)));
        Assert.False(session.IsActive(Now.AddMinutes(15)));

        session.End("expired", Now.AddMinutes(40));
        Assert.Equal((Now.AddMinutes(15), "expired"), (session.EndedUtc!.Value, session.EndedBy));
        session.End("someone else", Now.AddMinutes(50));
        Assert.Equal("expired", session.EndedBy);

        var early = Start();
        early.End("provider:x", Now.AddMinutes(5));
        Assert.False(early.IsActive(Now.AddMinutes(6)));
        Assert.False(early.CanRedeem(Now.AddSeconds(10)));
    }
}
