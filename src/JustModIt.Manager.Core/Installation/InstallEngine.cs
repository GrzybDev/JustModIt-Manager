using System.Text.Json;
using System.Threading.Channels;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Installation;

public sealed record InstallResult(InstallRecord Record, ValueList<InstallWarning> Warnings);

/// <summary>
/// Installs packages and recovers interrupted operations, one operation at a time on a single background queue, so no
/// two operations ever touch game files at once and no caller ever waits on file I/O. Recover first after a start.
/// </summary>
public sealed class InstallEngine : IAsyncDisposable
{
    private readonly InstallEngineOptions _options;
    private readonly IPackageReader _packages;
    private readonly IPatchDecoder _decoder;
    private readonly IFileLockProbe _locks;
    private readonly IGameProbe _games;
    private readonly IVolumes _volumes;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly InstallRecordStore _store;
    private readonly ChangeHook _hook;
    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
    {
        SingleReader = true,
    });

    private readonly Task _worker;

    /// <summary>Creates the engine and starts its queue.</summary>
    /// <param name="options">The app's data folders and names.</param>
    /// <param name="packages">Opens and validates packages.</param>
    /// <param name="decoder">Applies the patches of manifest packages.</param>
    /// <param name="locks">Tells which files are in use, and by whom.</param>
    /// <param name="games">A fresh look at a game right before its files change, which the library service provides.</param>
    /// <param name="volumes">Tells whether originals can be moved or must be copied, and whether a folder is gone for good.</param>
    /// <param name="time">The clock for records and folder checks.</param>
    /// <param name="logger">Logs every operation.</param>
    public InstallEngine(InstallEngineOptions options, IPackageReader packages, IPatchDecoder decoder, IFileLockProbe locks,
        IGameProbe games, IVolumes volumes, TimeProvider time, ILogger<InstallEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _packages = packages;
        _decoder = decoder;
        _locks = locks;
        _games = games;
        _volumes = volumes;
        _time = time;
        _logger = logger;
        _store = new InstallRecordStore(options.InstallsDirectory, time);
        _hook = new ChangeHook(options.BeforeChange);
        _worker = Task.Run(ProcessAsync);
    }

    /// <summary>Undoes or finishes what a crash interrupted. Meant to be queued first after a start.</summary>
    public Task<IReadOnlyList<RecoveredOperation>> RecoverAsync(CancellationToken cancellationToken) =>
        EnqueueAsync(() => new InstallRecovery(_store, _options, _volumes, _hook, _logger).RunAsync(cancellationToken),
            cancellationToken);

    /// <summary>Everything that would stop the install, and what to tell the player, without changing anything.</summary>
    /// <exception cref="InvalidPackageException">The package is malformed, unsafe or made for a newer app.</exception>
    /// <exception cref="InstallBlockedException">The game folder is gone.</exception>
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

    /// <exception cref="InvalidPackageException">The package is malformed, unsafe or made for a newer app.</exception>
    /// <exception cref="InstallBlockedException">Refused before any change.</exception>
    /// <exception cref="InstallFailedException">Failed part-way; the changes were undone unless it says otherwise.</exception>
    public Task<InstallResult> InstallAsync(InstallRequest request, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EnqueueAsync(() => InstallCoreAsync(request, progress, cancellationToken), cancellationToken);
    }

    /// <summary>Moves an install's backups to where the settings now keep them.</summary>
    /// <exception cref="InstallBlockedException">Refused before any change.</exception>
    public Task<InstallRecord> MoveBackupsAsync(Guid recordId, BackupChoice destination, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return EnqueueAsync(() => MoveBackupsCoreAsync(recordId, destination, progress, cancellationToken), cancellationToken);
    }

    /// <summary>The installs the app can read.</summary>
    public Task<IReadOnlyList<InstallRecord>> LoadRecordsAsync(CancellationToken cancellationToken) =>
        EnqueueAsync<IReadOnlyList<InstallRecord>>(async () =>
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
        using var transaction = InstallTransaction.Begin(game, folder, backup, gameVolume, plan, _decoder, _hook);
        InstallRecord record;
        try
        {
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
            };
            await transaction.CommitAsync(record, files, _store, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            progress?.Report(new InstallProgress(InstallStage.RollingBack, null, 0, 0, 0, 0));
            var rolledBack = await transaction.RollBackAsync(_logger).ConfigureAwait(false);
            if (rolledBack)
            {
                Tidy(id, () => TransactionCleanup.RemoveUncommitted(transaction.Journal.Header, folder));
            }

            _logger.InstallFailed(id, rolledBack, exception);
            if (rolledBack && exception is OperationCanceledException)
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

        ThrowIfAny(issues);
        var moves = _volumes.AreOnSameVolume(record.Backup.Directory, target!.Directory);
        if (!moves)
        {
            var bytes = files.Files.Sum(file => file.Backup is null ? 0 : file.Original?.Size ?? 0);
            if (_volumes.GetAvailableBytes(target.Directory) is { } available && available < bytes)
            {
                throw new InstallBlockedException([new NotEnoughSpaceIssue(target.Directory, bytes, available)]);
            }
        }

        using var gameLock = game is null ? null : Lock(game);
        return await BackupRelocation.RunAsync(record, files, target, folder, _store, moves,
            game?.StagingDirectory(recordId) ?? string.Empty, _hook, progress, _logger, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(PreflightReport Report, InstallPlan Plan, BackupFolder Backup)> CheckAsync(IPackage package, GameFolder game,
        InstalledGame installation, BackupChoice choice, Guid recordId, IReadOnlyList<RecordLoad> records, Guid? replacing,
        Func<string, SimulatedFile?>? baseline, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var issues = PendingOn(game.Root);
        var owners = new Dictionary<string, Guid>(PathComparison.Comparer);
        foreach (var load in records.Where(load => load.Folder.Id != replacing))
        {
            if (load.Record is not { } record)
            {
                if (load.IsUnreadable && OverlapsUnreadable(load.Folder, game.Root))
                {
                    issues.Add(new UnreadableInstallIssue(load.Folder.Id));
                }

                continue;
            }

            if (!PathComparison.Overlaps(record.InstallDirectory, game.Root))
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

        var backup = BackupFolders.For(choice, game.Root, installation.Key, recordId, _options);
        if (backup is null)
        {
            issues.Add(new BackupFolderUnavailableIssue(choice.CustomFolder ?? string.Empty));
            backup = BackupFolders.For(BackupChoice.GameFolder, game.Root, installation.Key, recordId, _options)!;
        }

        var preflight = new InstallPreflight(_locks, _volumes, _options.SlowBackupCopyBytes);
        var (report, plan) = await preflight.CheckAsync(package, game, installation, backup, owners, issues, progress,
            cancellationToken, baseline).ConfigureAwait(false);
        // Asked last, because hashing the files of a large package takes long enough for the store to start an update.
        var readiness = ReadinessIssues(await _games.ProbeAsync(installation.Key, cancellationToken).ConfigureAwait(false),
            forUninstall: false);
        return (report with { Issues = [.. report.Issues, .. readiness] }, plan, backup);
    }

    /// <summary>
    /// An interrupted operation on this game blocks it until recovery has run, and so does a journal that cannot be read
    /// at all, since installing over its half-done changes would back them up as originals.
    /// </summary>
    private List<InstallIssue> PendingOn(string gameRoot)
    {
        var issues = new List<InstallIssue>();
        foreach (var folder in _store.Folders().Where(folder => folder.HasJournal))
        {
            try
            {
                if (TransactionJournal.ReadHeader(folder.JournalFile) is { } header
                    && PathComparison.Overlaps(header.GameDirectory, gameRoot))
                {
                    issues.Add(new RecoveryPendingIssue(folder.Id));
                }
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                issues.Add(new RecoveryPendingIssue(folder.Id));
            }
        }

        return issues;
    }

    // A record this version cannot read still names its game folder, unless it is damaged beyond that.
    private static bool OverlapsUnreadable(InstallFolder folder, string gameRoot)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(folder.RecordFile));
            return document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("installDirectory", out var directory)
                || directory.ValueKind != JsonValueKind.String
                || PathComparison.Overlaps(directory.GetString()!, gameRoot);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException
            or ArgumentException)
        {
            return true;
        }
    }

    // Only a game its store reports installed and idle, with nothing running from its folder, may change. Putting originals
    // back is also what a waiting update or a repair of broken files needs, so an uninstall may run then.
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

        var (state, reason) = game.IsStale ? (GameInstallState.Unknown, GameStateReason.UnreadableStoreData) : (game.State, game.Reason);
        var ready = state == GameInstallState.Installed
            || (state == GameInstallState.Running && probe.RunningProcesses.Count > 0)
            || (forUninstall && state is GameInstallState.UpdatePending or GameInstallState.Broken);
        if (!ready)
        {
            yield return new GameNotReadyIssue(state, reason);
        }
    }

    // A record that keeps backups needs its backup folder; one on a disconnected drive is never recreated empty.
    private static BackupFolderUnavailableIssue? BackupsUnavailable(InstallRecord record, InstalledFiles files) =>
        files.Files.Any(file => file.Backup is not null) && !BackupFolders.IsAvailable(record.Backup.Directory)
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
