using System.Globalization;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.LiveTests;

/// <summary>Writes the engine's log into the test's output, so a live run that fails shows what the engine did.</summary>
/// <param name="output">The test's output; null logs nothing.</param>
internal sealed class TestOutputLoggers(ITestOutputHelper? output) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new OutputLogger(output, categoryName);

    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

    public void Dispose()
    {
    }

    private sealed class OutputLogger(ITestOutputHelper? output, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => output is not null && logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            try
            {
                output!.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.Now:HH:mm:ss.fff} {logLevel} {category}: {formatter(state, exception)} {exception}"));
            }
            // Background work may still log after its test finished and closed the output.
            catch (InvalidOperationException)
            {
            }
        }
    }
}
