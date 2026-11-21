using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui;

/// <summary>What the window's process knows when it starts.</summary>
public sealed record UiContext
{
    public required string ProductName { get; init; }

    public required string Version { get; init; }

    /// <summary>The system the window runs on, which problem and error reports name.</summary>
    public required SystemRelease System { get; init; }

    /// <summary>The app's reverse domain name, which the window gives Linux desktops as its class, so they group it under the app.</summary>
    public required string DesktopId { get; init; }

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

    /// <summary>
    /// The site's addresses and the name the app gives itself there, which the window's image requests carry; it never
    /// holds the API key, which only the agent sends.
    /// </summary>
    public required CatalogueApiOptions Site { get; init; }

    /// <summary>The repository holding the app's source code; null offers no link to it.</summary>
    public Uri? SourceAddress { get; init; }

    /// <summary>The commit the app was built from, whose source the licences page links to; null when the build did not record it.</summary>
    public string? Revision { get; init; }

    /// <summary>Where players report a problem, which the window gives the app's and the system's version; null offers none.</summary>
    public Uri? IssuesAddress { get; init; }

    /// <summary>The Sentry DSN that error reports go to with the player's consent; empty offers no reports.</summary>
    public string ErrorReportsDsn { get; init; } = "";

    /// <summary>How the window's own previous run crashed, which this window tells; null when it did not.</summary>
    public UncleanExitView? PreviousCrash { get; init; }

    /// <summary>Where the window keeps the images it downloaded, so they show again without the network.</summary>
    public required string ImageCacheDirectory { get; init; }

    /// <summary>Where the package creator keeps its projects, which only this process reads and writes.</summary>
    public required string CreatorDirectory { get; init; }

    /// <summary>
    /// The only hosts the window loads images from, the ones the site loads them from: each alone or with the path its
    /// images are under, e.g. <c>images.example.com/vi/</c>.
    /// </summary>
    public IReadOnlyList<string> ImageHosts { get; init; } = [];

    /// <summary>The platform's decoders for the pictures the window's own decoder does not read, such as AVIF, and for clips; null for none.</summary>
    public IMediaCodecs? Codecs { get; init; }

    /// <summary>The system's wish for reduced motion, which the window follows; null where the system has none.</summary>
    public ISystemMotion? SystemMotion { get; init; }

    /// <summary>The system's frame around windows, which the window's smallest size makes up for; null where it adds none.</summary>
    public IWindowFrames? WindowFrames { get; init; }

    /// <summary>Who draws the window's title bar and its buttons as it opens; on a Linux desktop the player may change it.</summary>
    public WindowFrame Frame { get; init; }

    /// <summary>The player chooses whether the system draws the window's title bar, as on a Linux desktop.</summary>
    public bool OffersSystemTitleBar => Frame is WindowFrame.Frameless or WindowFrame.System;

    /// <summary>
    /// The window runs in a game session such as the Steam Deck's Game Mode, told by its own environment, since the
    /// connecting view needs it before the agent says anything.
    /// </summary>
    public bool InGameMode => Frame == WindowFrame.Screen;

    /// <summary>Steam started the window from its library, so the app's shortcut is there; told on Linux only.</summary>
    public bool StartedBySteam { get; init; }

    /// <summary>The app's own views, pages, styles and resources, added after the shared ones so they win.</summary>
    public Action<UiComposition>? Compose { get; init; }

    /// <summary>
    /// Starts the agent again after the window waited for it in vain and tells whether the system took the start; it may wait
    /// for the system, so it runs off the UI thread.
    /// </summary>
    public required Func<bool> StartAgent { get; init; }

    /// <summary>Where the app writes its logs, which the window opens when the system refused to start the agent, since only they say why.</summary>
    public required string LogsDirectory { get; init; }

    /// <summary>The agent runs as the user's systemd unit, whose restart also replaces an agent that answers, as one of another version.</summary>
    public bool AgentRunsAsUnit { get; init; }

    /// <summary>The file this copy of the app runs from, such as its AppImage, which the window names when the agent runs another version.</summary>
    public string ProgramFile { get; init; } = "";

    /// <summary>
    /// Starts the window's process anew from the app's file once the window closed, so it runs the version an update put in the
    /// file's place; null where the file brings no other version, as anywhere but in an AppImage.
    /// </summary>
    public Action<WindowRestart>? RestartWindow { get; init; }

    /// <summary>How this process started anew in the agent's version; null when it did not.</summary>
    public WindowRestart? Restarted { get; init; }
}
