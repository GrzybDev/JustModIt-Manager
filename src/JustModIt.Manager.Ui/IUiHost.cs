using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Protocol.Activation;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui;

/// <summary>What the window's process knows when it starts.</summary>
public sealed record UiContext
{
    public required string ProductName { get; init; }

    public required string Version { get; init; }

    /// <summary>The agent's pipe, which the window talks to for everything.</summary>
    public required string PipeName { get; init; }

    public required AppLinks Links { get; init; }

    /// <summary>What the package creator builds and saves with, e.g. the app's names for its files.</summary>
    public required CreatorOptions Creator { get; init; }

    /// <summary>This process's command line, e.g. with the link to open.</summary>
    public required LaunchArguments Launch { get; init; }

    public required ILoggerFactory Loggers { get; init; }

    /// <summary>What the licences page shows.</summary>
    public required LegalNotices Notices { get; init; }

    /// <summary>The app's logo and the window's icon.</summary>
    public required AppBranding Branding { get; init; }

    /// <summary>The app's accent colour as <c>#rrggbb</c>; empty keeps the theme's own.</summary>
    public string AccentColor { get; init; } = "";

    /// <summary>The system's wish for reduced motion, which the window follows; null where the system has none.</summary>
    public ISystemMotion? SystemMotion { get; init; }

    /// <summary>The app's own views, pages, styles and resources, added after the shared ones so they win.</summary>
    public Action<UiComposition>? Compose { get; init; }
}

/// <summary>The window's process, started by the agent when the player asks for the window and ended when it closes.</summary>
public interface IUiHost
{
    /// <summary>Runs on the process's main thread until the window closes.</summary>
    /// <param name="context">The app's names, the agent's pipe and this process's command line.</param>
    /// <returns>The process's exit code.</returns>
    int Run(UiContext context);
}
