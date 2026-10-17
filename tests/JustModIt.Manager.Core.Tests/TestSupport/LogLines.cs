using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Tests.TestSupport;

internal sealed record LogLine(LogLevel Level, string Message, Exception? Exception);

internal sealed class LogLines<T> : ILogger<T>
{
    public List<LogLine> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Lines.Add(new LogLine(logLevel, formatter(state, exception), exception));
}
