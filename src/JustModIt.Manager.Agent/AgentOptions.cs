using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Protocol.Activation;

namespace JustModIt.Manager.Agent;

/// <summary>What the agent knows about the app it runs for, from the app's build configuration.</summary>
public sealed record AgentOptions
{
    public required AppIdentity Identity { get; init; }

    /// <summary>The app's name as players see it, in the tray and in notifications.</summary>
    public required string ProductName { get; init; }

    public required string Version { get; init; }

    public required AppDataLocations Locations { get; init; }

    public required InstanceNames Names { get; init; }

    public required CatalogueApiOptions Api { get; init; }

    /// <summary>The app's extension for manifest packages, with its dot, which names the ones it downloads.</summary>
    public required string PackageExtension { get; init; }

    public TargetLanguage TargetLanguage { get; init; } = TargetLanguage.None;

    /// <summary>
    /// The executable that the window, autostart and the link scheme start: once installed, the copy the updater keeps at
    /// a stable path.
    /// </summary>
    public required string ExecutablePath { get; init; }

    /// <summary>The folder the app is installed in, which updates replace; backups are never allowed in it.</summary>
    public required string InstallDirectory { get; init; }

    /// <summary>Registers autostart and the link scheme, which a development build does only when asked to.</summary>
    public bool IntegratesWithShell { get; init; }

    /// <summary>Where the app's own updates come from; null turns self-update off.</summary>
    public UpdateFeed? Updates { get; init; }

    /// <summary>The updater started this process after applying an update, or after failing to apply it.</summary>
    public bool RestartedByUpdater { get; init; }
}

/// <summary>The feed the app's own updates come from.</summary>
/// <param name="Url">The app's GitHub repository, whose releases hold its updates.</param>
/// <param name="Channel">The release channel to follow; null follows the one the installed build came from.</param>
/// <param name="IncludesPrereleases">Whether GitHub pre-releases count as updates.</param>
public sealed record UpdateFeed(string Url, string? Channel, bool IncludesPrereleases);
