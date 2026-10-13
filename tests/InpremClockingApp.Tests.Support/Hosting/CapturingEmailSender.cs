using System.Collections.Concurrent;
using Inprem.Platform.Application.Memberships;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>Replaces the real email sender in the test host: nothing leaves the machine; tests read what was sent.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    public sealed record SentEmail(string To, string Subject, string HtmlBody);

    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyList<SentEmail> Sent => _sent.ToList();

    public IEnumerable<SentEmail> To(string address) =>
        _sent.Where(e => string.Equals(e.To, address, StringComparison.OrdinalIgnoreCase));

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(new SentEmail(to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
