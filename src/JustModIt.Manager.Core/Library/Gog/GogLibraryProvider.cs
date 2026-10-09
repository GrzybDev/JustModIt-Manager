using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>
/// Finds GOG games through every launcher and installer that records them. Each record only says where a game should
/// be; the <c>goggame-&lt;id&gt;.info</c> files in the folder decide what is there.
/// </summary>
public sealed partial class GogLibraryProvider : ILibraryProvider
{
    private static readonly string[] GameFolderEntries = [GogInfoReader.InfoPattern, .. GogStateResolver.DownloadMarkers];

    private readonly IFileSystem _fileSystem;
    private readonly IReadOnlyList<IGogInstallSource> _sources;
    private readonly GogInfoReader _infos;
    private readonly GogFolderSource _folders;
    private readonly ILogger _logger;

    /// <param name="fileSystem">Reads the launchers' files and the games' folders.</param>
    /// <param name="sources">Launchers and installers, in order of precedence; folders the player added are always read.</param>
    /// <param name="platform">The system the app runs on, taken as the system of games in folders the player added.</param>
    /// <param name="logger">Logs sources and records that were skipped.</param>
    public GogLibraryProvider(IFileSystem fileSystem, IEnumerable<IGogInstallSource> sources, AppPlatform platform,
        ILogger<GogLibraryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _folders = new GogFolderSource(fileSystem, platform);
        _sources = [.. sources, _folders];
        _infos = new GogInfoReader(fileSystem);
        _logger = logger;
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
        List<IGogInstallSource> sources = environment.Platform == AppPlatform.Windows
            ? [new GogRegistrySource(registry, GalaxyDatabase.Locate(fileSystem, sqlite, environment))]
            : [];
        sources.AddRange(HeroicGogSource.StoreDirectories(environment)
            .Select(directory => new HeroicGogSource(fileSystem, directory)));
        return new GogLibraryProvider(fileSystem, sources, environment.Platform, logger);
    }

    public async Task<StoreScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sources = await Task.WhenAll(_sources.Select(source => ScanSourceAsync(source, context, cancellationToken))).ConfigureAwait(false);
        List<GogInstallRecord> records = [.. sources.SelectMany(source => source?.Records ?? [])];
        var evaluations = await Task.WhenAll(records.Select(record => EvaluateAsync(record, context.Access, cancellationToken)))
            .ConfigureAwait(false);
        List<Finding> findings = [.. evaluations.SelectMany(found => found ?? [])];
        List<GogInstallRecord> unanswered = [.. records.Where((_, index) => evaluations[index] is null)];

        var dlcIds = findings.Where(finding => finding.IsDlc).Select(finding => finding.GameId).ToHashSet(StringComparer.Ordinal);
        List<Finding> installs =
        [
            .. findings
                .Where(finding => !finding.IsDlc && !dlcIds.Contains(finding.GameId))
                .GroupBy(finding => finding.GameId, StringComparer.Ordinal)
                .Select(PickInstall),
        ];

