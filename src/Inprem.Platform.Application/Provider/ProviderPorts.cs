using System.Security.Cryptography;
using System.Text;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Provider;

namespace Inprem.Platform.Application.Provider;

/// <summary>Persistence of provider users, invitations, recoveries, the audit log and code use.</summary>
public interface IProviderStore
{
    Task<ProviderUser?> FindProviderUserAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// True when an Owner is Active, or Invited with a setup link still usable at <paramref name="nowUtc"/>.
    /// (An Invited owner whose link expired unused does not block bootstrap with a new code.)
    /// </summary>
    Task<bool> AnyOwnerAsync(DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Active owners with their account email and whether two-factor is enrolled.</summary>
    Task<IReadOnlyList<OwnerInfo>> ListActiveOwnersAsync(CancellationToken ct = default);

    Task AddProviderUserAsync(ProviderUser user, CancellationToken ct = default);

    /// <summary>Every provider user with their account's email and two-factor state.</summary>
    Task<IReadOnlyList<ProviderUserRow>> ListProviderUsersAsync(CancellationToken ct = default);

    Task AddInvitationAsync(Invitation invitation, CancellationToken ct = default);
    Task<Invitation?> FindInvitationByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task<IReadOnlyList<Invitation>> ListPendingInvitationsAsync(string userId, DateTime nowUtc, CancellationToken ct = default);

    Task AddRecoveryAsync(RecoveryRecord record, CancellationToken ct = default);
    Task<RecoveryRecord?> FindRecoveryAsync(Guid id, CancellationToken ct = default);
    Task<RecoveryRecord?> FindRecoveryByApprovalHashAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Whether a recovery (any level, any outcome) for this account started after <paramref name="sinceUtc"/>.</summary>
    Task<bool> AnyRecoverySinceAsync(string targetUserId, DateTime sinceUtc, CancellationToken ct = default);

    Task<bool> IsCodeUsedAsync(string codeHash, CancellationToken ct = default);
    Task MarkCodeUsedAsync(string codeHash, string purpose, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Makes a code usable again (only when the action it was used for was fully undone).</summary>
    Task ReleaseCodeAsync(string codeHash, CancellationToken ct = default);

    Task AddAttemptAsync(string kind, bool succeeded, string source, DateTime nowUtc, CancellationToken ct = default);
    Task<int> CountFailuresSinceAsync(string kind, DateTime sinceUtc, CancellationToken ct = default);

    Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record OwnerInfo(string UserId, string Email, bool TwoFactorEnabled);

public sealed record ProviderUserRow(string UserId, string Email, ProviderRole Role, ProviderUserStatus Status, bool TwoFactorEnabled, DateTime CreatedUtc);

/// <summary>Identity operations the provider use cases need (implemented over UserManager in the web app).</summary>
public interface IAccountGateway
{
    Task<AccountInfo?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<AccountInfo?> FindByIdAsync(string userId, CancellationToken ct = default);

    /// <summary>Creates an account with no password (the person sets it from the invitation).</summary>
    Task<string> CreateWithoutPasswordAsync(string email, CancellationToken ct = default);

    Task<string> CreateWithPasswordAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Sets the password of an account that has none, and marks the email confirmed.</summary>
    Task<IReadOnlyList<string>> SetPasswordAsync(string userId, string password, CancellationToken ct = default);

    /// <summary>Removes the password, two-factor (authenticator key, recovery codes) and signs out every session.</summary>
    Task ResetPasswordAndTwoFactorAsync(string userId, CancellationToken ct = default);

    /// <summary>Removes two-factor (authenticator key, recovery codes) and signs out every session.</summary>
    Task ResetTwoFactorAsync(string userId, CancellationToken ct = default);

    /// <summary>Blocks sign-in permanently and signs out every session.</summary>
    Task DisableAsync(string userId, CancellationToken ct = default);

    /// <summary>Checks a current authenticator code of the account (re-authentication).</summary>
    Task<bool> VerifyTwoFactorCodeAsync(string userId, string code, CancellationToken ct = default);
}

public sealed record AccountInfo(string Id, string Email, bool EmailConfirmed, bool HasPassword, bool TwoFactorEnabled);

/// <summary>
/// Configuration (server-only; never in the repository). Codes are stored as SHA-256 hashes.
/// <list type="bullet">
/// <item><c>Bootstrap:CodeHash</c> - one-time bootstrap code (D13).</item>
/// <item><c>Security:BreakGlassCodeHash</c> - one-time break-glass code (Levels 3 and 4).</item>
/// <item><c>Security:RecoveryApprovers</c> - who may approve Levels 3 and 4 (D15).</item>
/// <item><c>Security:Mailbox</c> - security mailbox copied on every bootstrap and recovery.</item>
/// <item><c>Links:AdminBaseUrl</c> - base URL of the Provider Console for links in emails: https://admin.inpremclockin.org,
/// or https://test.example.org/Provider on a site with SiteMode All.</item>
/// </list>
/// </summary>
public sealed class ProviderSecurityOptions
{
    public string? BootstrapCodeHash { get; set; }
    public string? BreakGlassCodeHash { get; set; }
    public string[] RecoveryApprovers { get; set; } = Array.Empty<string>();
    public string? SecurityMailbox { get; set; }
    public string? AdminBaseUrl { get; set; }
}

/// <summary>One-time tokens and codes: random values, stored and compared only as SHA-256 hashes.</summary>
public static class SecretTokens
{
    /// <summary>A URL-safe random token (32 bytes).</summary>
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>A code for people to copy: 6 groups of 5 characters from an unambiguous alphabet (about 140 bits).</summary>
    public static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = Enumerable.Range(0, 30).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray();
        return string.Join("-", Enumerable.Range(0, 6).Select(g => new string(chars, g * 5, 5)));
    }

    /// <summary>Lower-case hex SHA-256 of the trimmed value.</summary>
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();

    /// <summary>Compares a value with a stored hash in constant time.</summary>
    public static bool Matches(string value, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash)) return false;
        var actual = Encoding.ASCII.GetBytes(Hash(value));
        var expected = Encoding.ASCII.GetBytes(storedHash.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
