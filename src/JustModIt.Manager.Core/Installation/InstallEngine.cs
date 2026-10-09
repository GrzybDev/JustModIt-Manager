using System.Threading.Channels;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Installation;

public sealed record InstallResult(InstallRecord Record, ValueList<InstallWarning> Warnings);

/// <summary>
/// Installs, uninstalls, verifies and recovers packages one operation at a time on a single background queue, so no two
/// operations ever touch game files at once and no caller ever waits on file I/O; recover first after a start.
/// </summary>
public sealed partial class InstallEngine : IInstallEngine, IAsyncDisposable
{
    private readonly InstallEngineOptions _options;
    private readonly PackageReader _packages;
    private readonly FileLockProbe _locks;
    private readonly IGameProbe _games;
    private readonly IVolumes _volumes;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly InstallRecordStore _store;
    private readonly InstallRecordStore _leftovers;
    private readonly LibraryAccess _folderChecks;
    private readonly GameFolderOwnership _ownership;
    private readonly ChangeHook _hook;
    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    private readonly Task _worker;

    /// <summary>Creates the engine and starts its queue.</summary>
    /// <param name="options">The app's data folders and names.</param>
    /// <param name="packages">Opens and validates packages.</param>
    /// <param name="locks">Tells which files are in use, and by whom.</param>
    /// <param name="games">A fresh look at a game right before its files change, which the library service provides.</param>
    /// <param name="volumes">Tells whether originals can be moved or must be copied, and whether a folder is gone for good.</param>
    /// <param name="time">The clock for records and folder checks.</param>
    /// <param name="logger">Logs every operation.</param>
    public InstallEngine(InstallEngineOptions options, PackageReader packages, FileLockProbe locks,
        IGameProbe games, IVolumes volumes, TimeProvider time, ILogger<InstallEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _packages = packages;
        _locks = locks;
        _games = games;
        _volumes = volumes;
        _time = time;
        _logger = logger;
        _store = new InstallRecordStore(options.InstallsDirectory, time);
        _leftovers = new InstallRecordStore(options.LeftoversDirectory, time);
        _folderChecks = new LibraryAccess(time);
        _ownership = new GameFolderOwnership(_store, options);
        _hook = new ChangeHook(options.BeforeChange);
        _worker = Task.Run(ProcessAsync);
    }

    public Task<IReadOnlyList<RecoveredOperation>> RecoverAsync(CancellationToken cancellationToken) =>
        EnqueueAsync(() => new InstallRecovery(_store, _options, _ownership, _volumes, _hook, _logger).RunAsync(cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<GameManagedByOtherAppIssue>> FindOtherAppsAsync(string gameDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        // Only reads, so it need not wait for the operations queued before it, such as a long install of another game.
        return Task.Run(() => _ownership.OtherApps(PathCanonicalizer.Canonicalize(gameDirectory)), cancellationToken);
    }

    public Task<PreflightReport> CheckInstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EnqueueAsync(async () =>
        {
            var package = await _packages.OpenAsync(request.PackagePath, cancellationToken).ConfigureAwait(false);
            await using (package.ConfigureAwait(false))
            {
                var game = OpenGame(request.Game.InstallDirectory);
                var records = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
                var (report, _, _) = await CheckAsync(package, game, request.Game, request.Backup, Guid.NewGuid(), records,
                    replacing: null, baseline: null, progress, cancellationToken).ConfigureAwait(false);
                return report;
            }
        }, cancellationToken);
    }

    public Task<InstallResult> InstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EnqueueAsync(() => InstallCoreAsync(request, progress, cancellationToken), cancellationToken);
    }

    public Task<InstallRecord> MoveBackupsAsync(Guid recordId, BackupChoice destination, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return EnqueueAsync(() => MoveBackupsCoreAsync(recordId, destination, progress, cancellationToken), cancellationToken);
    }

