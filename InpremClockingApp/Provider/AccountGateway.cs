using Inprem.Platform.Application.Provider;
using InpremClockingApp.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace InpremClockingApp.Provider;

/// <summary>
/// The provider use cases' view of Identity (<see cref="IAccountGateway"/>). Every lookup goes
/// through UserManager and so through <c>AppUserStore</c>; provider accounts belong to no
/// organization (TenantId null).
/// </summary>
public sealed class AccountGateway : IAccountGateway
{
    private readonly UserManager<AppUser> _users;

    public AccountGateway(UserManager<AppUser> users) => _users = users;

    public async Task<AccountInfo?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(email) ? null : await InfoAsync(await _users.FindByEmailAsync(email.Trim()));

    public async Task<AccountInfo?> FindByIdAsync(string userId, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(userId) ? null : await InfoAsync(await _users.FindByIdAsync(userId));

    public async Task<string> CreateWithoutPasswordAsync(string email, CancellationToken ct = default)
    {
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = false, TenantId = null, LockoutEnabled = true };
        Check(await _users.CreateAsync(user), "create the account");
        return user.Id;
    }

    public async Task<string> CreateWithPasswordAsync(string email, string password, CancellationToken ct = default)
    {
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, TenantId = null, LockoutEnabled = true, RequiresPasswordChange = true };
        Check(await _users.CreateAsync(user, password), "create the account");
        return user.Id;
    }

    public async Task<IReadOnlyList<string>> SetPasswordAsync(string userId, string password, CancellationToken ct = default)
    {
        var user = await RequireAsync(userId);
        var result = await _users.AddPasswordAsync(user, password);
        if (!result.Succeeded) return result.Errors.Select(e => e.Description).ToList();

        // The link reached this mailbox, so the address is proven.
        user.EmailConfirmed = true;
        Check(await _users.UpdateAsync(user), "confirm the email");
        await _users.ResetAccessFailedCountAsync(user);
        return Array.Empty<string>();
    }

    public async Task ResetPasswordAndTwoFactorAsync(string userId, CancellationToken ct = default)
    {
        var user = await RequireAsync(userId);
        if (await _users.HasPasswordAsync(user)) Check(await _users.RemovePasswordAsync(user), "remove the password");
        await ClearTwoFactorAsync(user);
        await _users.SetLockoutEndDateAsync(user, null);
        await _users.ResetAccessFailedCountAsync(user);
    }

    public async Task ResetTwoFactorAsync(string userId, CancellationToken ct = default) =>
        await ClearTwoFactorAsync(await RequireAsync(userId));

    public async Task DisableAsync(string userId, CancellationToken ct = default)
    {
        var user = await RequireAsync(userId);
        Check(await _users.SetLockoutEnabledAsync(user, true), "enable lockout");
        Check(await _users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue), "lock the account");
        Check(await _users.UpdateSecurityStampAsync(user), "sign out every session");
    }

    public async Task<bool> VerifyTwoFactorCodeAsync(string userId, string code, CancellationToken ct = default)
    {
        var user = await _users.FindByIdAsync(userId);
        if (user == null || !await _users.GetTwoFactorEnabledAsync(user)) return false;
        return await _users.VerifyTwoFactorTokenAsync(user, _users.Options.Tokens.AuthenticatorTokenProvider, NormalizeCode(code));
    }

    /// <summary>Spaces and dashes are ignored in authenticator codes.</summary>
    public static string NormalizeCode(string code) => code.Replace(" ", string.Empty).Replace("-", string.Empty);

    /// <summary>
    /// Turns two-factor off, replaces the authenticator key (the old app entry stops working),
    /// removes every recovery code, and rotates the security stamp so every session, remember-me
    /// cookie and remembered two-factor browser is signed out.
    /// </summary>
    private async Task ClearTwoFactorAsync(AppUser user)
    {
        Check(await _users.SetTwoFactorEnabledAsync(user, false), "turn two-factor off");
        Check(await _users.ResetAuthenticatorKeyAsync(user), "replace the authenticator key");
        await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        Check(await _users.UpdateSecurityStampAsync(user), "sign out every session");
    }

    private async Task<AppUser> RequireAsync(string userId) =>
        await _users.FindByIdAsync(userId) ?? throw new InvalidOperationException("The account does not exist.");

    private async Task<AccountInfo?> InfoAsync(AppUser? user) =>
        user == null ? null : new AccountInfo(user.Id, user.Email ?? "", user.EmailConfirmed, await _users.HasPasswordAsync(user), await _users.GetTwoFactorEnabledAsync(user));

    private static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded) throw new InvalidOperationException($"Could not {what}: {string.Join(" ", result.Errors.Select(e => e.Description))}");
    }
}
