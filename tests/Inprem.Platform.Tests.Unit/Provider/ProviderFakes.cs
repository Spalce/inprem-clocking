using Inprem.Platform.Application.Memberships;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Audit;
using Inprem.Platform.Domain.Provider;

namespace Inprem.Platform.Tests.Unit.Provider;

internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow += by;
}

internal sealed class FakeEmail : IEmailSender
{
    public List<(string To, string Subject, string Body)> Sent { get; } = new();

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        Sent.Add((to, subject, htmlBody));
        return Task.CompletedTask;
    }

    public IEnumerable<(string To, string Subject, string Body)> To(string address) =>
        Sent.Where(e => string.Equals(e.To, address, StringComparison.OrdinalIgnoreCase));
}

internal sealed class FakeAccounts : IAccountGateway
{
    public sealed class Account
    {
        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string Email { get; init; } = "";
        public bool EmailConfirmed { get; set; }
        public string? Password { get; set; }
        public bool TwoFactor { get; set; }
        public string TotpCode { get; set; } = "123456";
        public int Stamp { get; set; }
        public bool Disabled { get; set; }
    }

    public List<Account> All { get; } = new();

    public Account Add(string email, string? password = "fake-account-pw", bool confirmed = true, bool twoFactor = true)
    {
        var account = new Account { Email = email, Password = password, EmailConfirmed = confirmed, TwoFactor = twoFactor };
        All.Add(account);
        return account;
    }

    public Account ByEmail(string email) => All.Single(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase));

    private static AccountInfo Info(Account a) => new(a.Id, a.Email, a.EmailConfirmed, a.Password != null, a.TwoFactor);

    public Task<AccountInfo?> FindByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(All.Where(a => string.Equals(a.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)).Select(Info).FirstOrDefault());

    public Task<AccountInfo?> FindByIdAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult(All.Where(a => a.Id == userId).Select(Info).FirstOrDefault());

    public Task<string> CreateWithoutPasswordAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(Add(email, password: null, confirmed: false, twoFactor: false).Id);

    public Task<IReadOnlyList<string>> SetPasswordAsync(string userId, string password, CancellationToken ct = default)
    {
        if (password.Length < 8) return Task.FromResult<IReadOnlyList<string>>(new[] { "Too short." });
        var a = All.Single(x => x.Id == userId);
        a.Password = password;
        a.EmailConfirmed = true;
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    public Task ResetPasswordAndTwoFactorAsync(string userId, CancellationToken ct = default)
    {
        var a = All.Single(x => x.Id == userId);
        a.Password = null;
        a.TwoFactor = false;
        a.Stamp++;
        return Task.CompletedTask;
    }

    public Task ResetTwoFactorAsync(string userId, CancellationToken ct = default)
    {
        var a = All.Single(x => x.Id == userId);
        a.TwoFactor = false;
        a.Stamp++;
        return Task.CompletedTask;
    }

    public Task DisableAsync(string userId, CancellationToken ct = default)
    {
        var a = All.Single(x => x.Id == userId);
        a.Disabled = true;
        a.Stamp++;
        return Task.CompletedTask;
    }

    public Task<bool> VerifyTwoFactorCodeAsync(string userId, string code, CancellationToken ct = default)
    {
        var a = All.Single(x => x.Id == userId);
        return Task.FromResult(a.TwoFactor && a.TotpCode == code);
    }
}

/// <summary>In-memory store. "Saved" state is what was there at the last SaveChangesAsync; tests inspect it.</summary>
internal sealed class FakeStore : IProviderStore
{
    private readonly FakeAccounts _accounts;

    public FakeStore(FakeAccounts accounts) => _accounts = accounts;

    public List<ProviderUser> Users { get; } = new();
    public List<Invitation> Invitations { get; } = new();
    public List<RecoveryRecord> Recoveries { get; } = new();
    public List<AuditEntry> Audit { get; } = new();
    public Dictionary<string, string> UsedCodes { get; } = new();
    public List<(string Kind, bool Succeeded, string Source, DateTime When)> Attempts { get; } = new();
    public int Saves { get; private set; }

    public Task<ProviderUser?> FindProviderUserAsync(string userId, CancellationToken ct = default) => Task.FromResult(Users.FirstOrDefault(u => u.UserId == userId));

    public Task<bool> AnyOwnerAsync(DateTime nowUtc, CancellationToken ct = default) =>
        Task.FromResult(Users.Any(u => u.Role == ProviderRole.Owner &&
            (u.Status == ProviderUserStatus.Active || (u.Status == ProviderUserStatus.Invited && Invitations.Any(i => i.UserId == u.UserId && i.IsUsable(nowUtc))))));

    public Task<IReadOnlyList<OwnerInfo>> ListActiveOwnersAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OwnerInfo>>(Users.Where(u => u.IsActiveOwner)
            .Select(u => _accounts.All.Single(a => a.Id == u.UserId))
            .Select(a => new OwnerInfo(a.Id, a.Email, a.TwoFactor)).ToList());

    public Task AddProviderUserAsync(ProviderUser user, CancellationToken ct = default) { Users.Add(user); return Task.CompletedTask; }

