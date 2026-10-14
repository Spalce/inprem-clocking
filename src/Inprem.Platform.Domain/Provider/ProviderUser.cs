namespace Inprem.Platform.Domain.Provider;

public enum ProviderRole
{
    Owner,
    Billing,
    Support,
}

public enum ProviderUserStatus
{
    /// <summary>Account created, invitation sent; no password yet.</summary>
    Invited,
    Active,
    Disabled,
}

/// <summary>A person on the provider side (the Inprem team running the platform). Design section 5.</summary>
public sealed class ProviderUser
{
    private ProviderUser()
    {
        UserId = "";
    }

    /// <summary>The Identity account (AspNetUsers.Id).</summary>
    public string UserId { get; private set; }
    public ProviderRole Role { get; private set; }
    public ProviderUserStatus Status { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    public bool IsActiveOwner => Role == ProviderRole.Owner && Status == ProviderUserStatus.Active;

    public static ProviderUser Invite(string userId, ProviderRole role, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A provider user needs an account.", nameof(userId));
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Times are stored in UTC.", nameof(nowUtc));
        return new ProviderUser { UserId = userId, Role = role, Status = ProviderUserStatus.Invited, CreatedUtc = nowUtc };
    }

    /// <summary>The person accepted the invitation and set a password.</summary>
    public void Activate()
    {
        if (Status == ProviderUserStatus.Disabled) throw new InvalidOperationException("A disabled provider user cannot be activated.");
        Status = ProviderUserStatus.Active;
    }

    public void Disable() => Status = ProviderUserStatus.Disabled;

    public void ChangeRole(ProviderRole role)
    {
        if (Status == ProviderUserStatus.Disabled) throw new InvalidOperationException("A disabled provider user's role cannot be changed.");
        Role = role;
    }
}
