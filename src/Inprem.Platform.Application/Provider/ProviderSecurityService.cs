using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Provider;

namespace Inprem.Platform.Application.Provider;

/// <summary>Outcome of a provider security use case. <see cref="Code"/> is stable (tests, audit); <see cref="Message"/> is for people.</summary>
public sealed record ProviderResult(bool Succeeded, string Code, string Message, IReadOnlyList<string> Plan, string? TemporaryPassword = null)
{
    public static ProviderResult Ok(string code, string message, IReadOnlyList<string>? plan = null, string? temporaryPassword = null) => new(true, code, message, plan ?? Array.Empty<string>(), temporaryPassword);

    public static ProviderResult Refused(string code, string message) => new(false, code, message, Array.Empty<string>());
}

/// <param name="LinkBase">Console base URL for the setup link when Links:AdminBaseUrl is not configured (the setup page passes its own).</param>
public sealed record BootstrapRequest(string Email, string Code, string Actor, bool DryRun, string? LinkBase = null);

/// <summary>Level 3 (Replace false) or Level 4 (Replace true, NewEmail, ApprovalReference, two approvers).</summary>
public sealed record BreakGlassRequest(
    string TargetEmail, string Code, IReadOnlyList<string> Approvers, string Actor, bool DryRun,
    bool Replace = false, string? NewEmail = null, string? ApprovalReference = null);

public sealed record InvitationInfo(string Email, InvitationPurpose Purpose, DateTime ExpiresUtc);

public sealed record RecoveryApprovalInfo(Guid Id, RecoveryLevel Level, string TargetEmail, string? NewEmail, string Operator, string Reason,
    RecoveryStatus Status, DateTime ApprovalExpiresUtc, bool AlreadyApprovedByThisLink, DateTime? NotBeforeUtc);

public sealed record OwnerStatus(int ActiveOwners, int EnrolledOwners)
{
    /// <summary>D16: at least two active Provider Owners with two-factor before production.</summary>
    public const int Minimum = 2;

    public bool MeetsMinimum => EnrolledOwners >= Minimum;
}

/// <summary>
/// Bootstrap of the first Provider Owner and recovery Levels 1-4 (design section 5, decisions
/// D13-D16). Shared by the server command (<c>provider ...</c>) and the one-time setup page.
/// Codes and link tokens are compared and stored only as hashes; nothing secret is audited,
/// printed or returned, except the link sent by email.
/// </summary>
public sealed class ProviderSecurityService
{
    public static readonly TimeSpan SetupInvitationLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan RecoveryInvitationLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RecoveryInterval = TimeSpan.FromHours(24);

    /// <summary>Failed bootstrap attempts allowed per hour (setup page and command together).</summary>
    public const int BootstrapFailuresPerHour = 5;

    /// <summary>Failed break-glass attempts that lock the recover command for 24 hours.</summary>
    public const int BreakGlassFailuresBeforeLock = 3;

    public const string BootstrapAttempt = "bootstrap";
    public const string BreakGlassAttempt = "break-glass";

    private readonly IProviderStore _store;
    private readonly IAccountGateway _accounts;
    private readonly IEmailSender _email;
    private readonly IClock _clock;
    private readonly ProviderSecurityOptions _options;

    public ProviderSecurityService(IProviderStore store, IAccountGateway accounts, IEmailSender email, IClock clock, ProviderSecurityOptions options)
    {
        _store = store;
        _accounts = accounts;
        _email = email;
        _clock = clock;
        _options = options;
    }

    // ---------------------------------------------------------------- bootstrap (5.2, D13)

    /// <summary>The setup page exists only while this is true (otherwise 404).</summary>
    public async Task<bool> IsSetupAvailableAsync(CancellationToken ct = default) =>
        !string.IsNullOrWhiteSpace(_options.BootstrapCodeHash) && !await _store.AnyOwnerAsync(_clock.UtcNow, ct).ConfigureAwait(false);

