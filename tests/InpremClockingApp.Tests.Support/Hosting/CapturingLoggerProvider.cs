using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace InpremClockingApp.Tests.Support.Hosting;

/// <summary>
/// Keeps every log line a test host writes (category, level, the formatted message, the exception
/// and every structured value), so tests can check what reaches the logs (step 7.6).
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public sealed record Entry(string Category, LogLevel Level, string Message, string? Exception, IReadOnlyList<string> Values)
    {
        /// <summary>Everything a log sink could store for this line.</summary>
        public string AllText => string.Join(" | ", new[] { Message, Exception ?? "" }.Concat(Values));
    }

    public ConcurrentQueue<Entry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger : ILogger
    {
        private readonly CapturingLoggerProvider _owner;
        private readonly string _category;

        public Logger(CapturingLoggerProvider owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Where(p => p.Key != "{OriginalFormat}").Select(p => $"{p.Key}={p.Value}").ToList()
                : new List<string>();
            _owner.Entries.Enqueue(new Entry(_category, logLevel, formatter(state, exception), exception?.ToString(), values));
        }
    }
}
