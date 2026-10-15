using System.Security.Cryptography;

namespace Inprem.Platform.Domain.Billing;

public enum CheckoutPurpose
{
    /// <summary>A new organization from the website signup (Phase 3).</summary>
    New,

    /// <summary>A trial becomes a paid subscription.</summary>
    Convert,

    /// <summary>An Expired, Canceled or Suspended organization subscribes again.</summary>
    Resubscribe,

    /// <summary>A paying organization moves to another price (PayPal revision).</summary>
    ChangePlan,
}

/// <summary>Design 7.5 checkout-session states.</summary>
public enum CheckoutStatus
{
    Created,
    AwaitingApproval,
    Approved,
    Active,
    Cancelled,
    Expired,
    Failed,
}

/// <summary>What PayPal says about a subscription (its <c>status</c> field).</summary>
public enum PayPalSubscriptionState
{
    ApprovalPending,
    Approved,
    Active,
    Suspended,
    Cancelled,
    Expired,
    Unknown,
}

/// <summary>
/// One attempt to subscribe through PayPal (Phase 2 step 2.1, design 7.4 and 7.5). Its
/// <see cref="Reference"/> is sent to PayPal as <c>custom_id</c>, and checked when PayPal reports
/// back. Final states (Active, Cancelled, Expired, Failed) never change again.
/// </summary>
public sealed class CheckoutSession
{
    /// <summary>PayPal's approve link is valid for about 3 hours; the session too.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(3);

    private CheckoutSession()
    {
        Reference = "";
        IdempotencyKey = "";
        ReturnUrl = "";
        CancelUrl = "";
        CreatedBy = "";
    }

    public Guid Id { get; private set; }

    /// <summary>Random, unguessable; PayPal's <c>custom_id</c>.</summary>
    public string Reference { get; private set; }

    /// <summary>The organization (convert, resubscribe, change plan), or null for a website signup.</summary>
    public int? TenantId { get; private set; }

    /// <summary>The website signup (Phase 3), or null for an organization.</summary>
    public Guid? SignupId { get; private set; }
    public CheckoutPurpose Purpose { get; private set; }
    public Guid PlanPriceId { get; private set; }

    /// <summary>The PayPal plan the subscription was created with (checked when PayPal reports back).</summary>
    public string? PayPalPlanId { get; private set; }
    public CheckoutStatus Status { get; private set; }
    public string? ExternalSubscriptionId { get; private set; }
    public string? ApprovalUrl { get; private set; }
    public string ReturnUrl { get; private set; }
    public string CancelUrl { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>Who started it ("user:{id}", "signup:{id}").</summary>
    public string CreatedBy { get; private set; }

    public bool IsFinal => Status is CheckoutStatus.Active or CheckoutStatus.Cancelled or CheckoutStatus.Expired or CheckoutStatus.Failed;

    public static CheckoutSession Create(CheckoutPurpose purpose, int? tenantId, Guid? signupId, PlanPrice price, string returnUrl, string cancelUrl, string idempotencyKey, string createdBy, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if ((tenantId == null) == (signupId == null)) throw new ArgumentException("A checkout is for one organization or one signup.", nameof(tenantId));
        if ((purpose == CheckoutPurpose.New) != (signupId != null)) throw new ArgumentException("Only a signup checks out as new; organizations convert, resubscribe or change plan.", nameof(purpose));
        if (price.Status != PlanStatus.Active) throw new ArgumentException("That price is not offered any more.", nameof(price));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100) throw new ArgumentException("An idempotency key (at most 100 characters) is required.", nameof(idempotencyKey));
        AbsoluteHttps(returnUrl, nameof(returnUrl));
        AbsoluteHttps(cancelUrl, nameof(cancelUrl));

        return new CheckoutSession
        {
            Id = Guid.NewGuid(), Reference = NewReference(), TenantId = tenantId, SignupId = signupId, Purpose = purpose,
            PlanPriceId = price.Id, Status = CheckoutStatus.Created, ReturnUrl = returnUrl, CancelUrl = cancelUrl,
            IdempotencyKey = idempotencyKey.Trim(), CreatedBy = createdBy, CreatedUtc = nowUtc, ExpiresUtc = nowUtc + Lifetime,
        };
    }

