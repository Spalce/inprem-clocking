using System.Net;
using Inprem.Platform.Application.Provider;
using Inprem.Platform.Domain.Provider;
using Inprem.Platform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Inprem.Platform.Tests.Integration.Provider.ProviderFixture;
using static Inprem.Platform.Tests.Integration.Provider.ProviderForms;

namespace Inprem.Platform.Tests.Integration.Provider;

/// <summary>Phase 0 step 0.7: <c>provider bootstrap</c> and <c>provider recover</c> (Level 3) as the operator runs them.</summary>
public class ServerCommandTests : IClassFixture<ProviderFixture>
{
    private const string Owner = "command.owner@provider.test";
    private readonly ProviderFixture _app;

    public ServerCommandTests(ProviderFixture app) => _app = app;

    private async Task<string> StateAsync()
    {
        await using var platform = _app.CreatePlatformDbContext();
        await using var app = _app.CreateDbContext();
        return string.Join("|",
            await app.Users.IgnoreQueryFilters().CountAsync(),
            await platform.ProviderUsers.CountAsync(), await platform.ProviderInvitations.CountAsync(),
            await platform.RecoveryRecords.CountAsync(), await platform.AuditEntries.CountAsync(),
            await platform.UsedCodes.CountAsync(), await platform.SecurityAttempts.CountAsync(),
            string.Join(",", await app.Users.IgnoreQueryFilters().OrderBy(u => u.Id).Select(u => u.SecurityStamp + (u.PasswordHash == null ? "-" : "+")).ToListAsync()),
            _app.Emails.Sent.Count);
    }

    [Fact]
    public async Task Provider_Commands_Usage_Errors()
    {
        Assert.Equal(2, (await _app.RunCommandAsync(new[] { "provider" })).ExitCode);
        Assert.Equal(2, (await _app.RunCommandAsync(new[] { "provider", "delete-everything" })).ExitCode);
        Assert.Equal(2, (await _app.RunCommandAsync(new[] { "provider", "bootstrap" })).ExitCode);
        Assert.Equal(2, (await _app.RunCommandAsync(new[] { "provider", "bootstrap", "--email" })).ExitCode);
        Assert.Equal(2, (await _app.RunCommandAsync(new[] { "provider", "recover", "--user", Owner })).ExitCode);
        Assert.Equal(2, (await _app.RunCommandAsync(new[] { "provider", "recover", "--complete", "--id", "not-a-guid" })).ExitCode);
        Assert.False(ProviderCommandsHostArgs("provider", "bootstrap", "--email", "x").Any());
    }

    private static string[] ProviderCommandsHostArgs(params string[] args) => InpremClockingApp.Provider.ProviderCommands.HostArgs(args);

