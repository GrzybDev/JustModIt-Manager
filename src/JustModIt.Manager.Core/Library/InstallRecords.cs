using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Library;

/// <summary>What one launcher or installer recorded about an install; the folder itself is checked afterwards.</summary>
public interface IInstallRecord
{
    /// <summary>The store's id of what the record is for: a game, or DLC or a pack installed into a game's folder.</summary>
    string GameId { get; }

    /// <summary>The folder the record names, as written, so possibly relative or gone.</summary>
    string InstallPath { get; }

    /// <summary>The <see cref="LibraryLocation.Id"/> of the source that holds the record.</summary>
    string LibraryId { get; }

    /// <summary>The record is for DLC, so its id never stands for a game.</summary>
    bool IsDlc { get; }
}

/// <summary>What one install source recorded.</summary>
/// <param name="Libraries">The source's own library, none when the source does not exist on this machine.</param>
/// <param name="Records">The installs it records.</param>
/// <param name="Watch">What to watch so that changes to its records trigger the next scan.</param>
public sealed record SourceScan<TRecord>(ValueList<LibraryLocation> Libraries, IReadOnlyList<TRecord> Records, WatchPlan Watch);

/// <summary>A launcher or installer that records a store's installs; supporting another one is one more source.</summary>
public interface IInstallSource<TRecord>
    where TRecord : IInstallRecord
{
    /// <summary>
    /// Reads the records through <see cref="LibraryScanContext.Access"/>; a source that exists but cannot be read reports
    /// its library as unreadable.
    /// </summary>
    /// <param name="context">The folders the player added for the store and the access that reads each drive.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    Task<SourceScan<TRecord>> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken);
}

/// <summary>What a record turned out to describe once its folder was read.</summary>
/// <param name="GameId">The store's id from the record.</param>
/// <param name="Game">Null for DLC.</param>
/// <param name="Watch">The game's folder, while it exists.</param>
public sealed record RecordFinding(string GameId, InstalledGame? Game, WatchedFolder? Watch = null)
{
    public bool IsDlc => Game is null;

    /// <summary>Lower is more trustworthy: installed and idle, then installed with doubts, then busy, then broken.</summary>
    internal int Rank => Game switch
    {
        { State: GameInstallState.Installed, Reason: GameStateReason.None } => 0,
        { State: GameInstallState.Installed } => 1,
        { State: GameInstallState.Broken } => 3,
        _ => 2,
    };

    /// <summary>Creates the finding of a DLC record, whose id never stands for a game.</summary>
    /// <param name="gameId">The DLC's store id.</param>
    public static RecordFinding Dlc(string gameId) => new(gameId, null);
}

