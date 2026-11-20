using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using JustModIt.Manager.Ui.Connection;

namespace JustModIt.Manager.Ui;

/// <summary>
/// The window's process, started by the agent when the player asks for the window: the Avalonia application with its main
/// window, until the window closes.
/// </summary>
public static class WindowHost
{
    /// <summary>Runs on the process's main thread until the window closes.</summary>
    /// <param name="context">The app's names, the agent's pipe and this process's command line.</param>
    /// <returns>The process's exit code.</returns>
    public static int Run(UiContext context)
    {
        AgentConnection? agent = null;
        PosixSignalRegistration[] stops = [];
        var exitCode = AppBuilder.Configure(() => new App(context,
                () => agent = new AgentConnection(context.PipeName, context.Version, context.Loggers)))
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = context.DesktopId })
            .WithInterFont()
            // Once the platform runs, since the signal posts to its dispatcher.
            .StartWithClassicDesktopLifetime([], lifetime => lifetime.Startup += (_, _) => stops = CloseOnStopSignals(lifetime));
        // The window has closed and nothing on this thread renders any more; what is still queued goes out first.
        agent?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        foreach (var stop in stops)
        {
            stop.Dispose();
        }

        return exitCode;
    }

    // Windows tells the window of a sign-out through its messages. Elsewhere the system's stop signal (a logout, the agent's
    // unit stopping) and Steam's interrupt for "Exit game" would end the process at once and leave the session marker of an
    // unexpected close, so they close the window as a sign-out does, without asking.
    private static PosixSignalRegistration[] CloseOnStopSignals(IControlledApplicationLifetime lifetime)
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }

        void Close(PosixSignalContext signal)
        {
            signal.Cancel = true;
            Dispatcher.UIThread.Post(() => lifetime.Shutdown());
        }

        return [PosixSignalRegistration.Create(PosixSignal.SIGTERM, Close), PosixSignalRegistration.Create(PosixSignal.SIGINT, Close)];
    }
}