    /// <summary>Created → AwaitingApproval: PayPal created the subscription and gave its approve link.</summary>
    public void Issued(string externalSubscriptionId, string payPalPlanId, string approvalUrl)
    {
        if (Status != CheckoutStatus.Created) throw Refused("be issued");
        if (string.IsNullOrWhiteSpace(externalSubscriptionId) || externalSubscriptionId.Length > 50) throw new ArgumentException("PayPal subscription id required.", nameof(externalSubscriptionId));
        if (string.IsNullOrWhiteSpace(payPalPlanId)) throw new ArgumentException("PayPal plan id required.", nameof(payPalPlanId));
        AbsoluteHttps(approvalUrl, nameof(approvalUrl));
        ExternalSubscriptionId = externalSubscriptionId;
        PayPalPlanId = payPalPlanId;
        ApprovalUrl = approvalUrl;
        Status = CheckoutStatus.AwaitingApproval;
    }

    /// <summary>
    /// Applies what PayPal reports. APPROVAL_PENDING keeps waiting; APPROVED → Approved; ACTIVE →
    /// Active; CANCELLED → Cancelled; EXPIRED → Expired; anything else → Failed. Returns true if
    /// the status changed. Final sessions never change.
    /// </summary>
    public bool Apply(PayPalSubscriptionState state, DateTime nowUtc)
    {
        if (IsFinal || Status == CheckoutStatus.Created) return false;
        switch (state)
        {
            case PayPalSubscriptionState.ApprovalPending:
                return false;
            case PayPalSubscriptionState.Approved:
                if (Status == CheckoutStatus.Approved) return false;
                Status = CheckoutStatus.Approved;
                return true;
            case PayPalSubscriptionState.Active:
                return Finish(CheckoutStatus.Active, null, nowUtc);
            case PayPalSubscriptionState.Cancelled:
                return Finish(CheckoutStatus.Cancelled, "Cancelled in PayPal.", nowUtc);
            case PayPalSubscriptionState.Expired:
                return Finish(CheckoutStatus.Expired, "Expired in PayPal.", nowUtc);
            default:
                return Finish(CheckoutStatus.Failed, $"PayPal reported {state}.", nowUtc);
        }
    }

    /// <summary>The buyer came back through the cancel URL (AwaitingApproval → Cancelled).</summary>
    public bool CancelByBuyer(DateTime nowUtc) =>
        Status == CheckoutStatus.AwaitingApproval && Finish(CheckoutStatus.Cancelled, "Cancelled by the buyer.", nowUtc);

    /// <summary>No approval in time (Created or AwaitingApproval → Expired once <see cref="ExpiresUtc"/> has passed).</summary>
    public bool ExpireIfOver(DateTime nowUtc) =>
        Status is CheckoutStatus.Created or CheckoutStatus.AwaitingApproval && nowUtc >= ExpiresUtc && Finish(CheckoutStatus.Expired, "Not approved in time.", nowUtc);

    /// <summary>A verification mismatch, a PayPal error, or Approved for too long → Failed.</summary>
    public bool Fail(string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Give the reason.", nameof(reason));
        return !IsFinal && Finish(CheckoutStatus.Failed, reason.Length > 500 ? reason[..500] : reason, nowUtc);
    }

    private bool Finish(CheckoutStatus status, string? reason, DateTime nowUtc)
    {
        Status = status;
        FailureReason = reason;
        CompletedUtc = nowUtc;
        return true;
    }

    private InvalidOperationException Refused(string what) => new($"A {Status} checkout cannot {what}.");

    private static string NewReference() => "chk_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static void AbsoluteHttps(string url, string name)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2000 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("An absolute https address is required.", name);
    }
}

/// <summary>Maps PayPal's subscription <c>status</c> (as sent, <c>APPROVAL_PENDING</c>, or as the SDK names it, <c>ApprovalPending</c>).</summary>
public static class PayPalStatus
{
    public static PayPalSubscriptionState Parse(string? status) => status?.Trim().Replace("_", "").ToUpperInvariant() switch
    {
        "APPROVALPENDING" => PayPalSubscriptionState.ApprovalPending,
        "APPROVED" => PayPalSubscriptionState.Approved,
        "ACTIVE" => PayPalSubscriptionState.Active,
        "SUSPENDED" => PayPalSubscriptionState.Suspended,
        "CANCELLED" => PayPalSubscriptionState.Cancelled,
        "EXPIRED" => PayPalSubscriptionState.Expired,
        _ => PayPalSubscriptionState.Unknown,
    };
}
