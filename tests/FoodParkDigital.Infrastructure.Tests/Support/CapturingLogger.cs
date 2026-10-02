using Microsoft.Extensions.Logging;

namespace FoodParkDigital.Infrastructure.Tests.Support;

public sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries;

    public LogEntry? FirstMentioning(string text) => _entries.FirstOrDefault(entry => entry.Mentions(text));

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
        _entries.Add(new LogEntry(logLevel, formatter(state, exception), properties.ToList(), exception));
    }
}

public sealed record LogEntry(
    LogLevel Level,
    string Message,
    IReadOnlyList<KeyValuePair<string, object?>> Properties,
    Exception? Exception,
    string? Category = null)
{
    public object? this[string property] => Properties.FirstOrDefault(p => p.Key == property).Value;

    // Structured sinks persist the template properties and the exception, not only the rendered message.
    public bool Mentions(string text) =>
        Message.Contains(text, StringComparison.OrdinalIgnoreCase)
        || Properties.Any(p => p.Value?.ToString()?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)
        || Exception?.ToString().Contains(text, StringComparison.OrdinalIgnoreCase) == true;
}
