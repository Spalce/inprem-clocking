namespace Inprem.Platform.Domain.Audit;

/// <summary>
/// One audited action. Append-only: never updated or deleted (enforced by PlatformDbContext and
/// by a database trigger). Holds no secrets: codes, tokens and passwords are never written here.
/// </summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
        Actor = "";
        Action = "";
    }

    public long Id { get; private set; }
    public DateTime OccurredUtc { get; private set; }

    /// <summary>Who: "operator:{machine user}" for commands, "provider:{user id}", "anonymous:{ip}" for the setup page.</summary>
    public string Actor { get; private set; }

    /// <summary>Stable action name, e.g. "provider.bootstrap.succeeded".</summary>
    public string Action { get; private set; }

    /// <summary>The account or object acted on (an id; never a secret).</summary>
    public string? Target { get; private set; }
    public int? TenantId { get; private set; }

    /// <summary>Human-readable details (no secrets, no full personal data beyond what the action needs).</summary>
    public string? Details { get; private set; }

    public static AuditEntry Record(string actor, string action, string? target, string? details, DateTime nowUtc, int? tenantId = null)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("An audit entry needs an actor.", nameof(actor));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("An audit entry needs an action.", nameof(action));
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        return new AuditEntry { Actor = actor, Action = action, Target = target, Details = details, OccurredUtc = nowUtc, TenantId = tenantId };
    }
}
