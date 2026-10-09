using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library.Heroic;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Epic;

/// <summary>
/// Reads the games Legendary installed, on its own or inside Heroic Games Launcher, from its <c>installed.json</c>,
/// taking each catalog namespace from the metadata Legendary keeps for the app, since <c>installed.json</c> records none.
/// </summary>
/// <param name="fileSystem">Reads Legendary's and Heroic's files.</param>
/// <param name="legendaryDirectory">Legendary's configuration folder.</param>
/// <param name="heroicDirectory">Heroic's configuration folder when this Legendary is Heroic's, which then starts its games.</param>
public sealed class LegendarySource(IFileSystem fileSystem, string legendaryDirectory, string? heroicDirectory = null)
    : IInstallSource<EpicInstallRecord>
{
    /// <summary>The <see cref="LibraryLocation.Source"/> of Legendary's list of installs when it runs on its own.</summary>
    public const string LibrarySource = "legendary";

    private const string Runner = "legendary";

    private string InstalledFile => Path.Join(legendaryDirectory, "installed.json");

    private string MetadataDirectory => Path.Join(legendaryDirectory, "metadata");

    /// <summary>
    /// Gives the configuration folder of Legendary installed in its own right, found the way Legendary itself looks for it.
    /// </summary>
    /// <param name="environment">The user's folders and environment variables.</param>
    public static string StandaloneDirectory(LibraryEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.LegendaryConfigPath
            ?? Path.Join(environment.XdgConfigHome ?? Path.Join(environment.HomeDirectory, ".config"), "legendary");
    }

    /// <summary>Creates the source of the Legendary that Heroic runs for its Epic games.</summary>
    /// <param name="fileSystem">Reads Legendary's and Heroic's files.</param>
    /// <param name="heroicDirectory">Heroic's configuration folder.</param>
    public static LegendarySource InHeroic(IFileSystem fileSystem, string heroicDirectory) =>
        new(fileSystem, Path.Join(heroicDirectory, "legendaryConfig", "legendary"), heroicDirectory);

    public Task<SourceScan<EpicInstallRecord>> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Access.ReadAsync(InstalledFile, ReadInstalledAsync, cancellationToken);
    }

    private async Task<SourceScan<EpicInstallRecord>> ReadInstalledAsync(CancellationToken cancellationToken)
    {
        var temporary = Path.Join(legendaryDirectory, "tmp");
        WatchedFolder?[] watches =
        [
            WatchedFolder.Toward(fileSystem, InstalledFile),
            fileSystem.DirectoryExists(temporary) ? new WatchedFolder(temporary, ["*.resume", "*.repair"]) : null,
            heroicDirectory is null ? null : WatchedFolder.Toward(fileSystem, HeroicLauncher.QueueFile(heroicDirectory)),
        ];
        var watch = new WatchPlan([.. watches.OfType<WatchedFolder>().Distinct()], []);
        if (!fileSystem.FileExists(InstalledFile))
        {
            return new([], [], watch);
        }

        var file = fileSystem.GetCanonicalPath(InstalledFile);
        var source = heroicDirectory is null ? LibrarySource : HeroicLauncher.LibrarySource;
        var records = new List<EpicInstallRecord>();
        try
        {
            var installed = (await LauncherJson.ReadAsync(fileSystem, file,
                LauncherJsonContext.Default.DictionaryStringLegendaryInstalledGame, cancellationToken).ConfigureAwait(false))?.Values.ToList()
                ?? [];
            if (installed.Count > 0 && fileSystem.DirectoryExists(MetadataDirectory))
            {
                var metadata = new WatchedFolder(MetadataDirectory, [.. installed.Select(game => $"{game.AppName}.json")]);
                watch = watch.Merge(new WatchPlan([metadata], []));
            }

            var queued = heroicDirectory is null
                ? new Dictionary<string, GameInstallState>()
                : await HeroicLauncher.ReadQueueAsync(fileSystem, heroicDirectory, Runner, cancellationToken).ConfigureAwait(false);
            foreach (var game in installed)
            {
                if (LauncherJson.NullIfBlank(game.AppName) is { } appName && LauncherJson.NullIfBlank(game.InstallPath) is { } path)
                {
                    records.Add(await ToRecordAsync(game, appName, path, file, temporary, queued, cancellationToken).ConfigureAwait(false));
                }
            }
        }
        // Legendary rewrites its files in place, so one caught mid-write keeps the games it listed until it can be read again.
        catch (Exception exception) when (LauncherJson.IsUnreadable(exception))
        {
            return new([new LibraryLocation(file, source, file, LibraryState.Unreadable)], [], watch);
        }

        return new([new LibraryLocation(file, source, file, LibraryState.Online)], EpicInstallRecord.FoldDlc(records), watch);
    }

    // Throws InvalidDataException when the app's metadata is missing or names no catalog namespace.
    private async Task<EpicInstallRecord> ToRecordAsync(LegendaryInstalledGame game, string appName, string path, string libraryId,
        string temporary, IReadOnlyDictionary<string, GameInstallState> queued, CancellationToken cancellationToken)
    {
        var metadataFile = Path.Join(MetadataDirectory, $"{appName}.json");
        var metadata = (await LauncherJson.ReadIfExistsAsync(fileSystem, metadataFile, LauncherJsonContext.Default.LegendaryMetadataFile,
            cancellationToken).ConfigureAwait(false))?.Metadata;
        if (metadata is null || LauncherJson.NullIfBlank(metadata.Namespace) is not { } catalogNamespace)
        {
            throw new InvalidDataException("An installed app has no catalog namespace.");
        }

        var resume = fileSystem.GetEntry(Path.Join(temporary, $"{appName}.resume"));
        var repair = fileSystem.GetEntry(Path.Join(temporary, $"{appName}.repair"));
        return new EpicInstallRecord(catalogNamespace, path, libraryId)
        {
            AppName = appName,
            CatalogItemId = LauncherJson.NullIfBlank(metadata.Id),
            Name = LauncherJson.NullIfBlank(game.Title),
            BuildId = LauncherJson.NullIfBlank(game.Version),
            IsDlc = game.IsDlc,
            Platform = game.Platform?.ToUpperInvariant() switch
            {
                "WINDOWS" or "WIN32" => OperatingSystemKind.Windows,
                "MAC" => OperatingSystemKind.MacOS,
                _ => OperatingSystemKind.Unknown,
            },
            Launch = heroicDirectory is null ? null : HeroicLauncher.Launch(appName, Runner),
            Activity = repair is not null || game.NeedsVerification ? GameInstallState.Validating
                : resume is not null ? GameInstallState.Updating
                : queued.TryGetValue(appName, out var activity) ? activity
                : null,
            ActivityAt = new[] { resume, repair }.Max(entry => entry?.LastWriteTimeUtc),
        };
    }
}