    public Task AddInvitationAsync(Invitation invitation, CancellationToken ct = default) { Invitations.Add(invitation); return Task.CompletedTask; }

    public Task<Invitation?> FindInvitationByTokenHashAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult(Invitations.FirstOrDefault(i => i.TokenHash == tokenHash));

    public Task<IReadOnlyList<Invitation>> ListPendingInvitationsAsync(string userId, DateTime nowUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Invitation>>(Invitations.Where(i => i.UserId == userId && i.IsUsable(nowUtc)).ToList());

    public Task AddRecoveryAsync(RecoveryRecord record, CancellationToken ct = default) { Recoveries.Add(record); return Task.CompletedTask; }

    public Task<RecoveryRecord?> FindRecoveryAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Recoveries.FirstOrDefault(r => r.Id == id));

    public Task<RecoveryRecord?> FindRecoveryByApprovalHashAsync(string tokenHash, CancellationToken ct = default) =>
        Task.FromResult(Recoveries.FirstOrDefault(r => r.FirstApprovalTokenHash == tokenHash || r.SecondApprovalTokenHash == tokenHash));

    public Task<bool> AnyRecoverySinceAsync(string targetUserId, DateTime sinceUtc, CancellationToken ct = default) =>
        Task.FromResult(Recoveries.Any(r => r.TargetUserId == targetUserId && r.CreatedUtc > sinceUtc));

    public Task<bool> IsCodeUsedAsync(string codeHash, CancellationToken ct = default) => Task.FromResult(UsedCodes.ContainsKey(codeHash));

    public Task MarkCodeUsedAsync(string codeHash, string purpose, DateTime nowUtc, CancellationToken ct = default) { UsedCodes.Add(codeHash, purpose); return Task.CompletedTask; }

    public Task AddAttemptAsync(string kind, bool succeeded, string source, DateTime nowUtc, CancellationToken ct = default) { Attempts.Add((kind, succeeded, source, nowUtc)); return Task.CompletedTask; }

    public Task<int> CountFailuresSinceAsync(string kind, DateTime sinceUtc, CancellationToken ct = default) =>
        Task.FromResult(Attempts.Count(a => a.Kind == kind && !a.Succeeded && a.When > sinceUtc));

    public Task AddAuditAsync(AuditEntry entry, CancellationToken ct = default) { Audit.Add(entry); return Task.CompletedTask; }

    public Task SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.CompletedTask; }

    /// <summary>A comparable picture of everything stored, to prove a dry run changed nothing.</summary>
    public string Snapshot() =>
        $"{Users.Count}|{Invitations.Count}|{Recoveries.Count}|{Audit.Count}|{UsedCodes.Count}|{Attempts.Count}|{Saves}|" +
        string.Join(",", Users.Select(u => $"{u.UserId}:{u.Status}")) + "|" + string.Join(",", Invitations.Select(i => $"{i.Id}:{i.ConsumedUtc}:{i.RevokedUtc}"));
}

/// <summary>Everything a service test needs, with an owner helper.</summary>
internal sealed class ProviderTestRig
{
    public const string BootstrapCode = "BOOT1-BOOT2-BOOT3-BOOT4-BOOT5-BOOT6";
    public const string BreakGlassCode = "GLASS-GLASS-GLASS-GLASS-GLASS-GLASS";
    public const string Approver1 = "approver.one@inprem.test";
    public const string Approver2 = "approver.two@inprem.test";
    public const string Mailbox = "security@inprem.test";

    public FakeClock Clock { get; } = new();
    public FakeEmail Email { get; } = new();
    public FakeAccounts Accounts { get; } = new();
    public FakeStore Store { get; }
    public ProviderSecurityOptions Options { get; }
    public ProviderSecurityService Service { get; }

    public ProviderTestRig(Action<ProviderSecurityOptions>? configure = null)
    {
        Store = new FakeStore(Accounts);
        Options = new ProviderSecurityOptions
        {
            BootstrapCodeHash = SecretTokens.Hash(BootstrapCode),
            BreakGlassCodeHash = SecretTokens.Hash(BreakGlassCode),
            RecoveryApprovers = new[] { Approver1, Approver2 },
            SecurityMailbox = Mailbox,
            AdminBaseUrl = "https://admin.inprem.test",
        };
        configure?.Invoke(Options);
        Service = new ProviderSecurityService(Store, Accounts, Email, Clock, Options);
    }

    /// <summary>An active, enrolled Provider Owner (as after bootstrap + invitation + enrolment).</summary>
    public FakeAccounts.Account AddOwner(string email, ProviderRole role = ProviderRole.Owner)
    {
        var account = Accounts.Add(email);
        var user = ProviderUser.Invite(account.Id, role, Clock.UtcNow);
        user.Activate();
        Store.Users.Add(user);
        return account;
    }

    /// <summary>The token of the link in the last email to <paramref name="to"/>.</summary>
    public string TokenSentTo(string to)
    {
        var body = Email.To(to).Last(e => e.Body.Contains("token=", StringComparison.Ordinal)).Body;
        var start = body.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var end = body.IndexOf('"', start);
        return Uri.UnescapeDataString(body[start..end]);
    }
}
