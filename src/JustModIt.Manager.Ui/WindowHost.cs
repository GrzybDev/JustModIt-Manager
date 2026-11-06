using Avalonia;
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
        var exitCode = AppBuilder.Configure(() => new App(context,
                () => agent = new AgentConnection(context.PipeName, context.Version, context.Loggers)))
            .UsePlatformDetect()
            .WithInterFont()
            .StartWithClassicDesktopLifetime([]);
        // The window has closed and nothing on this thread renders any more; what is still queued goes out first.
        agent?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return exitCode;
    }
}