    // Only reads, so it need not wait for the operations queued before it, such as a long install of another game.
    public Task<IReadOnlyList<InstallRecord>> LoadRecordsAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<InstallRecord>>(async () =>
            [.. (await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false)).Select(load => load.Record)
                .OfType<InstallRecord>()],
            cancellationToken);

    public Task<InstalledFiles> LoadFilesAsync(Guid recordId, CancellationToken cancellationToken) =>
        EnqueueAsync(async () => (await LoadAsync(recordId, cancellationToken).ConfigureAwait(false)).Files, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }

    private async Task<InstallResult> InstallCoreAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var package = await _packages.OpenAsync(request.PackagePath, cancellationToken).ConfigureAwait(false);
        await using (package.ConfigureAwait(false))
        {
            var game = OpenGame(request.Game.InstallDirectory);
            using var gameLock = Lock(game);
            var records = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            var id = Guid.NewGuid();
            var (report, plan, backup) = await CheckAsync(package, game, request.Game, request.Backup, id, records, replacing: null,
                baseline: null, progress, cancellationToken).ConfigureAwait(false);
            if (!report.CanProceed)
            {
                throw new InstallBlockedException(report.Issues);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var record = await RunInstallAsync(request, package, game, id, backup, plan, progress, cancellationToken)
                .ConfigureAwait(false);
            return new InstallResult(record, report.Warnings);
        }
    }

    private async Task<InstallRecord> RunInstallAsync(InstallRequest request, IPackage package, GameFolder game, Guid id,
        BackupFolder backup, InstallPlan plan, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var folder = _store.FolderOf(id);
        var gameVolume = _volumes.GetVolumeId(game.Root);
        using var transaction = InstallTransaction.Begin(game, folder, backup, gameVolume, plan, _hook);
        InstallRecord record;
        try
        {
            await _ownership.ClaimAsync(game.Root, cancellationToken).ConfigureAwait(false);
            // Another app may have checked the game at the same time; as each claims it before looking again, one of the
            // two sees the other.
            ThrowIfAny([.. _ownership.OtherApps(game.Root, plan.Paths.Values)]);
            await transaction.ExecuteAsync(package, new ProgressTracker(progress, InstallStage.Installing, package.Steps.Count,
                plan.BytesTotal), cancellationToken).ConfigureAwait(false);
            var files = transaction.Result();
            record = new InstallRecord
            {
                Id = id,
                Game = request.Game.Key,
                GameName = request.Game.Name,
                InstallDirectory = game.Root,
                GameVolume = gameVolume,
                GameBuildId = request.Game.BuildId,
                InstalledAt = _time.GetUtcNow(),
                Package = InstalledPackage.From(package),
                Source = request.Source,
                Backup = backup,
                FileCount = files.Files.Count,
                IsTest = request.IsTest,
            };
            await transaction.CommitAsync(record, files, _store, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            progress?.Report(new InstallProgress(InstallStage.RollingBack, null, 0, 0, 0, 0));
            var rolledBack = await transaction.RollBackAsync(_logger).ConfigureAwait(false);
            if (rolledBack)
            {
                await LeaveAsync(game.Root, id, () => TransactionCleanup.RemoveUncommitted(transaction.Journal.Header, folder))
                    .ConfigureAwait(false);
            }

            _logger.InstallFailed(id, rolledBack, exception);
            if (rolledBack && exception is OperationCanceledException or InstallBlockedException)
            {
                throw;
            }

            throw new InstallFailedException(rolledBack, exception);
        }

        transaction.Tidy(folder, _logger);
        _logger.Installed(package.FileName, request.Game.Key.ToString(), id);
        return record;
    }

    private async Task<InstallRecord> MoveBackupsCoreAsync(Guid recordId, BackupChoice destination,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var folder = _store.FolderOf(recordId);
        var (record, files) = await LoadAsync(recordId, cancellationToken).ConfigureAwait(false);
        var game = GameFolder.TryOpen(record.InstallDirectory, _options.HiddenFolderName);
        var issues = new List<InstallIssue>();
        if (folder.HasJournal)
        {
            issues.Add(new RecoveryPendingIssue(recordId));
        }

        if (destination.Location == BackupLocation.GameFolder && game is null)
        {
            issues.Add(new GameFolderMissingIssue(record.InstallDirectory));
        }

        var target = BackupFolders.For(destination, game?.Root ?? record.InstallDirectory, record.Game, recordId, _options);
        if (target is null)
        {
            issues.Add(new BackupFolderUnavailableIssue(destination.CustomFolder ?? string.Empty));
        }
        else if (PathComparison.AreSame(target.Directory, record.Backup.Directory))
        {
            return record;
        }

        if (BackupsUnavailable(record, files) is { } unavailable)
        {
            issues.Add(unavailable);
        }

        var moves = target is not null && _volumes.AreOnSameVolume(record.Backup.Directory, target.Directory);
        if (target is not null && !moves && InstallPreflight.SpaceIssue(_volumes, target.Directory,
                files.Files.Sum(file => file.Backup is null ? 0 : file.Original?.Size ?? 0)) is { } space)
        {
            issues.Add(space);
        }

        ThrowIfAny(issues);

        using var gameLock = game is null ? null : Lock(game);
        if (game is not null)
        {
            await _ownership.ClaimAsync(game.Root, cancellationToken).ConfigureAwait(false);
        }

        return await BackupRelocation.RunAsync(record, files, target!, folder, _store, moves,
            game?.StagingDirectory(recordId) ?? string.Empty, _hook, progress, _logger, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(PreflightReport Report, InstallPlan Plan, BackupFolder Backup)> CheckAsync(IPackage package, GameFolder game,
        InstalledGame installation, BackupChoice choice, Guid recordId, IReadOnlyList<RecordLoad> records, Guid? replacing,
        Func<string, SimulatedFile?>? baseline, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var issues = PendingOn(game.Root);
        var owners = await OwnersAsync(records, game.Root, replacing, issues, cancellationToken).ConfigureAwait(false);
        var backup = BackupFolders.For(choice, game.Root, installation.Key, recordId, _options);
        if (backup is null)
        {
            issues.Add(new BackupFolderUnavailableIssue(choice.CustomFolder ?? string.Empty));
            backup = BackupFolders.For(BackupChoice.GameFolder, game.Root, installation.Key, recordId, _options)!;
        }

        var preflight = new InstallPreflight(_locks, _volumes, _options.SlowBackupCopyBytes);
        var (report, plan) = await preflight.CheckAsync(package, game, installation, backup, owners, issues, baseline, progress,
            cancellationToken).ConfigureAwait(false);
        var others = _ownership.OtherApps(game.Root, plan.Paths.Values);
        // Asked last, because hashing the files of a large package takes long enough for the store to start an update.
        var probe = await _games.ProbeAsync(installation.Key, cancellationToken).ConfigureAwait(false);
        List<InstallWarning> storeWarnings =
            probe.Game?.Reason == GameStateReason.UnfinishedDownload ? [new UnfinishedStoreDownloadWarning()] : [];
        return (report with
        {
            Issues = [.. report.Issues, .. others, .. ReadinessIssues(probe, forUninstall: false)],
            Warnings = [.. report.Warnings, .. storeWarnings],
        }, plan, backup);
    }

    // An interrupted operation on this game blocks it until recovery has run, and so does a journal that cannot be read at
    // all, since installing over its half-done changes would back them up as originals.
    private List<InstallIssue> PendingOn(string gameRoot) =>
        [.. _store.Folders().Where(folder => InstallRecordStore.IsPendingOn(folder, gameRoot))
            .Select(folder => new RecoveryPendingIssue(folder.Id))];

    // By full path. An install on the game folder that cannot be read is an issue, since it may own any of the files.
    private static async Task<Dictionary<string, Guid>> OwnersAsync(IReadOnlyList<RecordLoad> records, string gameRoot, Guid? except,
        List<InstallIssue> issues, CancellationToken cancellationToken)
    {
        var owners = new Dictionary<string, Guid>(PathComparison.Comparer);
        foreach (var load in records.Where(load => load.Folder.Id != except))
        {
            if (load.Record is not { } record)
            {
                if (load.IsUnreadable && (load.InstallDirectory is null || PathComparison.Overlaps(load.InstallDirectory, gameRoot)))
                {
                    issues.Add(new UnreadableInstallIssue(load.Folder.Id));
                }

                continue;
            }

            if (!PathComparison.Overlaps(record.InstallDirectory, gameRoot))
            {
                continue;
            }

            try
            {
                var files = await InstallRecordStore.LoadFilesAsync(load.Folder, cancellationToken).ConfigureAwait(false);
                foreach (var file in files.Files)
                {
                    owners[PackagePath.ToFullPath(record.InstallDirectory, file.Path)] = record.Id;
                }
            }
            catch (InvalidDataException)
            {
                issues.Add(new UnreadableInstallIssue(record.Id));
            }
        }

        return owners;
    }

    private static IEnumerable<InstallIssue> ReadinessIssues(GameProbe probe, bool forUninstall)
    {
        foreach (var process in probe.RunningProcesses)
        {
            yield return new GameRunningIssue(process.Name);
        }

        if (probe.Game is not { } game)
        {
            if (!forUninstall)
            {
                yield return new GameNotInstalledIssue();
            }

            yield break;
        }

        if (!probe.AllowsChange(forUninstall))
        {
            var (state, reason) = game.TrustedState();
            yield return new GameNotReadyIssue(state, reason);
        }
    }

    // A record that keeps backups needs its backup folder; one on a disconnected drive is never recreated empty.
    private static BackupFolderUnavailableIssue? BackupsUnavailable(InstallRecord record, InstalledFiles files) =>
        BackupFolders.KeepsOriginals(files) && !BackupFolders.IsAvailable(record.Backup.Directory)
            ? new BackupFolderUnavailableIssue(record.Backup.Directory)
            : null;

    private async Task<(InstallRecord Record, InstalledFiles Files)> LoadAsync(Guid recordId, CancellationToken cancellationToken)
    {
        var folder = _store.FolderOf(recordId);
        var load = await InstallRecordStore.LoadAsync(folder, cancellationToken).ConfigureAwait(false);
        if (load.Record is not { } record)
        {
            throw new InstallBlockedException(
                [load.IsUnreadable ? new UnreadableInstallIssue(recordId) : new InstallNotFoundIssue(recordId)]);
        }

        try
        {
            return (record, await InstallRecordStore.LoadFilesAsync(folder, cancellationToken).ConfigureAwait(false));
        }
        catch (InvalidDataException exception)
        {
            throw new InstallBlockedException([new UnreadableInstallIssue(recordId)], exception);
        }
    }

    private GameFolder OpenGame(string directory) =>
        GameFolder.TryOpen(directory, _options.HiddenFolderName)
            ?? throw new InstallBlockedException([new GameFolderMissingIssue(directory)]);

    private static GameFolderLock Lock(GameFolder game)
    {
        try
        {
            return GameFolderLock.TryAcquire(game) ?? throw new InstallBlockedException([new GameBusyIssue()]);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InstallBlockedException([new PermissionRequiredIssue(".")], exception);
        }
    }

    private static void ThrowIfAny(List<InstallIssue> issues)
    {
        if (issues.Count > 0)
        {
            throw new InstallBlockedException(issues);
        }
    }

    private void Tidy(Guid recordId, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.CleanupFailed(recordId, exception);
        }
    }

    // The game is given up before the cleanup removes the journal, so recovery tries again when giving it up fails.
    private async Task LeaveAsync(string gameRoot, Guid recordId, Action? cleanup = null)
    {
        try
        {
            await _ownership.ReleaseAsync(gameRoot, recordId).ConfigureAwait(false);
            cleanup?.Invoke();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.CleanupFailed(recordId, exception);
        }
    }

    private Task<T> EnqueueAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        // A caller that gives up while the operation waits for its turn must not wait for the ones before it.
        var waiting = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        var queued = _queue.Writer.TryWrite(async () =>
        {
            waiting.Dispose();
            if (completion.Task.IsCompleted)
            {
                return;
            }

            try
            {
                completion.TrySetResult(await work().ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        if (!queued)
        {
            waiting.Dispose();
            return Task.FromException<T>(new ObjectDisposedException(nameof(InstallEngine)));
        }

        return completion.Task;
    }

    private async Task ProcessAsync()
    {
        await foreach (var work in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            await work().ConfigureAwait(false);
        }
    }
}
