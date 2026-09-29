using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.Installation;

/// <summary>Backups an install still keeps although the store says its game is gone.</summary>
/// <param name="RecordId">The forgotten install's record id.</param>
/// <param name="Game">The game the install was on.</param>
/// <param name="GameName">The game's name, for the settings page.</param>
/// <param name="Directory">The folder that holds the originals.</param>
/// <param name="Bytes">The originals' total size.</param>
public sealed record LeftoverBackup(Guid RecordId, GameKey Game, string GameName, string Directory, long Bytes);

public sealed partial class InstallEngine
{
    public Task<PreflightReport> PredictReplacementAsync(Guid recordId, InstallRequest replacement,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return EnqueueAsync(() => PredictCoreAsync(recordId, replacement, progress, cancellationToken), cancellationToken);
    }

    public Task<UninstallResult> UninstallAsync(Guid recordId, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) =>
        EnqueueAsync(() => UninstallCoreAsync(recordId, progress, cancellationToken), cancellationToken);

    public Task<VerificationResult> VerifyAsync(Guid recordId, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) =>
        EnqueueAsync(async () =>
        {
            var (record, files) = await LoadAsync(recordId, cancellationToken).ConfigureAwait(false);
            var game = OpenGame(record.InstallDirectory);
            var tracker = new ProgressTracker(progress, InstallStage.Verifying, files.Files.Count, FileInspector.BytesToHash(files));
            var inspected = await FileInspector.InspectAllAsync(files, game, tracker, cancellationToken).ConfigureAwait(false);
            return new VerificationResult([.. inspected.Select(file => new FileVerification(file.File.Path, file.File.Change,
                file.Integrity))]);
        }, cancellationToken);