    [Fact]
    public async Task Provider_Bootstrap_And_Level3_Recovery_Through_Server_Commands()
    {
        // Dry run: says what would happen, changes nothing, sends nothing.
        var before = await StateAsync();
        var dry = await _app.RunCommandAsync(new[] { "provider", "bootstrap", "--email", Owner, "--code", BootstrapCode, "--dry-run" });
        Assert.Equal(0, dry.ExitCode);
        Assert.Contains("Dry run", dry.Output);
        Assert.Equal(before, await StateAsync());

        // Wrong code: refused (exit 1).
        Assert.Equal(1, (await _app.RunCommandAsync(new[] { "provider", "bootstrap", "--email", Owner, "--code", "WRONG" })).ExitCode);

        // The code typed at the prompt (kept out of the shell history); the output never shows a code or link.
        var boot = await _app.RunCommandAsync(new[] { "provider", "bootstrap", "--email", Owner }, typedInput: BootstrapCode + "\n");
        Assert.Equal(0, boot.ExitCode);
        Assert.DoesNotContain(BootstrapCode, boot.Output);
        Assert.DoesNotContain("token=", boot.Output);
        Assert.DoesNotContain("https://", boot.Output);
        Assert.Equal(1, (await _app.RunCommandAsync(new[] { "provider", "bootstrap", "--email", "second@provider.test", "--code", BootstrapCode })).ExitCode);

        var (owner, key, _) = await _app.CompleteInvitationAsync(Owner);
        await GetDocumentAsync(owner, "/Provider");

        // Level 3 dry run, then refusals that touch nothing.
        before = await StateAsync();
        var dryRecover = await _app.RunCommandAsync(new[] { "provider", "recover", "--user", Owner, "--approver", Approver1, "--code", BreakGlassCode, "--dry-run" });
        Assert.Equal(0, dryRecover.ExitCode);
        Assert.Equal(before, await StateAsync());
        Assert.Equal(1, (await _app.RunCommandAsync(new[] { "provider", "recover", "--user", "nobody@provider.test", "--approver", Approver1, "--code", BreakGlassCode })).ExitCode);

        // Level 3: the approver is asked; nothing happens to the owner until they approve.
        var recover = await _app.RunCommandAsync(new[] { "provider", "recover", "--user", Owner, "--approver", Approver1, "--code", BreakGlassCode });
        Assert.Equal(0, recover.ExitCode);
        Assert.DoesNotContain(BreakGlassCode, recover.Output);
        Assert.DoesNotContain("token=", recover.Output);
        await GetDocumentAsync(owner, "/Provider");
        Assert.Contains(_app.Emails.To(Mailbox), e => e.Subject.Contains("Level 3 started"));

        using var approver = _app.CreateClient();
        var approval = LinkSentTo(_app.Emails, Approver1);
        Assert.NotNull((await GetDocumentAsync(approver, approval)).QuerySelector("[data-testid=request]"));
        var approved = await PostAsync(approver, approval);
        Assert.Contains("1-hour link to set a new password", await BodyAsync(approved));

        // Sessions revoked at once, password and two-factor removed.
        Assert.EndsWith("/Provider/Login", Location(await owner.GetAsync("/Provider")));
        using (var scope = _app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<InpremClockingApp.Models.Identity.AppUser>>();
            var account = (await users.FindByEmailAsync(Owner))!;
            Assert.False(await users.HasPasswordAsync(account));
            Assert.False(await users.GetTwoFactorEnabledAsync(account));
        }
        using (var oldPassword = _app.CreateClient())
        {
            Assert.Contains("Invalid sign-in attempt.", await BodyAsync(await PostAsync(oldPassword, "/Provider/Login", ("Email", Owner), ("Password", StrongPassword))));
        }

        // The 1-hour link went to the owner's own address only; it restores access with a new authenticator.
        await using (var platform = _app.CreatePlatformDbContext())
        {
            var invitation = await platform.ProviderInvitations.Where(i => i.Purpose == InvitationPurpose.ProviderRecovery).SingleAsync();
            Assert.Equal((Owner, TimeSpan.FromHours(1)), (invitation.Email, invitation.ExpiresUtc - invitation.CreatedUtc));
            Assert.Equal(RecoveryStatus.Completed, (await platform.RecoveryRecords.SingleAsync()).Status);
            Assert.Contains(await platform.AuditEntries.ToListAsync(), a => a.Action == "provider.recovery.level3.completed");
        }
        var (restored, newKey, _) = await _app.CompleteInvitationAsync(Owner);
        Assert.NotEqual(key, newKey);
        await GetDocumentAsync(restored, "/Provider");
        Assert.Contains(_app.Emails.To(Mailbox), e => e.Subject.Contains("Level 3 completed"));

        // At most one recovery per account per 24 hours.
        var again = await _app.RunCommandAsync(new[] { "provider", "recover", "--user", Owner, "--approver", Approver1, "--code", BreakGlassCode });
        Assert.Equal(1, again.ExitCode);
        Assert.Contains("24 hours", again.Output);
    }
}

