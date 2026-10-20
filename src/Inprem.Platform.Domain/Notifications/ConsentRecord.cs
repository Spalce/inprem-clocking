namespace Inprem.Platform.Domain.Notifications;

public enum ConsentAction
{
    /// <summary>The person agreed to texts (for one organization, or from Inprem when TenantId is null).</summary>
    Grant,

    /// <summary>The person withdrew: a STOP reply (every organization and Inprem), or a choice for one organization or for Inprem (TenantId null, not a STOP).</summary>
    Revoke,

    /// <summary>A START reply after STOP: the texts they had agreed to resume (it grants nothing new).</summary>
    Resubscribe,
}

/// <summary>Where a consent record came from.</summary>
public static class ConsentSources
{
    public const string WebsiteSignup = "website-signup";
    public const string SmsStop = "sms-stop";
    public const string SmsStart = "sms-start";
    public const string Console = "console";
}

/// <summary>
/// One consent event for a phone number (Phase 5 step 5.1, design Appendix C: who, when, how and the
/// exact wording). Append-only: the history is the proof, so a change is a new record, never an edit.
/// A STOP reply stops every text to the number, whichever organization it came from, because the
/// texts all come from Inprem's one sender.
/// </summary>
public sealed class ConsentRecord
{
    public const int MaxWordingLength = 1000;

    private ConsentRecord()
    {
        Phone = "";
        Source = "";
        RecordedBy = "";
    }

    public Guid Id { get; private set; }

    /// <summary>E.164.</summary>
    public string Phone { get; private set; }
    public ConsentAction Action { get; private set; }

    /// <summary>The organization a grant is for; null for Inprem's own texts and for a STOP or START (every organization).</summary>
    public int? TenantId { get; private set; }

    /// <summary>The account or person it belongs to, when known.</summary>
    public string? PersonRef { get; private set; }
    public string Source { get; private set; }

    /// <summary>The wording the person agreed to, word for word (grants only).</summary>
    public string? Wording { get; private set; }
    public DateTime RecordedUtc { get; private set; }
    public string RecordedBy { get; private set; }

    public static ConsentRecord Record(string phone, ConsentAction action, int? tenantId, string? personRef, string source, string? wording, DateTime nowUtc, string recordedBy)
    {
        var e164 = PhoneNumber.ToE164(phone);
        if (e164 == null || e164 != phone) throw new ArgumentException("An E.164 phone number.", nameof(phone));
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        if (string.IsNullOrWhiteSpace(source) || source.Length > 50) throw new ArgumentException("A source (at most 50 characters).", nameof(source));
        if (action == ConsentAction.Grant && string.IsNullOrWhiteSpace(wording)) throw new ArgumentException("A grant keeps the wording agreed to.", nameof(wording));
        if (wording?.Length > MaxWordingLength) throw new ArgumentException($"At most {MaxWordingLength} characters.", nameof(wording));
        if (personRef?.Length > 450) throw new ArgumentException("At most 450 characters.", nameof(personRef));
        return new ConsentRecord
        {
            Id = Guid.NewGuid(), Phone = phone, Action = action, TenantId = tenantId, PersonRef = personRef, Source = source,
            Wording = action == ConsentAction.Grant ? wording!.Trim() : null, RecordedUtc = nowUtc,
            RecordedBy = string.IsNullOrWhiteSpace(recordedBy) ? "system" : recordedBy.Length > 300 ? recordedBy[..300] : recordedBy,
        };
    }

    /// <summary>
    /// Whether texts from <paramref name="tenantId"/> (null: Inprem) may go to the number, from its
    /// history: a grant for that organization, and no withdrawal after it unless a later START.
    /// </summary>
    public static ConsentState StateFor(IEnumerable<ConsentRecord> historyOfOnePhone, int? tenantId)
    {
        var history = historyOfOnePhone.ToList();
        var grant = history.Where(r => r.Action == ConsentAction.Grant && r.TenantId == tenantId).MaxBy(r => r.RecordedUtc);
        if (grant == null) return ConsentState.NotGiven;
        // A STOP reply withdraws everything; any other withdrawal only its own scope (an organization, or Inprem when null).
        var revoke = history.Where(r => r.Action == ConsentAction.Revoke && (r.TenantId == tenantId || r.Source == ConsentSources.SmsStop)).MaxBy(r => r.RecordedUtc);
        if (revoke == null || revoke.RecordedUtc < grant.RecordedUtc) return ConsentState.Given;
        var resubscribe = history.Where(r => r.Action == ConsentAction.Resubscribe).MaxBy(r => r.RecordedUtc);
        return revoke.Source == ConsentSources.SmsStop && resubscribe != null && resubscribe.RecordedUtc > revoke.RecordedUtc ? ConsentState.Given : ConsentState.Withdrawn;
    }
}

public enum ConsentState
{
    NotGiven,
    Given,
    Withdrawn,
}