    public Task<IReadOnlyList<LeftoverBackup>> FindLeftoverBackupsAsync(Func<InstallRecord, bool> isGameGone,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(isGameGone);
        return EnqueueAsync<IReadOnlyList<LeftoverBackup>>(async () =>
        {
            var leftovers = new List<LeftoverBackup>();
            foreach (var load in await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (load.Record is not { } record || record.Backup.Location == BackupLocation.GameFolder
                    || Directory.Exists(record.InstallDirectory) || !Directory.Exists(record.Backup.Directory)
                    || !isGameGone(record))
                {
                    continue;
                }

                InstalledFiles files;
                try
                {
                    files = await InstallRecordStore.LoadFilesAsync(load.Folder, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidDataException exception)
                {
                    _logger.UnreadableFileList(record.Id, exception);
                    continue;
                }

                var bytes = files.Files
                    .Select(file => file.Backup is { } name ? new FileInfo(Path.Join(record.Backup.Directory, name)) : null)
                    .Sum(file => file is { Exists: true } ? file.Length : 0);
                leftovers.Add(new LeftoverBackup(record.Id, record.Game, record.GameName, record.Backup.Directory, bytes));
            }

            return leftovers;
        }, cancellationToken);
    }

    public Task ForgetAsync(Guid recordId, CancellationToken cancellationToken) =>
        EnqueueAsync(() => ForgetCoreAsync(recordId, onlyWhenGone: false, cancellationToken), cancellationToken);

    public Task<bool> ForgetIfGoneAsync(Guid recordId, CancellationToken cancellationToken) =>
        EnqueueAsync(() => ForgetCoreAsync(recordId, onlyWhenGone: true, cancellationToken), cancellationToken);

    private async Task<bool> ForgetCoreAsync(Guid recordId, bool onlyWhenGone, CancellationToken cancellationToken)
    {
        var folder = _store.FolderOf(recordId);
        if (folder.HasJournal)
        {
            throw new InstallBlockedException([new RecoveryPendingIssue(recordId)]);
        }

        var (record, files) = await LoadAsync(recordId, cancellationToken).ConfigureAwait(false);
        if (onlyWhenGone && !GameFolder.IsGoneForGood(record.InstallDirectory, record.GameVolume, _volumes))
        {
            return false;
        }

        _hook.Before(ChangeKind.Record);
        var removed = BackupFolders.DeleteListed(record.Backup.Directory, files);
        InstallRecordStore.DeleteFolder(folder);
        _logger.Forgotten(recordId, removed);
        return true;
    }

    private async Task<PreflightReport> PredictCoreAsync(Guid recordId, InstallRequest replacement,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var package = await _packages.OpenAsync(replacement.PackagePath, cancellationToken).ConfigureAwait(false);
        await using (package.ConfigureAwait(false))
        {
            var (record, files) = await LoadAsync(recordId, cancellationToken).ConfigureAwait(false);
            var game = OpenGame(record.InstallDirectory);
            var plan = await Uninstaller.PlanAsync(record, files, game, progress: null, cancellationToken).ConfigureAwait(false);
            var afterUninstall = plan.Steps.ToDictionary(step => step.Path, Uninstaller.AfterUndo, PathComparison.Comparer);
            var records = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            var (report, _, _) = await CheckAsync(package, game, replacement.Game, replacement.Backup, Guid.NewGuid(), records,
                replacing: recordId, path => afterUninstall.TryGetValue(path, out var file) ? file : SimulatedFile.OnDisk(path),
                progress, cancellationToken).ConfigureAwait(false);
            return BackupsUnavailable(record, files) is { } unavailable
                ? report with { Issues = report.Issues.Add(unavailable) }
                : report;
        }
    }

    private async Task<UninstallResult> UninstallCoreAsync(Guid recordId, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var folder = _store.FolderOf(recordId);
        var (record, files) = await LoadAsync(recordId, cancellationToken).ConfigureAwait(false);
        var game = OpenGame(record.InstallDirectory);
        var issues = new List<InstallIssue>();
        if (folder.HasJournal)
        {
            issues.Add(new RecoveryPendingIssue(recordId));
        }

        if (BackupsUnavailable(record, files) is { } unavailable)
        {
            issues.Add(unavailable);
        }

        ThrowIfAny(issues);
        using var gameLock = Lock(game);
        var plan = await Uninstaller.PlanAsync(record, files, game, progress, cancellationToken).ConfigureAwait(false);
        var changed = plan.Steps.Select(step => step.Path).Where(File.Exists);
        var directories = plan.Steps.Select(step => Path.GetDirectoryName(step.Path)!).Where(Directory.Exists)
            .Distinct(PathComparison.Comparer);
        issues.AddRange(InstallPreflight.AccessIssues(_locks.Probe(changed, cancellationToken), directories, game));
        var movesBack = _volumes.AreOnSameVolume(record.Backup.Directory, game.Root);
        // Originals from another drive are copied next to the installed files they replace before those go.
        var copiedBack = plan.Steps.Where(step => step.Action == UndoAction.Restore).Sum(step => step.File.Original!.Size);
        if (!movesBack && copiedBack > 0 && InstallPreflight.SpaceIssue(_volumes, game.Root, copiedBack) is { } space)
        {
            issues.Add(space);
        }

        issues.AddRange(ReadinessIssues(await _games.ProbeAsync(record.Game, cancellationToken).ConfigureAwait(false),
            forUninstall: true));
        ThrowIfAny(issues);

        var header = new TransactionStarted(TransactionStarted.CurrentFormatVersion, TransactionKind.Uninstall, recordId, game.Root,
            game.StagingDirectory(recordId), record.Backup.Directory, record.GameVolume);
        IReadOnlyList<KeptFile> kept;
        using (var journal = TransactionJournal.Create(folder.JournalFile, header))
        {
            try
            {
                kept = await Uninstaller.RunAsync(plan, record, files, game, journal, movesBack, _hook, progress, _logger,
                    cancellationToken).ConfigureAwait(false);
                // Removing the record commits the uninstall; what follows only tidies up.
                _hook.Before(ChangeKind.Record);
                File.Delete(folder.RecordFile);
            }
            catch (Exception exception)
            {
                progress?.Report(new InstallProgress(InstallStage.RollingBack, null, 0, 0, 0, 0));
                journal.Commit();
                var rolledBack = await JournalRollback.UndoAsync(journal.Entries, game, _hook, _logger).ConfigureAwait(false);
                journal.Dispose();
                if (rolledBack)
                {
                    Tidy(recordId, () => TransactionCleanup.RemoveStaging(header.StagingDirectory));
                    Tidy(recordId, () => File.Delete(folder.JournalFile));
                }

                _logger.UninstallFailed(recordId, rolledBack, exception);
                if (rolledBack && exception is OperationCanceledException)
                {
                    throw;
                }

                throw new InstallFailedException(rolledBack, exception);
            }
        }

        Tidy(recordId, () =>
        {
            _hook.Before(ChangeKind.Tidy);
            Uninstaller.Finish(header, folder, files);
        });
        _logger.Uninstalled(recordId, kept.Count);
        return new UninstallResult([.. kept]);
    }
}
