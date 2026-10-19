using System.Collections.Concurrent;
using Inprem.Platform.Application.Notifications;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>
/// Replaces the text sender in the test host (Phase 5): nothing leaves the machine; tests read what
/// was sent, and can make the next sends fail (<see cref="FailNext"/>).
/// </summary>
public sealed class CapturingSmsSender : ISmsSender
{
    public sealed record SentSms(string To, string Body, string ExternalId);

    private readonly ConcurrentQueue<SentSms> _sent = new();
    private readonly ConcurrentQueue<SmsSendResult> _failures = new();

    public bool IsStandIn => true;

    public IReadOnlyList<SentSms> Sent => _sent.ToList();

    public IEnumerable<SentSms> To(string phone) => _sent.Where(s => s.To == phone);

    /// <summary>The next send answers with this failure instead.</summary>
    public void FailNext(SmsSendResult failure) => _failures.Enqueue(failure);

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        if (_failures.TryDequeue(out var failure)) return Task.FromResult(failure);
        var id = "SMtest" + Guid.NewGuid().ToString("N");
        _sent.Enqueue(new SentSms(toE164, body, id));
        return Task.FromResult(SmsSendResult.Ok(id));
    }
}
