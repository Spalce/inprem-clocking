namespace Inprem.Platform.Domain.Provider;

public enum RecoveryLevel
{
    /// <summary>Self-service: sign in with a recovery code.</summary>
    SelfService = 1,

    /// <summary>Peer reset: another Provider Owner resets the person's two-factor.</summary>
    PeerReset = 2,

    /// <summary>Break-glass reissue to the owner's existing verified email.</summary>
    BreakGlassReissue = 3,

    /// <summary>Break-glass replace: old owner disabled, new owner invited.</summary>
    BreakGlassReplace = 4,
}

public enum RecoveryStatus
{
    /// <summary>Waiting for the approver(s) to confirm by email link.</summary>
    PendingApproval,

    /// <summary>Approved; Level 4 waits for its 24-hour delay unless both approvers waived it.</summary>
    Approved,
    Completed,
    Expired,
}

/// <summary>One recovery (design 5.3/5.4). Levels 3 and 4 need the approvers' confirmation.</summary>
public sealed class RecoveryRecord
{
    private RecoveryRecord()
    {
        TargetUserId = "";
        TargetEmail = "";
        Operator = "";
        Reason = "";
    }

    public Guid Id { get; private set; }
    public RecoveryLevel Level { get; private set; }
    public RecoveryStatus Status { get; private set; }

    /// <summary>The owner being recovered (Level 4: the owner being replaced).</summary>
    public string TargetUserId { get; private set; }
    public string TargetEmail { get; private set; }

    /// <summary>Level 4 only: who replaces the old owner.</summary>
    public string? NewEmail { get; private set; }

    /// <summary>Who started it (operator or provider user) and why / the written approval reference.</summary>
    public string Operator { get; private set; }
    public string Reason { get; private set; }

    public string? FirstApproverEmail { get; private set; }
    public string? FirstApprovalTokenHash { get; private set; }
    public DateTime? FirstApprovedUtc { get; private set; }
    public bool FirstWaivedDelay { get; private set; }

    /// <summary>Level 4 only: the second approver.</summary>
    public string? SecondApproverEmail { get; private set; }
    public string? SecondApprovalTokenHash { get; private set; }
    public DateTime? SecondApprovedUtc { get; private set; }
    public bool SecondWaivedDelay { get; private set; }

    public DateTime CreatedUtc { get; private set; }
    public DateTime ApprovalExpiresUtc { get; private set; }

    /// <summary>Level 4: earliest completion time (24 h after the last approval unless both waived).</summary>
    public DateTime? NotBeforeUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }

    public static readonly TimeSpan ApprovalWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan ReplaceDelay = TimeSpan.FromHours(24);

    /// <summary>Levels 1 and 2: done at once, recorded for the audit trail and the rate limit.</summary>
    public static RecoveryRecord Immediate(RecoveryLevel level, string targetUserId, string targetEmail, string actor, string reason, DateTime nowUtc)
    {
        if (level is not (RecoveryLevel.SelfService or RecoveryLevel.PeerReset)) throw new ArgumentOutOfRangeException(nameof(level));
        var record = New(level, targetUserId, targetEmail, actor, reason, nowUtc);
        record.Status = RecoveryStatus.Completed;
        record.CompletedUtc = nowUtc;
        return record;
    }

    public static RecoveryRecord BreakGlassReissue(string targetUserId, string targetEmail, string actor, string approverEmail, string approvalTokenHash, DateTime nowUtc)
    {
        var record = New(RecoveryLevel.BreakGlassReissue, targetUserId, targetEmail, actor, "Break-glass reissue", nowUtc);
        record.FirstApproverEmail = approverEmail;
        record.FirstApprovalTokenHash = approvalTokenHash;
        return record;
    }

    public static RecoveryRecord BreakGlassReplace(string targetUserId, string targetEmail, string newEmail, string actor, string approvalReference,
        string firstApprover, string firstTokenHash, string secondApprover, string secondTokenHash, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(approvalReference)) throw new ArgumentException("Level 4 needs the written approval reference.", nameof(approvalReference));
        if (string.Equals(firstApprover, secondApprover, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Level 4 needs two different approvers.");
        var record = New(RecoveryLevel.BreakGlassReplace, targetUserId, targetEmail, actor, approvalReference, nowUtc);
        record.NewEmail = newEmail;
        record.FirstApproverEmail = firstApprover;
        record.FirstApprovalTokenHash = firstTokenHash;
        record.SecondApproverEmail = secondApprover;
        record.SecondApprovalTokenHash = secondTokenHash;
        return record;
    }

    private static RecoveryRecord New(RecoveryLevel level, string targetUserId, string targetEmail, string actor, string reason, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A recovery needs a reason.", nameof(reason));
        return new RecoveryRecord
        {
            Id = Guid.NewGuid(), Level = level, Status = RecoveryStatus.PendingApproval, TargetUserId = targetUserId, TargetEmail = targetEmail,
            Operator = actor, Reason = reason, CreatedUtc = nowUtc, ApprovalExpiresUtc = nowUtc + ApprovalWindow,
        };
    }

    /// <summary>Which approver a token belongs to: 1, 2, or 0 when it matches neither.</summary>
    public int ApproverFor(string tokenHash) =>
        FirstApprovalTokenHash == tokenHash ? 1 : SecondApprovalTokenHash != null && SecondApprovalTokenHash == tokenHash ? 2 : 0;

    /// <summary>Records one approver's confirmation. Returns true when every required approval is in.</summary>
    public bool Approve(int approver, bool waiveDelay, DateTime nowUtc)
    {
        if (Status != RecoveryStatus.PendingApproval) throw new InvalidOperationException("This recovery is not waiting for approval.");
        if (nowUtc >= ApprovalExpiresUtc)
        {
            Status = RecoveryStatus.Expired;
            return false;
        }

        if (approver == 1) { FirstApprovedUtc ??= nowUtc; FirstWaivedDelay = waiveDelay; }
        else if (approver == 2 && SecondApproverEmail != null) { SecondApprovedUtc ??= nowUtc; SecondWaivedDelay = waiveDelay; }
        else throw new ArgumentOutOfRangeException(nameof(approver));

        var complete = FirstApprovedUtc != null && (SecondApproverEmail == null || SecondApprovedUtc != null);
        if (!complete) return false;

        Status = RecoveryStatus.Approved;
        NotBeforeUtc = Level == RecoveryLevel.BreakGlassReplace && !(FirstWaivedDelay && SecondWaivedDelay)
            ? nowUtc + ReplaceDelay
            : nowUtc;
        return true;
    }

    public bool CanComplete(DateTime nowUtc) => Status == RecoveryStatus.Approved && NotBeforeUtc <= nowUtc;

    public void Complete(DateTime nowUtc)
    {
        if (!CanComplete(nowUtc)) throw new InvalidOperationException("This recovery cannot be completed now.");
        Status = RecoveryStatus.Completed;
        CompletedUtc = nowUtc;
    }
}
