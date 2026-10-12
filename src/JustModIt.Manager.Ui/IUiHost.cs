using JustModIt.Manager.Core.Catalog;
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

    /// <summary>Where the window keeps the images it downloaded, so they show again without the network.</summary>
    public required string ImageCacheDirectory { get; init; }

    /// <summary>
    /// The only hosts the window loads images from, the ones the site loads them from: each alone or with the path its
    /// images are under, e.g. <c>images.example.com/vi/</c>.
    /// </summary>
    public IReadOnlyList<string> ImageHosts { get; init; } = [];

    /// <summary>The system's decoders for the pictures the window's own decoder does not read, such as AVIF; null for none.</summary>
    public IMediaCodecs? Codecs { get; init; }

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
