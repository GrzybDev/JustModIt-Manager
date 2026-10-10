using Avalonia;
using JustModIt.Manager.Ui.Connection;

namespace JustModIt.Manager.Ui;

/// <summary>The window's process: the Avalonia application with its main window, until the window closes.</summary>
public sealed class WindowHost : IUiHost
{
    public int Run(UiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
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
