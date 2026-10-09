using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library.Heroic;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>
/// Reads the GOG games of one Heroic installation from <c>gog_store\installed.json</c>, written only once an install
/// finishes, so a download in progress is told by gogdl's markers in the game folder and a waiting one by Heroic's queue.
/// </summary>
/// <param name="fileSystem">Reads Heroic's files.</param>
/// <param name="configDirectory">Heroic's configuration folder (<see cref="HeroicLauncher.ConfigDirectories"/>).</param>
internal sealed class HeroicGogSource(IFileSystem fileSystem, string configDirectory) : IInstallSource<GogInstallRecord>
{
    private const string Runner = "gog";

    private string InstalledFile => Path.Join(configDirectory, "gog_store", "installed.json");

    public Task<SourceScan<GogInstallRecord>> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Access.ReadAsync(InstalledFile, ReadInstalledAsync, cancellationToken);
    }

    private async Task<SourceScan<GogInstallRecord>> ReadInstalledAsync(CancellationToken cancellationToken)
    {
        // Heroic may be installed, or signed in to GOG, only later; each scan watches one folder closer to the file.
        var watch = WatchPlan.ForFolders(WatchedFolder.Toward(fileSystem, InstalledFile),
            WatchedFolder.Toward(fileSystem, HeroicLauncher.QueueFile(configDirectory)));
        if (!fileSystem.FileExists(InstalledFile))
        {
            return new([], [], watch);
        }

        var file = fileSystem.GetCanonicalPath(InstalledFile);
        IReadOnlyList<HeroicInstalledGame> installed;
        IReadOnlyDictionary<string, GameInstallState> queued;
        try
        {
            installed = (await LauncherJson.ReadAsync(fileSystem, file, LauncherJsonContext.Default.HeroicInstalledFile, cancellationToken)
                .ConfigureAwait(false))?.Installed ?? [];
            queued = await HeroicLauncher.ReadQueueAsync(fileSystem, configDirectory, Runner, cancellationToken).ConfigureAwait(false);
        }
        // Usually caught while Heroic rewrites a file; the games it listed are kept until it can be read again.
        catch (Exception exception) when (LauncherJson.IsUnreadable(exception))
        {
            return new([new LibraryLocation(file, HeroicLauncher.LibrarySource, file, LibraryState.Unreadable)], [], watch);
        }

        return new([new LibraryLocation(file, HeroicLauncher.LibrarySource, file, LibraryState.Online)],
            [.. installed.Select(game => ToRecord(game, file, queued)).OfType<GogInstallRecord>()], watch);
    }

    private static GogInstallRecord? ToRecord(HeroicInstalledGame game, string libraryId,
        IReadOnlyDictionary<string, GameInstallState> queued) =>
        GogJson.IsId(game.AppName) && LauncherJson.NullIfBlank(game.InstallPath) is { } path
            ? new GogInstallRecord(game.AppName, path, libraryId)
            {
                BuildId = LauncherJson.NullIfBlank(game.BuildId),
                IsDlc = game.IsDlc,
                Language = LauncherJson.NullIfBlank(game.Language),
                Platform = game.Platform?.ToUpperInvariant() switch
                {
                    "WINDOWS" => OperatingSystemKind.Windows,
                    "LINUX" => OperatingSystemKind.Linux,
                    "OSX" or "MAC" => OperatingSystemKind.MacOS,
                    _ => OperatingSystemKind.Unknown,
                },
                Launch = HeroicLauncher.Launch(game.AppName, Runner),
                Activity = queued.TryGetValue(game.AppName, out var activity) ? activity : null,
            }
            : null;
}