        // Counted as unreadable records, so this scan cannot confirm that a game on a drive that did not answer is gone.
        var unansweredPerLibrary = unanswered.CountBy(record => record.LibraryId).ToDictionary(StringComparer.Ordinal);
        List<LibraryLocation> libraries =
        [
            .. sources.SelectMany(source => source?.Libraries ?? []).DistinctBy(library => library.Id)
                .Select(library => unansweredPerLibrary.TryGetValue(library.Id, out var count)
                    ? library with { UnreadableEntries = library.UnreadableEntries + count }
                    : library),
        ];
        return new StoreScan
        {
            Store = Store,
            IsPresent = libraries.Count > 0,
            Libraries = [.. libraries],
            Games = [.. installs.Select(finding => finding.Game!)],
            UnansweredGames = [.. unanswered.Select(record => new UnansweredGame(new GameKey(Store, record.GameId), record.InstallPath)).Distinct()],
            IsComplete = sources.All(source => source is not null),
            Watch = sources.OfType<GogSourceScan>().Aggregate(WatchPlan.Empty, (watch, source) => watch.Merge(source.Watch))
                .Merge(new WatchPlan([.. installs.Select(finding => finding.Watch).OfType<WatchedFolder>()], [])),
        };
    }

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

    private async Task<GogSourceScan?> ScanSourceAsync(IGogInstallSource source, LibraryScanContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await source.ScanAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Paths hide in the messages, so only the type is logged.
            LogSourceFailed(source.GetType().Name, exception.GetType().Name);
            return null;
        }
    }

    /// <returns>Null when the folder the record names did not answer in time.</returns>
    private async Task<IReadOnlyList<Finding>?> EvaluateAsync(GogInstallRecord record, LibraryAccess access, CancellationToken cancellationToken)
    {
        if (record.IsDlc)
        {
            return [Finding.Dlc(record.GameId)];
        }

        // A relative path would resolve against the app's current folder.
        if (!Path.IsPathFullyQualified(record.InstallPath))
        {
            LogRecordFailed(record.GameId, "RelativePath");
            return [];
        }

        try
        {
            return await access.ReadAsync(record.InstallPath, token => EvaluateFolderAsync(record, token), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            LogFolderNotAnswering(record.GameId);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogRecordFailed(record.GameId, exception.GetType().Name);
            return [];
        }
    }

    private async Task<IReadOnlyList<Finding>> EvaluateFolderAsync(GogInstallRecord record, CancellationToken cancellationToken)
    {
        var directory = _fileSystem.GetCanonicalPath(record.InstallPath);
        var infos = await _infos.ReadAsync(directory, cancellationToken).ConfigureAwait(false);
        if (infos.Find(record.GameId) is { IsDlc: true })
        {
            return [Finding.Dlc(record.GameId)];
        }

        // DLC installers record their base game's folder and not all of them set dependsOn; a pack or edition id
        // does the same. Either way the folder's own base game is what is installed there.
        if (infos.Base is { } folderGame && folderGame.GameId != record.GameId)
        {
            return [Finding.Dlc(record.GameId), Describe(record with { GameId = folderGame.GameId, BuildId = null, Name = null }, directory, folderGame)];
        }

        return [Describe(record, directory, infos.Base)];
    }

    private Finding Describe(GogInstallRecord record, string directory, GogGameInfo? info)
    {
        var state = GogStateResolver.Resolve(_fileSystem, directory, record, info);
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
            State = state.State,
            Reason = state.Reason,
            StoreActivityAt = info?.LastWriteTime,
            Platform = record.Platform,
            Languages = [.. languages],
            Launch = record.Launch ?? info?.PlayTask,
        };
        var watch = _fileSystem.DirectoryExists(directory) ? new WatchedFolder(directory, GameFolderEntries) : null;
        return new Finding(record.GameId, IsDlc: false, game, state, watch);
    }

    private static Finding PickInstall(IEnumerable<Finding> findings) =>
        findings
            // Records of one folder differ only in the build they expect, and one without a build (an uninstall entry)
            // cannot vouch that no update is running, so the most cautious of them counts.
            .GroupBy(finding => finding.Game!.InstallDirectory, PathComparison.Comparer)
            .Select(folder => folder.MaxBy(finding => finding.State.Rank)!)
            // A stale record pointing at another folder must not hide a working install listed by another source.
            .MinBy(finding => finding.State.Rank)!;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped GOG install source {Source}: {Error}")]
    private partial void LogSourceFailed(string source, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped GOG game {GameId}: {Error}")]
    private partial void LogRecordFailed(string gameId, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The folder of GOG game {GameId} did not answer in time, treating it as offline")]
    private partial void LogFolderNotAnswering(string gameId);

    /// <param name="GameId">The GOG product id that the record names.</param>
    /// <param name="IsDlc">Whether the product is DLC, which is never a game.</param>
    /// <param name="Game">The game as the record describes it; null for DLC.</param>
    /// <param name="State">The install's state; the most cautious record of a folder wins.</param>
    /// <param name="Watch">The game's folder, while it exists.</param>
    private sealed record Finding(string GameId, bool IsDlc, InstalledGame? Game, GogState State, WatchedFolder? Watch = null)
    {
        public static Finding Dlc(string gameId) => new(gameId, IsDlc: true, null, default);
    }
}
