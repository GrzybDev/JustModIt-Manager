using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Diagnostics;

/// <summary>Logs exceptions that no code caught, on any thread, before they are lost with the process.</summary>
public static partial class CrashHandlers
{
    /// <summary>Starts logging exceptions that no code caught and recording a crash in the session marker.</summary>
    /// <param name="logger">Receives the exceptions.</param>
    /// <param name="session">Records a crash for the next start to report.</param>
    /// <param name="flush">Writes out what the logger still buffers; disposed when an exception ends the process.</param>
    /// <returns>Removes the handlers again.</returns>
    public static IDisposable Install(ILogger logger, SessionMarker session, IDisposable flush)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(flush);

        void OnUnhandled(object? sender, UnhandledExceptionEventArgs args)
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogCrash(logger, exception);
                session.RecordCrash(exception);
            }

            if (args.IsTerminating)
            {
                flush.Dispose();
            }
        }

        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            LogUnobserved(logger, args.Exception);
            args.SetObserved();
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        return new Removal(() =>
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        });
    }

    /// <summary>Tells in the log how the previous run ended, when it did not exit cleanly.</summary>
    /// <param name="logger">Receives the report.</param>
    /// <param name="previous">What <see cref="SessionMarker.Begin"/> found; null after a clean exit.</param>
    public static void Report(ILogger logger, UncleanExit? previous)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (previous is { CrashType: { } crash })
        {
            LogPreviousCrash(logger, previous.Version, crash, previous.CrashedAt);
        }
        else if (previous is not null)
        {
            LogPreviousKilled(logger, previous.Version, previous.StartedAt);
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "An unhandled exception ends the process")]
    private static partial void LogCrash(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A background task failed and nobody observed it")]
    private static partial void LogUnobserved(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The previous session ({Version}) crashed with {CrashType} at {CrashedAt}")]
    private static partial void LogPreviousCrash(ILogger logger, string version, string crashType, DateTimeOffset? crashedAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The previous session ({Version}, started {StartedAt}) ended unexpectedly")]
    private static partial void LogPreviousKilled(ILogger logger, string version, DateTimeOffset startedAt);

    private sealed class Removal(Action remove) : IDisposable
    {
        private Action? _remove = remove;

        public void Dispose() => Interlocked.Exchange(ref _remove, null)?.Invoke();
    }
}