/// <summary>Phase 0 step 0.7: Level 4 (replace) with its 24-hour delay, and Level 2 (peer reset).</summary>
public class ReplaceAndPeerResetTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public ReplaceAndPeerResetTests(ProviderFixture app) => _app = app;

    [Fact]
    public async Task Provider_Level4_Replaces_The_Owner_After_The_Delay()
    {
        const string old = "leaving.owner@provider.test";
        const string successor = "successor@provider.test";
        var key = await _app.CreateEnrolledOwnerAsync(old);
        using var oldSession = _app.CreateClient();
        await SignInAsync(oldSession, old, key);

        var started = await _app.RunCommandAsync(new[]
        {
            "provider", "recover", "--replace", "--old", old, "--new-email", successor, "--code", BreakGlassCode,
            "--approver", Approver1, "--approver", Approver2, "--approval-ref", "Letter signed by project owner and CTO, 2026-10-13",
        });
        Assert.Equal(0, started.ExitCode);
        var id = Guid.Parse(started.Output.Split(' ').First(w => Guid.TryParse(w, out _)));

        // Approver 1 waives the delay, approver 2 does not: the 24 hours apply.
        using var approver = _app.CreateClient();
        Assert.Contains("Waiting for the other approver", await BodyAsync(await PostAsync(approver, LinkSentTo(_app.Emails, Approver1), ("WaiveDelay", "true"))));
        Assert.Contains("can be completed after", await BodyAsync(await PostAsync(approver, LinkSentTo(_app.Emails, Approver2))));
        await GetDocumentAsync(oldSession, "/Provider"); // still an owner

        var early = await _app.RunCommandAsync(new[] { "provider", "recover", "--complete", "--id", id.ToString() });
        Assert.Equal(1, early.ExitCode);

        _app.Clock.Advance(TimeSpan.FromHours(24));
        var done = await _app.RunCommandAsync(new[] { "provider", "recover", "--complete", "--id", id.ToString() });
        Assert.Equal(0, done.ExitCode);

        // The old owner is disabled and signed out everywhere; the successor is invited.
        Assert.EndsWith("/Provider/Login", Location(await oldSession.GetAsync("/Provider")));
        using (var retry = _app.CreateClient())
        {
            var login = await PostAsync(retry, "/Provider/Login", ("Email", old), ("Password", StrongPassword));
            Assert.Contains("Invalid sign-in attempt.", await BodyAsync(login));
        }
        await using (var platform = _app.CreatePlatformDbContext())
        {
            var oldUser = await _app.FindUserAsync(old);
            Assert.Equal(ProviderUserStatus.Disabled, (await platform.ProviderUsers.SingleAsync(u => u.UserId == oldUser.Id)).Status);
            Assert.Contains(await platform.AuditEntries.ToListAsync(), a => a.Action == "provider.recovery.level4.completed");
        }
        var (successorClient, _, _) = await _app.CompleteInvitationAsync(successor);
        await GetDocumentAsync(successorClient, "/Provider");
    }

    [Fact]
    public async Task Provider_Level2_Peer_Reset_Signs_The_Owner_Out_And_Needs_Reenrolment()
    {
        const string actor = "actor.owner@provider.test";
        const string target = "target.owner@provider.test";
        var actorKey = await _app.CreateEnrolledOwnerAsync(actor);
        var targetKey = await _app.CreateEnrolledOwnerAsync(target);
        using var targetSession = _app.CreateClient();
        await SignInAsync(targetSession, target, targetKey);

        var actorId = (await _app.FindUserAsync(actor)).Id;
        var targetId = (await _app.FindUserAsync(target)).Id;
        using (var scope = _app.Services.CreateScope())
        {
            var provider = scope.ServiceProvider.GetRequiredService<ProviderSecurityService>();
            Assert.Equal("refused.reauthentication", (await provider.PeerResetAsync(actorId, targetId, "Lost phone", "000000")).Code);
            Assert.Equal("refused.self", (await provider.PeerResetAsync(targetId, targetId, "Lost phone", Totp.Code(targetKey))).Code);
            var reset = await provider.PeerResetAsync(actorId, targetId, "Lost phone and codes", Totp.Code(actorKey));
            Assert.True(reset.Succeeded, reset.Message);
        }

        Assert.EndsWith("/Provider/Login", Location(await targetSession.GetAsync("/Provider")));
        using var again = _app.CreateClient();
        var login = await PostAsync(again, "/Provider/Login", ("Email", target), ("Password", StrongPassword));
        Assert.EndsWith("/Provider/EnrolTwoFactor", Location(login));
        Assert.EndsWith("/Provider/EnrolTwoFactor", Location(await again.GetAsync("/Provider")));
        await EnrolAsync(again);
        await GetDocumentAsync(again, "/Provider");

        Assert.Contains(_app.Emails.To(actor), e => e.Subject.Contains("Level 2"));
        Assert.Contains(_app.Emails.To(Mailbox), e => e.Subject.Contains("Level 2"));
    }

    [Fact]
    public async Task Provider_Audit_Log_Is_Append_Only()
    {
        await using var platform = _app.CreatePlatformDbContext();
        platform.AuditEntries.Add(Inprem.Platform.Domain.Audit.AuditEntry.Record("test", "test.entry", null, "x", DateTime.UtcNow));
        await platform.SaveChangesAsync();
        var entry = await platform.AuditEntries.OrderByDescending(a => a.Id).FirstAsync();

        platform.Entry(entry).State = EntityState.Modified;
        Assert.Throws<InvalidOperationException>(() => platform.SaveChanges());
        platform.Entry(entry).State = EntityState.Deleted;
        await Assert.ThrowsAsync<InvalidOperationException>(() => platform.SaveChangesAsync());
        platform.Entry(entry).State = EntityState.Unchanged;

        var update = await Assert.ThrowsAsync<SqlException>(() => platform.Database.ExecuteSqlRawAsync("UPDATE platform.AuditEntries SET Details = 'changed'"));
        Assert.Equal(50010, update.Number);
        var delete = await Assert.ThrowsAsync<SqlException>(() => platform.Database.ExecuteSqlRawAsync("DELETE FROM platform.AuditEntries"));
        Assert.Equal(50010, delete.Number);
    }
}