/// <summary>
/// Turns what every source of a store recorded into the store's games, reading the folder each record names, since a
/// record only says where a game should be, and reporting a folder on a drive that does not answer as such.
/// </summary>
/// <param name="store">The store the sources record installs of.</param>
/// <param name="sources">Every launcher or installer that records the store's installs.</param>
/// <param name="logger">Logs sources and records that were skipped.</param>
internal sealed class InstallRecordScanner<TRecord>(StoreId store, IReadOnlyList<IInstallSource<TRecord>> sources, ILogger logger)
    where TRecord : IInstallRecord
{
    /// <summary>Scans every source and reads the folder of each record.</summary>
    /// <param name="context">The folders the player added for the store and the access that reads each drive.</param>
    /// <param name="evaluateFolder">What the record's folder holds; it runs on the folder drive's turn.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    public async Task<StoreScan> ScanAsync(LibraryScanContext context,
        Func<TRecord, CancellationToken, Task<IReadOnlyList<RecordFinding>>> evaluateFolder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scans = await Task.WhenAll(sources.Select(source => ScanSourceAsync(source, context, cancellationToken))).ConfigureAwait(false);
        List<TRecord> records = [.. scans.SelectMany(scan => scan?.Records ?? [])];
        var evaluations = await Task.WhenAll(records.Select(record => EvaluateAsync(record, context.Access, evaluateFolder, cancellationToken)))
            .ConfigureAwait(false);
        List<RecordFinding> findings = [.. evaluations.SelectMany(found => found ?? [])];
        List<TRecord> unanswered = [.. records.Where((_, index) => evaluations[index] is null)];

        var dlcIds = findings.Where(finding => finding.IsDlc).Select(finding => finding.GameId).ToHashSet(StringComparer.Ordinal);
        List<RecordFinding> installs =
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
            .. scans.SelectMany(scan => scan?.Libraries ?? []).DistinctBy(library => library.Id)
                .Select(library => unansweredPerLibrary.TryGetValue(library.Id, out var count)
                    ? library with { UnreadableEntries = library.UnreadableEntries + count }
                    : library),
        ];
        return new StoreScan
        {
            Store = store,
            IsPresent = libraries.Count > 0,
            Libraries = [.. libraries],
            Games = [.. installs.Select(finding => finding.Game!)],
            UnansweredGames = [.. unanswered.Select(record => new UnansweredGame(new GameKey(store, record.GameId), record.InstallPath)).Distinct()],
            IsComplete = scans.All(scan => scan is not null),
            Watch = scans.OfType<SourceScan<TRecord>>().Aggregate(WatchPlan.Empty, (watch, scan) => watch.Merge(scan.Watch))
                .Merge(new WatchPlan([.. installs.Select(finding => finding.Watch).OfType<WatchedFolder>()], [])),
        };
    }

    private async Task<SourceScan<TRecord>?> ScanSourceAsync(IInstallSource<TRecord> source, LibraryScanContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await source.ScanAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Paths hide in the messages, so only the type is logged.
            logger.SourceSkipped(store, source.GetType().Name, exception.GetType().Name);
            return null;
        }
    }

    // Null when the folder the record names did not answer in time.
    private async Task<IReadOnlyList<RecordFinding>?> EvaluateAsync(TRecord record, LibraryAccess access,
        Func<TRecord, CancellationToken, Task<IReadOnlyList<RecordFinding>>> evaluateFolder, CancellationToken cancellationToken)
    {
        if (record.IsDlc)
        {
            return [RecordFinding.Dlc(record.GameId)];
        }

        // A relative path would resolve against the app's current folder.
        if (!Path.IsPathFullyQualified(record.InstallPath))
        {
            logger.RecordSkipped(store, record.GameId, "RelativePath");
            return [];
        }

        try
        {
            return await access.ReadAsync(record.InstallPath, token => evaluateFolder(record, token), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger.FolderNotAnswering(store, record.GameId);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.RecordSkipped(store, record.GameId, exception.GetType().Name);
            return [];
        }
    }

    private static RecordFinding PickInstall(IEnumerable<RecordFinding> findings) =>
        findings
            // Records of one folder differ only in what the launcher reports about it, and one that reports less (an
            // uninstall entry) cannot vouch that no update is running, so the most cautious of them counts.
            .GroupBy(finding => finding.Game!.InstallDirectory, PathComparison.Comparer)
            .Select(folder => folder.MaxBy(finding => finding.Rank)!)
            // A stale record pointing at another folder must not hide a working install listed by another source.
            .MinBy(finding => finding.Rank)!;
}

internal static class InstallFolder
{
    /// <summary>The state of a game whose folder is gone: broken, unless its whole drive is, which is no uninstall.</summary>
    /// <param name="fileSystem">Tells whether the folder's drive exists.</param>
    /// <param name="directory">The game's missing folder.</param>
    public static (GameInstallState State, GameStateReason Reason) MissingState(IFileSystem fileSystem, string directory) =>
        Path.GetPathRoot(directory) is { Length: > 0 } root && fileSystem.DirectoryExists(root)
            ? (GameInstallState.Broken, GameStateReason.InstallDirectoryMissing)
            : (GameInstallState.Offline, GameStateReason.LibraryOffline);
}

internal static partial class InstallRecordLog
{
    /// <summary>Logs a launcher's install records that could not be read.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="store">The store the records are for.</param>
    /// <param name="source">The kind of source.</param>
    /// <param name="error">What failed, without a path.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {Store} install source {Source}: {Error}")]
    public static partial void SourceSkipped(this ILogger logger, StoreId store, string source, string error);

    /// <summary>Logs an install record that was left out.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="store">The game's store.</param>
    /// <param name="gameId">The game's id in that store.</param>
    /// <param name="error">Why it was left out, without a path.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {Store} game {GameId}: {Error}")]
    public static partial void RecordSkipped(this ILogger logger, StoreId store, string gameId, string error);

    /// <summary>Logs a game whose folder did not answer in time.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="store">The game's store.</param>
    /// <param name="gameId">The game's id in that store.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "The folder of {Store} game {GameId} did not answer in time, treating it as offline")]
    public static partial void FolderNotAnswering(this ILogger logger, StoreId store, string gameId);
}
