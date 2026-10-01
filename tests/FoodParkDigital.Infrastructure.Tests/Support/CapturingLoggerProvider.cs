using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Tests.Support;

/// <summary>Captures what every logger of a service provider writes, EF Core's own events included.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyCollection<LogEntry> Entries => _entries;

    public LogEntry? FirstMentioning(string text) => _entries.FirstOrDefault(entry => entry.Mentions(text));

    public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CategoryLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), properties.ToList(), exception, category));
        }
    }
}