/// <summary>Phase 0 step 0.7: repeated break-glass failures lock the recover command for 24 hours.</summary>
public class BreakGlassLockTests : IClassFixture<ProviderFixture>
{
    private readonly ProviderFixture _app;

    public BreakGlassLockTests(ProviderFixture app) => _app = app;

    [Fact]
    public async Task Provider_Recover_Command_Locks_After_Three_Failures()
    {
        const string owner = "locked.owner@provider.test";
        await _app.CreateEnrolledOwnerAsync(owner);
        string[] Recover(string code) => new[] { "provider", "recover", "--user", owner, "--approver", Approver1, "--code", code };

        for (var i = 0; i < ProviderSecurityService.BreakGlassFailuresBeforeLock; i++) Assert.Equal(1, (await _app.RunCommandAsync(Recover("WRONG"))).ExitCode);
        Assert.Contains(_app.Emails.To(owner), e => e.Subject.Contains("locked"));
        Assert.Contains(_app.Emails.To(Mailbox), e => e.Subject.Contains("locked"));

        var locked = await _app.RunCommandAsync(Recover(BreakGlassCode));
        Assert.Equal(1, locked.ExitCode);
        Assert.Contains("locked", locked.Output);
        Assert.Empty(_app.Emails.To(Approver1));

        _app.Clock.Advance(TimeSpan.FromHours(24.1));
        Assert.Equal(0, (await _app.RunCommandAsync(Recover(BreakGlassCode))).ExitCode);
    }
}
