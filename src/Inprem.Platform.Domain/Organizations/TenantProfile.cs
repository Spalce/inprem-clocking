using System.Text.RegularExpressions;

namespace Inprem.Platform.Domain.Organizations;

public enum OrganizationStatus
{
    Active,

    /// <summary>Suspended from the Provider Console (with a reason). Enforced as read-only from Phase 2 (C3-3).</summary>
    Suspended,

    /// <summary>Signed up on the website; the owner has not verified their email yet (Phase 3, D22).</summary>
    PendingVerification,
}

/// <summary>
/// The platform's view of an organization (dbo.Tenants keeps name, time zone, address and contact):
/// its unique slug and its console status (Phase 1 step 1.2).
/// </summary>
public sealed class TenantProfile
{
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

    /// <summary>Not available to organizations: they would clash with site addresses.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
    {
        "admin", "api", "app", "www", "provider", "setup", "login", "signup", "billing", "support", "help", "status", "mail", "inprem",
    };

    private TenantProfile()
    {
        Slug = "";
    }

    public int TenantId { get; private set; }

    /// <summary>Short unique name for addresses and the website, e.g. "akim-asafo".</summary>
    public string Slug { get; private set; }
    public OrganizationStatus Status { get; private set; }
    public string? StatusReason { get; private set; }
    public DateTime? StatusChangedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    public static TenantProfile Create(int tenantId, string slug, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        var profile = new TenantProfile { TenantId = tenantId, Status = OrganizationStatus.Active, CreatedUtc = nowUtc };
        profile.ChangeSlug(slug);
        return profile;
    }

    /// <summary>Lower-case letters, digits and single dashes; 3 to 50 characters; not reserved.</summary>
    public static string? SlugProblem(string? slug)
    {
        var value = slug ?? "";
        if (value.Length is < 3 or > 50) return "The address name must be 3 to 50 characters.";
        if (!SlugPattern.IsMatch(value)) return "Use lower-case letters, digits and single dashes (e.g. akim-asafo).";
        if (Reserved.Contains(value)) return $"\"{value}\" is reserved. Choose another.";
        return null;
    }

    /// <summary>A slug made from a name: "AKIM ASAFO SHS" becomes "akim-asafo-shs".</summary>
    public static string Suggest(string? name)
    {
        var lower = (name ?? "").Trim().ToLowerInvariant();
        var slug = Regex.Replace(lower, "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 50) slug = slug[..50].TrimEnd('-');
        if (slug.Length < 3) slug = (slug + "-org").Trim('-');
        return Reserved.Contains(slug) ? slug + "-org" : slug;
    }

    public void ChangeSlug(string slug)
    {
        var value = (slug ?? "").Trim().ToLowerInvariant();
        if (SlugProblem(value) is { } problem) throw new ArgumentException(problem, nameof(slug));
        Slug = value;
    }

    public void Suspend(string reason, DateTime nowUtc) => ChangeStatus(OrganizationStatus.Suspended, reason, nowUtc);

    /// <summary>A website signup (Phase 3): the organization waits until its owner verifies their email (D22).</summary>
    public static TenantProfile CreatePendingVerification(int tenantId, string slug, DateTime nowUtc)
    {
        var profile = Create(tenantId, slug, nowUtc);
        profile.Status = OrganizationStatus.PendingVerification;
        profile.StatusReason = "Signed up on the website; waiting for the owner to verify their email.";
        profile.StatusChangedUtc = nowUtc;
        return profile;
    }

    /// <summary>The owner verified their email: PendingVerification → Active (nothing else changes).</summary>
    public bool Verified(DateTime nowUtc)
    {
        if (Status != OrganizationStatus.PendingVerification) return false;
        Status = OrganizationStatus.Active;
        StatusReason = "Owner verified their email.";
        StatusChangedUtc = nowUtc;
        return true;
    }

    public void Reactivate(string reason, DateTime nowUtc) => ChangeStatus(OrganizationStatus.Active, reason, nowUtc);

    private void ChangeStatus(OrganizationStatus status, string reason, DateTime nowUtc)
    {
        if (Status == status) throw new InvalidOperationException($"The organization is already {status.ToString().ToLowerInvariant()}.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw new ArgumentException("Give a reason (up to 500 characters).", nameof(reason));
        Status = status;
        StatusReason = reason.Trim();
        StatusChangedUtc = nowUtc;
    }
}

/// <summary>A provider's note about an organization (Notes tab).</summary>
public sealed class TenantNote
{
    private TenantNote()
    {
        Text = "";
        Author = "";
    }

    public Guid Id { get; private set; }
    public int TenantId { get; private set; }
    public string Text { get; private set; }

    /// <summary>Who wrote it: "provider:{account id}".</summary>
    public string Author { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    public static TenantNote Create(int tenantId, string text, string author, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 2000) throw new ArgumentException("A note needs text (up to 2,000 characters).", nameof(text));
        return new TenantNote { Id = Guid.NewGuid(), TenantId = tenantId, Text = text.Trim(), Author = author, CreatedUtc = nowUtc };
    }
}
