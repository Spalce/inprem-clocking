using System.Collections.Concurrent;
using System.Security.Cryptography;
using Inprem.Platform.Application.Notifications;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>
/// Replaces the login-code service in the test host (Phase 5 step 5.2): nothing is texted; tests read
/// the code sent to a number (<see cref="LastCode"/>). As with Verify, a new code replaces the old
/// one and a code works once.
/// </summary>
public sealed class CapturingOtpService : IOtpService
{
    private readonly ConcurrentDictionary<string, string> _pending = new();
    private readonly ConcurrentDictionary<string, string> _last = new();

    public bool IsStandIn => true;

    public int SentCount { get; private set; }

    /// <summary>The last code sent to the number (also after it was used).</summary>
    public string? LastCode(string phoneE164) => _last.GetValueOrDefault(phoneE164);

    public Task<OtpSendResult> SendAsync(string phoneE164, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        _pending[phoneE164] = code;
        _last[phoneE164] = code;
        SentCount++;
        return Task.FromResult(OtpSendResult.Ok);
    }

    public Task<OtpCheck> CheckAsync(string phoneE164, string code, CancellationToken ct = default)
    {
        if (!_pending.TryGetValue(phoneE164, out var expected)) return Task.FromResult(OtpCheck.ExpiredOrUsed);
        if (expected != code) return Task.FromResult(OtpCheck.Wrong);
        _pending.TryRemove(phoneE164, out _);
        return Task.FromResult(OtpCheck.Approved);
    }
}