    public async Task<ProviderResult> BootstrapAsync(BootstrapRequest request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var email = request.Email?.Trim() ?? "";

        var refusal = await CheckBootstrapAsync(request, email, now, ct).ConfigureAwait(false);
        if (refusal != null)
        {
            if (!request.DryRun)
            {
                var countsAsFailure = refusal.Code is "refused.wrong-code" or "refused.code-used";
                if (countsAsFailure) await _store.AddAttemptAsync(BootstrapAttempt, false, request.Actor, now, ct).ConfigureAwait(false);
                await AuditAsync(request.Actor, "provider.bootstrap.refused", null, $"{refusal.Code}{(countsAsFailure ? "" : " (not counted)")}", now, ct).ConfigureAwait(false);
                await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            return refusal;
        }

        if (request.DryRun)
        {
            return ProviderResult.Ok("dry-run", "Dry run: every check passed; nothing was changed.", new[]
            {
                $"Create the account {email} without a password.",
                "Make it the first Provider Owner (status Invited).",
                $"Email it a one-time setup link valid for {SetupInvitationLifetime.TotalHours:0} hours.",
                "Mark the bootstrap code as used (bootstrap is then disabled for good).",
                "Write an audit entry and notify the security mailbox.",
            });
        }

        // Claim the code first: if two attempts race, the second fails on the used-code key.
        var codeHash = SecretTokens.Hash(request.Code);
        await _store.MarkCodeUsedAsync(codeHash, BootstrapAttempt, now, ct).ConfigureAwait(false);
        await _store.AddAttemptAsync(BootstrapAttempt, true, request.Actor, now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        // A first owner whose setup link expired unused is invited again rather than created twice.
        var userId = (await _accounts.FindByEmailAsync(email, ct).ConfigureAwait(false))?.Id;
        if (userId == null)
        {
            userId = await _accounts.CreateWithoutPasswordAsync(email, ct).ConfigureAwait(false);
            await _store.AddProviderUserAsync(ProviderUser.Invite(userId, ProviderRole.Owner, now), ct).ConfigureAwait(false);
        }
        var token = await AddInvitationAsync(email, userId, InvitationPurpose.ProviderSetup, SetupInvitationLifetime, request.Actor, now, ct).ConfigureAwait(false);
        await AuditAsync(request.Actor, "provider.bootstrap.succeeded", userId, $"First Provider Owner invited: {email}", now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        try
        {
            await SendInvitationAsync(email, InvitationPurpose.ProviderSetup, LinkFor("AcceptInvitation", token, request.LinkBase), SetupInvitationLifetime, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Nobody can use a link that never arrived: withdraw it and release the code, so setup
            // is open again and the same code works once email is fixed (the account is reused).
            foreach (var pending in await _store.ListPendingInvitationsAsync(userId, now, ct).ConfigureAwait(false)) pending.Revoke(now);
            await _store.ReleaseCodeAsync(codeHash, ct).ConfigureAwait(false);
            await AuditAsync(request.Actor, "provider.bootstrap.email-failed", userId, $"Setup email to {email} could not be sent ({e.GetType().Name}); undone, code released.", now, ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            return ProviderResult.Refused("refused.email-failed",
                $"The setup email to {email} could not be sent, so nothing was kept. Fix the email settings, then try again with the same code.");
        }

        await NotifyAsync("First Provider Owner invited",
            $"The bootstrap code was used by {request.Actor} to invite {email} as the first Provider Owner. Bootstrap is now disabled. Delete the bootstrap code from the secret store.", ct).ConfigureAwait(false);

        return ProviderResult.Ok("bootstrap.done", $"Done. A setup link was emailed to {email} (valid {SetupInvitationLifetime.TotalHours:0} hours). The bootstrap code is now used; delete it from the secret store.");
    }

    private async Task<ProviderResult?> CheckBootstrapAsync(BootstrapRequest request, string email, DateTime now, CancellationToken ct)
    {
        if (await _store.CountFailuresSinceAsync(BootstrapAttempt, now - TimeSpan.FromHours(1), ct).ConfigureAwait(false) >= BootstrapFailuresPerHour)
            return ProviderResult.Refused("refused.rate-limited", "Too many failed attempts. Try again in an hour.");
        if (string.IsNullOrWhiteSpace(_options.BootstrapCodeHash))
            return ProviderResult.Refused("refused.not-configured", "Bootstrap is not configured on this server (Bootstrap:CodeHash).");
        if (await _store.AnyOwnerAsync(now, ct).ConfigureAwait(false))
            return ProviderResult.Refused("refused.owner-exists", "A Provider Owner already exists; bootstrap is disabled.");
        if (!IsValidEmail(email))
            return ProviderResult.Refused("refused.invalid-email", "Enter a valid email address.");
        if (string.IsNullOrWhiteSpace(request.Code) || !SecretTokens.Matches(request.Code, _options.BootstrapCodeHash))
            return ProviderResult.Refused("refused.wrong-code", "The bootstrap code is not valid.");
        if (await _store.IsCodeUsedAsync(SecretTokens.Hash(request.Code), ct).ConfigureAwait(false))
            return ProviderResult.Refused("refused.code-used", "This bootstrap code has already been used.");
        if (await _accounts.FindByEmailAsync(email, ct).ConfigureAwait(false) is { } existing && !await IsUnusedOwnerInvitationAsync(existing, ct).ConfigureAwait(false))
            return ProviderResult.Refused("refused.account-exists", "An account with this email already exists. Use a separate address for the Provider Owner.");
        if (LinkBaseOrNull(request.LinkBase) == null)
            return ProviderResult.Refused("refused.no-link-base", "Links:AdminBaseUrl is not configured, so no setup link can be built.");
        return null;
    }

    /// <summary>An Invited owner account that never set a password (its setup link expired: bootstrap may run again).</summary>
    private async Task<bool> IsUnusedOwnerInvitationAsync(AccountInfo account, CancellationToken ct) =>
        !account.HasPassword && await _store.FindProviderUserAsync(account.Id, ct).ConfigureAwait(false) is { Role: ProviderRole.Owner, Status: ProviderUserStatus.Invited };

    // ---------------------------------------------------------------- invitations

    public async Task<InvitationInfo?> FindInvitationAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var invitation = await _store.FindInvitationByTokenHashAsync(SecretTokens.Hash(token), ct).ConfigureAwait(false);
        return invitation != null && invitation.IsUsable(_clock.UtcNow) ? new InvitationInfo(invitation.Email, invitation.Purpose, invitation.ExpiresUtc) : null;
    }

    /// <summary>Sets the person's password from a setup or recovery link. Returns the account id when it worked.</summary>
    public async Task<(ProviderResult Result, string? UserId)> AcceptInvitationAsync(string token, string password, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var invitation = string.IsNullOrWhiteSpace(token) ? null : await _store.FindInvitationByTokenHashAsync(SecretTokens.Hash(token), ct).ConfigureAwait(false);
        if (invitation == null || !invitation.IsUsable(now))
            return (ProviderResult.Refused("refused.invitation-invalid", "This link is not valid any more. Ask for a new one."), null);

        var provider = await _store.FindProviderUserAsync(invitation.UserId, ct).ConfigureAwait(false);
        var account = await _accounts.FindByIdAsync(invitation.UserId, ct).ConfigureAwait(false);
        if (provider == null || provider.Status == ProviderUserStatus.Disabled || account == null)
            return (ProviderResult.Refused("refused.invitation-invalid", "This link is not valid any more. Ask for a new one."), null);
        if (account.HasPassword)
            return (ProviderResult.Refused("refused.password-already-set", "This account already has a password. Sign in instead."), null);

        var errors = await _accounts.SetPasswordAsync(account.Id, password, ct).ConfigureAwait(false);
        if (errors.Count > 0) return (ProviderResult.Refused("refused.password", string.Join(" ", errors)), null);

        invitation.Consume(now);
        provider.Activate();
        await AuditAsync($"provider:{account.Id}", "provider.invitation.accepted", account.Id, invitation.Purpose.ToString(), now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        return (ProviderResult.Ok("invitation.accepted", "Password set. Now set up two-factor sign-in."), account.Id);
    }

    // ---------------------------------------------------------------- console access

    /// <summary>The provider user behind an account, when it may use the console (not Disabled).</summary>
    public async Task<ProviderUser?> FindUsableProviderUserAsync(string userId, CancellationToken ct = default)
    {
        var user = await _store.FindProviderUserAsync(userId, ct).ConfigureAwait(false);
        return user is { Status: not ProviderUserStatus.Disabled } ? user : null;
    }

    public async Task<OwnerStatus> GetOwnerStatusAsync(CancellationToken ct = default)
    {
        var owners = await _store.ListActiveOwnersAsync(ct).ConfigureAwait(false);
        return new OwnerStatus(owners.Count, owners.Count(o => o.TwoFactorEnabled));
    }

    /// <summary>Records completed two-factor enrolment and activates a newly configured provider.</summary>
    public async Task RecordTwoFactorEnrolledAsync(string userId, CancellationToken ct = default)
    {
        var provider = await _store.FindProviderUserAsync(userId, ct).ConfigureAwait(false);
        if (provider is { Status: ProviderUserStatus.Invited })
            provider.Activate();

        await AuditAsync($"provider:{userId}", "provider.two-factor.enrolled", userId, null, _clock.UtcNow, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- Level 1 (self-service)

    /// <summary>Someone signed in with one of their recovery codes and must re-enrol two-factor.</summary>
    public async Task RecordSelfServiceRecoveryAsync(string userId, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var account = await _accounts.FindByIdAsync(userId, ct).ConfigureAwait(false);
        await AuditAsync($"provider:{userId}", "provider.recovery.level1", userId, "Signed in with a recovery code; two-factor to be re-enrolled.", now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        if (account != null)
        {
            await _email.SendAsync(account.Email, "A recovery code was used on your provider account",
                Html("One of your recovery codes was just used to sign in to the Provider Console. You will set up two-factor again and get new recovery codes.",
                     "If this was not you, tell another Provider Owner and the security team at once."), ct).ConfigureAwait(false);
            await NotifyAsync("Recovery Level 1 (recovery code used)", $"{account.Email} signed in with a recovery code and is re-enrolling two-factor.", ct, exceptEmail: account.Email).ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- Level 2 (peer reset)

    /// <summary>
    /// Another active Provider Owner resets the person's two-factor (console UI in Phase 1). The
    /// acting owner re-authenticates with a current authenticator code and gives a reason.
    /// </summary>
    public async Task<ProviderResult> PeerResetAsync(string actorUserId, string targetUserId, string reason, string actorCode, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal))
            return ProviderResult.Refused("refused.self", "Owners cannot reset their own two-factor. Use a recovery code or ask another owner.");
        if (string.IsNullOrWhiteSpace(reason))
            return ProviderResult.Refused("refused.no-reason", "Give a reason.");

        var actor = await _store.FindProviderUserAsync(actorUserId, ct).ConfigureAwait(false);
        var actorAccount = await _accounts.FindByIdAsync(actorUserId, ct).ConfigureAwait(false);
        if (actor is not { IsActiveOwner: true } || actorAccount is not { TwoFactorEnabled: true })
            return ProviderResult.Refused("refused.not-owner", "Only an active Provider Owner with two-factor can reset another owner.");

        var target = await _store.FindProviderUserAsync(targetUserId, ct).ConfigureAwait(false);
        var targetAccount = await _accounts.FindByIdAsync(targetUserId, ct).ConfigureAwait(false);
        if (target == null || target.Status == ProviderUserStatus.Disabled || targetAccount == null)
            return ProviderResult.Refused("refused.no-target", "That person is not an active provider user.");

        if (string.IsNullOrWhiteSpace(actorCode) || !await _accounts.VerifyTwoFactorCodeAsync(actorUserId, actorCode, ct).ConfigureAwait(false))
        {
            await AuditAsync($"provider:{actorUserId}", "provider.recovery.level2.refused", targetUserId, "re-authentication failed", now, ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            return ProviderResult.Refused("refused.reauthentication", "Your authenticator code is not valid.");
        }

        if (await _store.AnyRecoverySinceAsync(targetUserId, now - RecoveryInterval, ct).ConfigureAwait(false))
            return ProviderResult.Refused("refused.rate-limited", "This account was already recovered in the last 24 hours.");

        await _accounts.ResetTwoFactorAsync(targetUserId, ct).ConfigureAwait(false);
        await _store.AddRecoveryAsync(RecoveryRecord.Immediate(RecoveryLevel.PeerReset, targetUserId, targetAccount.Email, $"provider:{actorUserId}", reason.Trim(), now), ct).ConfigureAwait(false);
        await AuditAsync($"provider:{actorUserId}", "provider.recovery.level2", targetUserId, $"Two-factor of {targetAccount.Email} reset by {actorAccount.Email}. Reason: {reason.Trim()}", now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        await NotifyAsync("Recovery Level 2 (peer reset)",
            $"{actorAccount.Email} reset the two-factor sign-in of {targetAccount.Email}. All their sessions were signed out; they set up two-factor again at next sign-in. Reason: {reason.Trim()}", ct).ConfigureAwait(false);
        return ProviderResult.Ok("level2.done", $"Two-factor of {targetAccount.Email} was reset; they set it up again at next sign-in.");
    }

    // ---------------------------------------------------------------- Levels 3 and 4 (break-glass)

    public async Task<ProviderResult> StartBreakGlassAsync(BreakGlassRequest request, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var level = request.Replace ? RecoveryLevel.BreakGlassReplace : RecoveryLevel.BreakGlassReissue;
        var targetEmail = request.TargetEmail?.Trim() ?? "";
        var approvers = request.Approvers.Select(a => a.Trim()).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var locked = await _store.CountFailuresSinceAsync(BreakGlassAttempt, now - RecoveryInterval, ct).ConfigureAwait(false) >= BreakGlassFailuresBeforeLock;
        if (locked) return await RefuseLockedBreakGlassAsync(request, now, ct).ConfigureAwait(false);

        var (target, targetProvider, refusal) = await ValidateBreakGlassTargetAsync(
            request, targetEmail, approvers, now, ct).ConfigureAwait(false);
        var failure = refusal == null
            ? await ValidateBreakGlassSecretAsync(request, approvers, ct).ConfigureAwait(false)
            : null;
        if (refusal != null || failure != null)
            return await RefuseBreakGlassAsync(request, target, level, refusal ?? failure!, now, failure != null, ct).ConfigureAwait(false);
        if (request.DryRun)
            return BuildBreakGlassDryRun(request, targetEmail, approvers);
        return await CreateBreakGlassAsync(request, target!, approvers, level, now, ct).ConfigureAwait(false);
    }

    private async Task<(AccountInfo? Target, ProviderUser? Provider, ProviderResult? Refusal)> ValidateBreakGlassTargetAsync(
        BreakGlassRequest request, string targetEmail, IReadOnlyList<string> approvers, DateTime now, CancellationToken ct)
    {
        var refusal = ValidateBreakGlassRequest(request, approvers);
        if (refusal != null)
            return (null, null, refusal);

        var target = await _accounts.FindByEmailAsync(targetEmail, ct).ConfigureAwait(false);
        ProviderUser? provider = null;
        if (target != null)
            provider = await _store.FindProviderUserAsync(target.Id, ct).ConfigureAwait(false);
        if (target == null || provider is not { Role: ProviderRole.Owner } || provider.Status == ProviderUserStatus.Disabled)
            return (target, provider, ProviderResult.Refused("refused.no-target", "That email does not belong to a Provider Owner."));
        if (!request.Replace && !target.EmailConfirmed)
            return (target, provider, ProviderResult.Refused("refused.email-not-verified", "That owner's email was never verified; Level 3 sends links only to a verified email. Use Level 4."));
        if (approvers.Any(a => string.Equals(a, targetEmail, StringComparison.OrdinalIgnoreCase) || string.Equals(a, request.NewEmail?.Trim(), StringComparison.OrdinalIgnoreCase)))
            return (target, provider, ProviderResult.Refused("refused.approvers", "An approver cannot be the owner being recovered or the new owner."));
        if (request.Replace && await _accounts.FindByEmailAsync(request.NewEmail!.Trim(), ct).ConfigureAwait(false) != null)
            return (target, provider, ProviderResult.Refused("refused.account-exists", "An account with the new email already exists. Use a separate address for the new owner."));
        if (await _store.AnyRecoverySinceAsync(target.Id, now - RecoveryInterval, ct).ConfigureAwait(false))
            return (target, provider, ProviderResult.Refused("refused.rate-limited", "This account was already recovered in the last 24 hours."));

        return (target, provider, null);
    }

    private ProviderResult? ValidateBreakGlassRequest(BreakGlassRequest request, IReadOnlyList<string> approvers)
    {
        if (string.IsNullOrWhiteSpace(_options.BreakGlassCodeHash))
            return ProviderResult.Refused("refused.not-configured", "No break-glass code is configured on this server (Security:BreakGlassCodeHash).");
        if (LinkBaseOrNull(null) == null)
            return ProviderResult.Refused("refused.no-link-base", "Links:AdminBaseUrl is not configured, so no approval link can be built.");
        var requiredApprovers = request.Replace ? 2 : 1;
        if (approvers.Count != requiredApprovers)
            return ProviderResult.Refused("refused.approvers", GetApproverMessage(request.Replace));
        if (request.Replace && string.IsNullOrWhiteSpace(request.ApprovalReference))
            return ProviderResult.Refused("refused.no-approval-reference", "Level 4 needs the reference of the written approval (--approval-ref).");
        if (request.Replace && !IsValidEmail(request.NewEmail?.Trim() ?? ""))
            return ProviderResult.Refused("refused.invalid-email", "Give the new owner's email (--new-email).");
        return null;
    }

    private static string GetApproverMessage(bool replace)
    {
        if (replace)
            return "Level 4 needs two different approvers (--approver twice).";
        return "Name one approver (--approver).";
    }

    private async Task<ProviderResult?> ValidateBreakGlassSecretAsync(BreakGlassRequest request, IReadOnlyList<string> approvers, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || !SecretTokens.Matches(request.Code, _options.BreakGlassCodeHash))
            return ProviderResult.Refused("refused.wrong-code", "The break-glass code is not valid.");
        if (await _store.IsCodeUsedAsync(SecretTokens.Hash(request.Code), ct).ConfigureAwait(false))
            return ProviderResult.Refused("refused.code-used", "This break-glass code has already been used. A new one must be put in the secret store.");
        if (approvers.Any(a => !_options.RecoveryApprovers.Contains(a, StringComparer.OrdinalIgnoreCase)))
            return ProviderResult.Refused("refused.unknown-approver", "Every approver must be listed in Security:RecoveryApprovers.");
        return null;
    }

    private async Task<ProviderResult> RefuseLockedBreakGlassAsync(BreakGlassRequest request, DateTime now, CancellationToken ct)
    {
        if (!request.DryRun)
        {
            await AuditAsync(request.Actor, "provider.recovery.refused", null, "refused.locked", now, ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        return ProviderResult.Refused("refused.locked", "The recover command is locked for 24 hours after repeated failures.");
    }

    private async Task<ProviderResult> RefuseBreakGlassAsync(BreakGlassRequest request, AccountInfo? target,
        RecoveryLevel level, ProviderResult refusal, DateTime now, bool countsAsFailure, CancellationToken ct)
    {
        if (!request.DryRun)
        {
            if (countsAsFailure)
                await _store.AddAttemptAsync(BreakGlassAttempt, false, request.Actor, now, ct).ConfigureAwait(false);
            await AuditAsync(request.Actor, "provider.recovery.refused", target?.Id,
                $"Level {(int)level}: {refusal.Code}", now, ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            if (countsAsFailure && await _store.CountFailuresSinceAsync(BreakGlassAttempt, now - RecoveryInterval, ct).ConfigureAwait(false) >= BreakGlassFailuresBeforeLock)
                await NotifyAsync("Recover command locked",
                    $"{BreakGlassFailuresBeforeLock} failed break-glass attempts in 24 hours (last by {request.Actor}). The recover command is locked for 24 hours.", ct).ConfigureAwait(false);
        }
        return refusal;
    }

    private static ProviderResult BuildBreakGlassDryRun(BreakGlassRequest request, string targetEmail, IReadOnlyList<string> approvers)
    {
        var plan = new List<string> { "Mark the break-glass code as used (a new one must then be put in the secret store)." };
        plan.AddRange(approvers.Select(a => $"Email an approval link (valid 1 hour) to {a}."));
        plan.Add("Notify all Provider Owners and the security mailbox.");
        if (request.Replace)
        {
            plan.Add($"After both approvals and a 24-hour delay (unless both approvers waive it): disable {targetEmail}, sign out all its sessions, withdraw its pending links.");
            plan.Add($"Create {request.NewEmail?.Trim()} without a password as a Provider Owner and email it a setup link valid 24 hours.");
        }
        else
        {
            plan.Add($"After approval: remove the password and two-factor of {targetEmail}, sign out all its sessions, withdraw its pending links.");
            plan.Add($"Email a 1-hour link to set a new password to {targetEmail} (its existing verified email).");
        }
        return ProviderResult.Ok("dry-run", "Dry run: every check passed; nothing was changed.", plan);
    }

    private async Task<ProviderResult> CreateBreakGlassAsync(BreakGlassRequest request, AccountInfo target,
        IReadOnlyList<string> approvers, RecoveryLevel level, DateTime now, CancellationToken ct)
    {
        var newEmail = request.NewEmail?.Trim();
        await _store.MarkCodeUsedAsync(SecretTokens.Hash(request.Code), BreakGlassAttempt, now, ct).ConfigureAwait(false);
        await _store.AddAttemptAsync(BreakGlassAttempt, true, request.Actor, now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        var tokens = approvers.Select(_ => SecretTokens.NewToken()).ToList();
        var record = CreateBreakGlassRecord(request, target, newEmail!, approvers, tokens, now);
        await _store.AddRecoveryAsync(record, ct).ConfigureAwait(false);
        await AuditAsync(request.Actor, $"provider.recovery.level{(int)level}.started", target.Id,
            $"Recovery {record.Id} for {target.Email}{(request.Replace ? $", new owner {newEmail}, approval {request.ApprovalReference!.Trim()}" : "")}; approvers {string.Join(", ", approvers)}", now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        for (var i = 0; i < approvers.Count; i++)
            await _email.SendAsync(approvers[i], $"Approve provider recovery (Level {(int)level})",
                Html($"{request.Actor} started a Level {(int)level} recovery of the Provider Owner {target.Email}{(request.Replace ? $", to be replaced by {newEmail} (written approval: {request.ApprovalReference!.Trim()})" : "")}.",
                    "Open this link only if you agree. It is valid for 1 hour:", LinkFor("ApproveRecovery", tokens[i], null)), ct).ConfigureAwait(false);
        await NotifyAsync($"Recovery Level {(int)level} started",
            $"{request.Actor} started a Level {(int)level} recovery of {target.Email}{(request.Replace ? $" (replacement by {newEmail})" : "")}. Waiting for approval by {string.Join(" and ", approvers)}.", ct).ConfigureAwait(false);
        return ProviderResult.Ok("recovery.started",
            $"Recovery {record.Id} started. Approval link(s) emailed to {string.Join(" and ", approvers)} (valid 1 hour). The break-glass code is now used; put a new one in the secret store.");
    }

    private static RecoveryRecord CreateBreakGlassRecord(BreakGlassRequest request, AccountInfo target, string newEmail,
        IReadOnlyList<string> approvers, IReadOnlyList<string> tokens, DateTime now)
    {
        if (request.Replace)
            return RecoveryRecord.BreakGlassReplace(target.Id, target.Email, newEmail, request.Actor, request.ApprovalReference!.Trim(),
                approvers[0], SecretTokens.Hash(tokens[0]), approvers[1], SecretTokens.Hash(tokens[1]), now);
        return RecoveryRecord.BreakGlassReissue(target.Id, target.Email, request.Actor, approvers[0], SecretTokens.Hash(tokens[0]), now);
    }

    public async Task<RecoveryApprovalInfo?> FindRecoveryForApprovalAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var hash = SecretTokens.Hash(token);
        var record = await _store.FindRecoveryByApprovalHashAsync(hash, ct).ConfigureAwait(false);
        if (record == null) return null;
        var approver = record.ApproverFor(hash);
        var approved = approver == 1 ? record.FirstApprovedUtc != null : record.SecondApprovedUtc != null;
        return new RecoveryApprovalInfo(record.Id, record.Level, record.TargetEmail, record.NewEmail, record.Operator, record.Reason, record.Status,
            record.ApprovalExpiresUtc, approved, record.NotBeforeUtc);
    }

    /// <summary>An approver confirms by the emailed link. Level 3 then runs at once; Level 4 when both approved (and after the delay unless both waived it).</summary>
    public async Task<ProviderResult> ApproveRecoveryAsync(string token, bool waiveDelay, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var hash = string.IsNullOrWhiteSpace(token) ? "" : SecretTokens.Hash(token);
        var record = hash.Length == 0 ? null : await _store.FindRecoveryByApprovalHashAsync(hash, ct).ConfigureAwait(false);
        var approver = record?.ApproverFor(hash) ?? 0;
        if (record == null || approver == 0)
            return ProviderResult.Refused("refused.approval-invalid", "This approval link is not valid.");
        if (record.Status != RecoveryStatus.PendingApproval)
            return ProviderResult.Refused("refused.approval-closed", $"This recovery is {record.Status.ToString().ToLowerInvariant()}; nothing to approve.");

        var approverEmail = approver == 1 ? record.FirstApproverEmail! : record.SecondApproverEmail!;
        var complete = record.Approve(approver, waiveDelay && record.Level == RecoveryLevel.BreakGlassReplace, now);
        if (record.Status == RecoveryStatus.Expired)
        {
            await AuditAsync($"approver:{approverEmail}", "provider.recovery.expired", record.TargetUserId, $"Recovery {record.Id}: approval came after the 1-hour window.", now, ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
            await NotifyAsync("Recovery expired", $"The recovery of {record.TargetEmail} expired: it was not approved within 1 hour. Nothing was changed.", ct).ConfigureAwait(false);
            return ProviderResult.Refused("refused.approval-expired", "This approval link has expired (1 hour). Nothing was changed; the operator must start again with a new break-glass code.");
        }

        var delayNote = "";
        if (record.Level == RecoveryLevel.BreakGlassReplace)
            delayNote = waiveDelay ? " (waives the 24-hour delay)" : " (keeps the 24-hour delay)";
        await AuditAsync($"approver:{approverEmail}", $"provider.recovery.level{(int)record.Level}.approved", record.TargetUserId,
            $"Recovery {record.Id} approved by {approverEmail}{delayNote}", now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        if (!complete) return ProviderResult.Ok("recovery.approved-partly", "Thank you. Waiting for the other approver.");
        if (!record.CanComplete(now))
        {
            await NotifyAsync("Recovery approved; 24-hour delay", $"The replacement of {record.TargetEmail} by {record.NewEmail} is approved and runs after {record.NotBeforeUtc:yyyy-MM-dd HH:mm} UTC (provider recover --complete).", ct).ConfigureAwait(false);
            return ProviderResult.Ok("recovery.approved-delayed", $"Approved. The replacement can be completed after {record.NotBeforeUtc:yyyy-MM-dd HH:mm} UTC.");
        }

        return await ExecuteAsync(record, $"approver:{approverEmail}", now, ct).ConfigureAwait(false);
    }

    /// <summary>Completes an approved Level 4 recovery after its delay (server command <c>provider recover --complete</c>).</summary>
    public async Task<ProviderResult> CompleteRecoveryAsync(Guid recoveryId, string actor, bool dryRun, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var record = await _store.FindRecoveryAsync(recoveryId, ct).ConfigureAwait(false);
        if (record == null) return ProviderResult.Refused("refused.no-recovery", "No such recovery.");
        if (record.Status != RecoveryStatus.Approved) return ProviderResult.Refused("refused.not-approved", $"This recovery is {record.Status.ToString().ToLowerInvariant()}, not approved.");
        if (!record.CanComplete(now)) return ProviderResult.Refused("refused.too-early", $"This recovery can be completed after {record.NotBeforeUtc:yyyy-MM-dd HH:mm} UTC.");
        if (dryRun) return ProviderResult.Ok("dry-run", "Dry run: the recovery can be completed now; nothing was changed.");
        return await ExecuteAsync(record, actor, now, ct).ConfigureAwait(false);
    }

    private async Task<ProviderResult> ExecuteAsync(RecoveryRecord record, string actor, DateTime now, CancellationToken ct)
    {
        foreach (var pending in await _store.ListPendingInvitationsAsync(record.TargetUserId, now, ct).ConfigureAwait(false)) pending.Revoke(now);

        string inviteEmail;
        string inviteUserId;
        InvitationPurpose purpose;
        TimeSpan lifetime;
        if (record.Level == RecoveryLevel.BreakGlassReplace)
        {
            if (await _accounts.FindByEmailAsync(record.NewEmail!, ct).ConfigureAwait(false) != null)
                return ProviderResult.Refused("refused.account-exists", "An account with the new email was created meanwhile; nothing was changed.");
            await _accounts.DisableAsync(record.TargetUserId, ct).ConfigureAwait(false);
            (await _store.FindProviderUserAsync(record.TargetUserId, ct).ConfigureAwait(false))?.Disable();
            inviteUserId = await _accounts.CreateWithoutPasswordAsync(record.NewEmail!, ct).ConfigureAwait(false);
            await _store.AddProviderUserAsync(ProviderUser.Invite(inviteUserId, ProviderRole.Owner, now), ct).ConfigureAwait(false);
            (inviteEmail, purpose, lifetime) = (record.NewEmail!, InvitationPurpose.ProviderSetup, SetupInvitationLifetime);
        }
        else
        {
            await _accounts.ResetPasswordAndTwoFactorAsync(record.TargetUserId, ct).ConfigureAwait(false);
            (inviteEmail, inviteUserId, purpose, lifetime) = (record.TargetEmail, record.TargetUserId, InvitationPurpose.ProviderRecovery, RecoveryInvitationLifetime);
        }

        var token = await AddInvitationAsync(inviteEmail, inviteUserId, purpose, lifetime, actor, now, ct).ConfigureAwait(false);
        record.Complete(now);
        var summary = record.Level == RecoveryLevel.BreakGlassReplace
            ? $"{record.TargetEmail} was disabled and signed out everywhere; {record.NewEmail} was invited as a Provider Owner."
            : $"The password and two-factor of {record.TargetEmail} were removed and every session signed out; a 1-hour link to set a new password was sent to that address.";
        await AuditAsync(actor, $"provider.recovery.level{(int)record.Level}.completed", record.TargetUserId, $"Recovery {record.Id}: {summary}", now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        await SendInvitationAsync(inviteEmail, purpose, LinkFor("AcceptInvitation", token, null), lifetime, ct).ConfigureAwait(false);
        await NotifyAsync($"Recovery Level {(int)record.Level} completed", $"{summary} Started by {record.Operator}. Complete the recovery report within 24 hours.", ct).ConfigureAwait(false);
        return ProviderResult.Ok("recovery.completed", summary);
    }

    // ---------------------------------------------------------------- provider users (Phase 1 step 1.4)

    public Task<IReadOnlyList<ProviderUserRow>> ListProviderUsersAsync(CancellationToken ct = default) => _store.ListProviderUsersAsync(ct);

    /// <summary>
    /// Invites a provider user (any role): a new account without a password, status Invited, and a
    /// 24-hour setup link (password, then two-factor). Provider accounts are separate: an email
    /// that already has an account (e.g. an organization admin) is refused.
    /// </summary>
    public async Task<ProviderResult> InviteProviderUserAsync(string actorUserId, string email, ProviderRole role, string? linkBase, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var address = email?.Trim() ?? "";
        if (await RequireActiveOwnerAsync(actorUserId, ct).ConfigureAwait(false) is { } refused) return refused;
        if (!IsValidEmail(address)) return ProviderResult.Refused("refused.invalid-email", "Enter a valid email address.");
        if (await _accounts.FindByEmailAsync(address, ct).ConfigureAwait(false) is { } existing)
        {
            return await _store.FindProviderUserAsync(existing.Id, ct).ConfigureAwait(false) != null
                ? ProviderResult.Refused("refused.already-provider", "That person is already a provider user.")
                : ProviderResult.Refused("refused.account-exists", "That email already has an account (e.g. an organization admin). Provider accounts are separate: use another address.");
        }
        var temporaryPassword = CreateTemporaryPassword();
        var userId = await _accounts.CreateWithPasswordAsync(address, temporaryPassword, ct).ConfigureAwait(false);
        await _store.AddProviderUserAsync(ProviderUser.Invite(userId, role, now), ct).ConfigureAwait(false);
        await AuditAsync($"provider:{actorUserId}", "provider.user.invited", userId, $"{address} invited as {role}", now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);

        return ProviderResult.Ok("user.invited", $"{address} was created as {role}. Give the temporary password to the provider securely.", temporaryPassword: temporaryPassword);
    }

    /// <summary>A new setup link for someone who has not finished setting up (the old link stops working).</summary>
    public async Task<ProviderResult> ResendProviderInvitationAsync(string actorUserId, string userId, string? linkBase, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        if (await RequireActiveOwnerAsync(actorUserId, ct).ConfigureAwait(false) is { } refused) return refused;
        var user = await _store.FindProviderUserAsync(userId, ct).ConfigureAwait(false);
        var account = await _accounts.FindByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is not { Status: ProviderUserStatus.Invited } || account == null || account.HasPassword)
            return ProviderResult.Refused("refused.not-invited", "Only someone who has not set up their account yet can get a new setup link.");
        if (LinkBaseOrNull(linkBase) == null) return ProviderResult.Refused("refused.no-link-base", "Links:AdminBaseUrl is not configured, so no setup link can be built.");

        foreach (var pending in await _store.ListPendingInvitationsAsync(userId, now, ct).ConfigureAwait(false)) pending.Revoke(now);
        var token = await AddInvitationAsync(account.Email, userId, InvitationPurpose.ProviderSetup, SetupInvitationLifetime, $"provider:{actorUserId}", now, ct).ConfigureAwait(false);
        await AuditAsync($"provider:{actorUserId}", "provider.user.invitation-resent", userId, account.Email, now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        await SendInvitationAsync(account.Email, InvitationPurpose.ProviderSetup, LinkFor("AcceptInvitation", token, linkBase), SetupInvitationLifetime, ct).ConfigureAwait(false);
        return ProviderResult.Ok("user.invitation-resent", $"A new setup link was sent to {account.Email}.");
    }

    public async Task<ProviderResult> ChangeProviderRoleAsync(string actorUserId, string userId, ProviderRole role, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        if (await RequireActiveOwnerAsync(actorUserId, ct).ConfigureAwait(false) is { } refused) return refused;
        if (actorUserId == userId) return ProviderResult.Refused("refused.self", "You cannot change your own role. Ask another owner.");
        var user = await _store.FindProviderUserAsync(userId, ct).ConfigureAwait(false);
        if (user == null || user.Status == ProviderUserStatus.Disabled) return ProviderResult.Refused("refused.no-target", "That person is not an active provider user.");
        if (user.Role == role) return ProviderResult.Refused("refused.no-change", $"That person is already {role}.");
        if (user.IsActiveOwner && role != ProviderRole.Owner && await IsLastActiveOwnerAsync(userId, ct).ConfigureAwait(false))
            return ProviderResult.Refused("refused.last-owner", "This is the last active owner: make someone else owner first.");

        var before = user.Role;
        user.ChangeRole(role);
        var email = await EmailOfAsync(userId, ct).ConfigureAwait(false);
        await AuditAsync($"provider:{actorUserId}", "provider.user.role-changed", userId, $"{email}: {before} -> {role}", now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        await NotifyAsync("Provider role changed", $"{await EmailOfAsync(actorUserId, ct).ConfigureAwait(false)} changed {email} from {before} to {role}.", ct).ConfigureAwait(false);
        return ProviderResult.Ok("user.role-changed", $"{email} is now {role}.");
    }

    /// <summary>Disables a provider user: no more sign-in, every session ends, pending links are withdrawn.</summary>
    public async Task<ProviderResult> DisableProviderUserAsync(string actorUserId, string userId, string reason, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        if (await RequireActiveOwnerAsync(actorUserId, ct).ConfigureAwait(false) is { } refused) return refused;
        if (actorUserId == userId) return ProviderResult.Refused("refused.self", "You cannot disable yourself. Ask another owner.");
        if (string.IsNullOrWhiteSpace(reason)) return ProviderResult.Refused("refused.no-reason", "Give a reason.");
        var user = await _store.FindProviderUserAsync(userId, ct).ConfigureAwait(false);
        if (user == null || user.Status == ProviderUserStatus.Disabled) return ProviderResult.Refused("refused.no-target", "That person is not an active provider user.");
        if (user.IsActiveOwner && await IsLastActiveOwnerAsync(userId, ct).ConfigureAwait(false))
            return ProviderResult.Refused("refused.last-owner", "This is the last active owner and cannot be disabled.");

        user.Disable();
        foreach (var pending in await _store.ListPendingInvitationsAsync(userId, now, ct).ConfigureAwait(false)) pending.Revoke(now);
        await _accounts.DisableAsync(userId, ct).ConfigureAwait(false);
        var email = await EmailOfAsync(userId, ct).ConfigureAwait(false);
        await AuditAsync($"provider:{actorUserId}", "provider.user.disabled", userId, $"{email}. Reason: {reason.Trim()}", now, ct).ConfigureAwait(false);
        await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        await NotifyAsync("Provider user disabled", $"{await EmailOfAsync(actorUserId, ct).ConfigureAwait(false)} disabled {email}. Reason: {reason.Trim()}", ct).ConfigureAwait(false);
        return ProviderResult.Ok("user.disabled", $"{email} was disabled and signed out everywhere.");
    }

    private async Task<ProviderResult?> RequireActiveOwnerAsync(string actorUserId, CancellationToken ct)
    {
        var provider = await _store.FindProviderUserAsync(actorUserId, ct).ConfigureAwait(false);
        if (provider is { IsActiveOwner: true }) return null;

        return ProviderResult.Refused("refused.not-owner", "Only an active Provider Owner can manage provider users.");
    }

    // A safety net: the acting owner can never act on themselves and stays an active owner, so this
    // never refuses today. It keeps the rule true if other paths (e.g. support tooling) are added.
    private async Task<bool> IsLastActiveOwnerAsync(string userId, CancellationToken ct) =>
        (await _store.ListProviderUsersAsync(ct).ConfigureAwait(false)).Count(u => u.Role == ProviderRole.Owner && u.Status == ProviderUserStatus.Active && u.UserId != userId) == 0;

    private async Task<string> EmailOfAsync(string userId, CancellationToken ct) =>
        (await _accounts.FindByIdAsync(userId, ct).ConfigureAwait(false))?.Email ?? userId;

    private static string CreateTemporaryPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)) + "aA1!";

    // ---------------------------------------------------------------- helpers

    private async Task<string> AddInvitationAsync(string email, string userId, InvitationPurpose purpose, TimeSpan lifetime, string createdBy, DateTime now, CancellationToken ct)
    {
        var token = SecretTokens.NewToken();
        await _store.AddInvitationAsync(Invitation.Create(email, userId, purpose, SecretTokens.Hash(token), lifetime, createdBy, now), ct).ConfigureAwait(false);
        return token;
    }

    private Task SendInvitationAsync(string email, InvitationPurpose purpose, string link, TimeSpan lifetime, CancellationToken ct) =>
        purpose == InvitationPurpose.ProviderSetup
            ? _email.SendAsync(email, "Set up your provider account",
                Html("You have been invited to the Inprem Provider Console. Set your password and two-factor sign-in with this link.",
                     $"The link works once and is valid for {lifetime.TotalHours:0} hours:", link), ct)
            : _email.SendAsync(email, "Set a new password for your provider account",
                Html("A recovery of your Provider Console account was approved. Your password and two-factor sign-in were removed and every session signed out.",
                     $"Set a new password with this link (works once, valid {lifetime.TotalHours:0} hour):", link), ct);

    /// <summary>Notification to every active Provider Owner and the security mailbox.</summary>
    private async Task NotifyAsync(string subject, string text, CancellationToken ct, string? exceptEmail = null)
    {
        var recipients = (await _store.ListActiveOwnersAsync(ct).ConfigureAwait(false)).Select(o => o.Email).ToList();
        if (!string.IsNullOrWhiteSpace(_options.SecurityMailbox)) recipients.Add(_options.SecurityMailbox.Trim());
        var failed = new List<string>();
        foreach (var to in recipients.Distinct(StringComparer.OrdinalIgnoreCase).Where(r => !string.Equals(r, exceptEmail, StringComparison.OrdinalIgnoreCase)))
        {
            // The action itself is done (and audited); a notification that cannot be sent must not
            // turn it into an error. The failure is audited instead.
            try
            {
                await _email.SendAsync(to, $"[Inprem security] {subject}", Html(text), ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failed.Add($"{to} ({e.GetType().Name})");
            }
        }

        if (failed.Count > 0)
        {
            await AuditAsync("system", "provider.notification.failed", null, $"\"{subject}\" not delivered to: {string.Join(", ", failed)}", _clock.UtcNow, ct).ConfigureAwait(false);
            await _store.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private Task AuditAsync(string actor, string action, string? target, string? details, DateTime now, CancellationToken ct) =>
        _store.AddAuditAsync(AuditEntry.Record(actor, action, target, details, now), ct);

    private string? LinkBaseOrNull(string? linkBase)
    {
        var value = !string.IsNullOrWhiteSpace(_options.AdminBaseUrl) ? _options.AdminBaseUrl : linkBase;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');
    }

    private string LinkFor(string page, string token, string? linkBase) =>
        $"{LinkBaseOrNull(linkBase)}/{page}?token={Uri.EscapeDataString(token)}";

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256) return false;
        try
        {
            return new MailAddress(email).Address == email;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Paragraphs (encoded); a value starting with https:// or http:// becomes a link.</summary>
    private static string Html(params string[] paragraphs) =>
        string.Concat(paragraphs.Select(p => p.StartsWith("http", StringComparison.Ordinal)
            ? $"<p><a href=\"{WebUtility.HtmlEncode(p)}\">{WebUtility.HtmlEncode(p)}</a></p>"
            : $"<p>{WebUtility.HtmlEncode(p)}</p>"));
}
