using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>
/// Finds GOG games through every launcher and installer that records them, letting the <c>goggame-&lt;id&gt;.info</c>
/// files in each folder decide what is there, since a record only says where a game should be.
/// </summary>
public sealed class GogLibraryProvider : ILibraryProvider
{
    private static readonly string[] GameFolderEntries = [GogInfoReader.InfoPattern, .. GogStateResolver.DownloadMarkers];

    private readonly IFileSystem _fileSystem;
    private readonly InstallRecordScanner<GogInstallRecord> _scanner;
    private readonly GogInfoReader _infos;
    private readonly GogFolderSource _folders;

    /// <summary>Creates a provider that reads the given launchers and installers.</summary>
    /// <param name="fileSystem">Reads the launchers' files and the games' folders.</param>
    /// <param name="sources">Launchers and installers, in order of precedence; folders the player added are always read.</param>
    /// <param name="platform">The system the app runs on, taken as the system of games in folders the player added.</param>
    /// <param name="logger">Logs sources and records that were skipped.</param>
    public GogLibraryProvider(IFileSystem fileSystem, IEnumerable<IInstallSource<GogInstallRecord>> sources, AppPlatform platform,
        ILogger<GogLibraryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _folders = new GogFolderSource(fileSystem, platform);
        _scanner = new InstallRecordScanner<GogInstallRecord>(Store, [.. sources, _folders], logger);
        _infos = new GogInfoReader(fileSystem);
    }

    public StoreId Store => StoreId.Gog;

    /// <summary>
    /// Creates the provider for the sources of this machine: the registry and Galaxy's database on Windows, then every
    /// Heroic installation.
    /// </summary>
    /// <param name="fileSystem">Reads the launchers' files and the games' folders.</param>
    /// <param name="registry">Reads the installers' and Galaxy's registry records.</param>
    /// <param name="sqlite">Reads Galaxy's databases.</param>
    /// <param name="environment">The user's folders the launchers are found relative to.</param>
    /// <param name="logger">Logs sources and records that were skipped.</param>
    public static GogLibraryProvider Create(IFileSystem fileSystem, IRegistry registry, ISqliteReader sqlite, LibraryEnvironment environment,
        ILogger<GogLibraryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        List<IInstallSource<GogInstallRecord>> sources = environment.Platform == AppPlatform.Windows
            ? [new GogRegistrySource(registry, GalaxyDatabase.Locate(fileSystem, sqlite, environment))]
            : [];
        sources.AddRange(HeroicGogSource.StoreDirectories(environment)
            .Select(directory => new HeroicGogSource(fileSystem, directory)));
        return new GogLibraryProvider(fileSystem, sources, environment.Platform, logger);
    }

    public Task<StoreScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken) =>
        _scanner.ScanAsync(context, EvaluateFolderAsync, cancellationToken);

    public async Task<InstalledGame?> ProbeAsync(InstalledGame game, LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        // Whether a record is DLC depends on every other record, so the probe reads them all; there are only a few.
        return (await ScanAsync(context, cancellationToken).ConfigureAwait(false)).Describe(game);
    }

    public async Task<LibraryFolderEvidence> InspectFolderAsync(string path, CancellationToken cancellationToken)
    {
        var folder = _fileSystem.GetCanonicalPath(path);
        var infos = await _infos.ReadAsync(folder, cancellationToken).ConfigureAwait(false);
        if (infos.Base is null && infos.Entries.Count > 0)
        {
            return LibraryFolderEvidence.Rejected(LibraryFolderRejection.DlcOnly);
        }

        await foreach (var _ in _folders.FindGamesAsync(folder, cancellationToken).ConfigureAwait(false))
        {
            return LibraryFolderEvidence.Accepted(folder);
        }

        return LibraryFolderEvidence.None;
    }

    private async Task<IReadOnlyList<RecordFinding>> EvaluateFolderAsync(GogInstallRecord record, CancellationToken cancellationToken)
    {
        var directory = _fileSystem.GetCanonicalPath(record.InstallPath);
        var infos = await _infos.ReadAsync(directory, cancellationToken).ConfigureAwait(false);
        if (infos.Find(record.GameId) is { IsDlc: true })
        {
            return [RecordFinding.Dlc(record.GameId)];
        }

        // DLC installers record their base game's folder and not all of them set dependsOn; a pack or edition id
        // does the same. Either way the folder's own base game is what is installed there.
        if (infos.Base is { } folderGame && folderGame.GameId != record.GameId)
        {
            return
            [
                RecordFinding.Dlc(record.GameId),
                Describe(record with { GameId = folderGame.GameId, BuildId = null, Name = null }, directory, folderGame),
            ];
        }

        return [Describe(record, directory, infos.Base)];
    }

    private RecordFinding Describe(GogInstallRecord record, string directory, GogGameInfo? info)
    {
        var (state, reason) = GogStateResolver.Resolve(_fileSystem, directory, record, info);
        var languages = new List<string>(info?.Languages ?? []);
        if (LanguageNames.FromTagOrName(record.Language) is { } recorded
            && !languages.Any(tag => tag.Split('-')[0].Equals(recorded.Split('-')[0], StringComparison.OrdinalIgnoreCase)))
        {
            languages.Add(recorded);
        }

        // The registry name is in the installer's language and may be ASCII-folded; the .info name is proper UTF-8.
        var game = new InstalledGame
        {
            Key = new GameKey(StoreId.Gog, record.GameId),
            Name = info?.Name ?? record.Name ?? record.GameId,
            InstallDirectory = directory,
            LibraryId = record.LibraryId,
            BuildId = info?.BuildId ?? record.BuildId,
            State = state,
            Reason = reason,
            StoreActivityAt = info?.LastWriteTime,
            Platform = record.Platform,
            Languages = [.. languages],
            Launch = record.Launch ?? info?.PlayTask,
        };
        var watch = _fileSystem.DirectoryExists(directory) ? new WatchedFolder(directory, GameFolderEntries) : null;
        return new RecordFinding(record.GameId, game, watch);
    }
}
