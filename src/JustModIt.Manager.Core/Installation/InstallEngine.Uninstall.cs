using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// Originals that an install forgotten with its game left outside the game folder, or copies of originals an uninstall
/// kept of files a package's software changed.
/// </summary>
/// <param name="RecordId">The forgotten or uninstalled install's record id.</param>
/// <param name="Game">The game the install was on.</param>
/// <param name="GameName">The game's name, for the settings page.</param>
/// <param name="Directory">The folder that holds the originals.</param>
/// <param name="Bytes">The originals' total size.</param>
public sealed record LeftoverBackup(Guid RecordId, GameKey Game, string GameName, string Directory, long Bytes)
{
    /// <summary>The game's folder the install was in.</summary>
    public string GameDirectory { get; init; } = "";

    /// <summary>The cover of the install's project; null when its record names none.</summary>
    public Uri? Cover { get; init; }

    /// <summary>What an uninstall left in the game, for copies of that kind; null for a forgotten install's backups.</summary>
    public KeptFilesSummary? Kept { get; init; }
}

/// <summary>The files an uninstall left as a package's software changed them, whose originals' copies it kept.</summary>
/// <param name="Paths">The files, game-relative, at most <see cref="MaxPaths"/> of them.</param>
/// <param name="FileCount">How many files it left, also beyond those listed.</param>
/// <param name="KeptAt">When the uninstall left them.</param>
/// <param name="Reason">
/// Why it left them, the most pressing reason when there are several; null for files a replacement that did not finish handed
/// over.
/// </param>
/// <param name="ProjectName">The uninstalled install's project; null when its record names none.</param>
public sealed record KeptFilesSummary(IReadOnlyList<string> Paths, int FileCount, DateTimeOffset KeptAt, KeptReason? Reason = null,
    string? ProjectName = null)
{
    public const int MaxPaths = 80;
}

public sealed partial class InstallEngine
{
    public Task<PreflightReport> PredictReplacementAsync(Guid recordId, InstallRequest replacement,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        return EnqueueAsync(() => PredictCoreAsync(recordId, replacement, progress, cancellationToken), cancellationToken);
    }

    public Task<UninstallResult> UninstallAsync(Guid recordId, IReadOnlyList<string>? replacedBy, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) =>
        EnqueueAsync(() => UninstallCoreAsync(recordId, replacedBy, progress, cancellationToken), cancellationToken);

    public Task<VerificationResult> VerifyAsync(Guid recordId, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken) =>
        EnqueueAsync(async () =>
        {
            var (record, files) = await LoadAsync(recordId, cancellationToken).ConfigureAwait(false);
            var game = OpenGame(record.InstallDirectory);
            var tracker = new ProgressTracker(progress, InstallStage.Verifying, FileInspector.Checked(files).Count(),
                FileInspector.BytesToHash(files));
            var inspected = await FileInspector.InspectAllAsync(files, game, tracker, cancellationToken).ConfigureAwait(false);
            return new VerificationResult([.. inspected.Select(file => new FileVerification(file.File.Path, file.File.Change,
                file.Integrity))]);
        }, cancellationToken);

    // Leftovers are read and removed apart from the queue, which a long install may hold: forgetting adds whole folders at
    // once, and an uninstall writes its kept-files record whole before it commits, which the listing waits for by leaving
    // out a record whose install is still there.
    public Task<IReadOnlyList<LeftoverBackup>> FindLeftoverBackupsAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<LeftoverBackup>>(async () =>
        {
            var leftovers = new List<LeftoverBackup>();
            foreach (var load in await _leftovers.LoadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                // A record still among the installs belongs to an uninstall that has not committed, or was undone.
                if (load.Record is not { } record || _store.FolderOf(record.Id).HasRecord
                    || await LoadLeftoverFilesAsync(load.Folder, record, cancellationToken).ConfigureAwait(false) is not { } files)
                {
                    continue;
                }

                if (files.KeptAt is not null && record.Backup.Location == BackupLocation.GameFolder
                    && await IsGoneForGoodAsync(record, cancellationToken).ConfigureAwait(false))
                {
                    // Its copies went with the game's folder, so nothing is left to show or to remove.
                    InstallRecordStore.DeleteFolder(load.Folder);
                    _logger.KeptFilesDropped(record.Id);
                    continue;
                }

                if (await SizeOfLeftoversAsync(files, record, cancellationToken).ConfigureAwait(false) is { } bytes)
                {
                    leftovers.Add(new LeftoverBackup(record.Id, record.Game, record.GameName, record.Backup.Directory, bytes)
                    {
                        GameDirectory = record.InstallDirectory,
                        Cover = record.Source?.CoverUrl,
                        Kept = files.KeptAt is { } keptAt
                            ? new KeptFilesSummary([.. files.Files.Take(KeptFilesSummary.MaxPaths).Select(file => file.Path)],
                                files.Files.Count, keptAt,
                                KeptReasons.MostPressing(files.Files.Select(file => file.Kept).OfType<KeptReason>()),
                                record.Source?.ProjectName)
                            : null,
                    });
                }
            }

            return leftovers;
        }, cancellationToken);

    public Task<bool> RemoveLeftoverBackupsAsync(Guid recordId, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            var folder = _leftovers.FolderOf(recordId);
            if ((await InstallRecordStore.LoadAsync(folder, cancellationToken).ConfigureAwait(false)).Record is not { } record
                || !BackupFolders.IsAvailable(record.Backup.Directory))
            {
                return false;
            }

            var removed = BackupFolders.DeleteListed(record.Backup.Directory,
                await InstallRecordStore.LoadFilesAsync(folder, cancellationToken).ConfigureAwait(false));
            InstallRecordStore.DeleteFolder(folder);
            _logger.LeftoverBackupsRemoved(recordId, removed);
            return true;
        }, cancellationToken);

    public async Task<ForgetOutcome> ForgetIfGoneAsync(Guid recordId, bool keepBackups, CancellationToken cancellationToken)
    {
        // Looked at first apart from the queue, so a drive that does not answer holds up neither the caller nor the
        // operations queued after this one.
        if ((await InstallRecordStore.LoadAsync(_store.FolderOf(recordId), cancellationToken).ConfigureAwait(false)).Record is { } record
            && !await IsGoneForGoodAsync(record, cancellationToken).ConfigureAwait(false))
        {
            return ForgetOutcome.Kept;
        }

        return await EnqueueAsync(() => ForgetCoreAsync(recordId, keepBackups, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ForgetOutcome> ForgetCoreAsync(Guid recordId, bool keepBackups, CancellationToken cancellationToken)
    {
        var folder = _store.FolderOf(recordId);
        if (folder.HasJournal)
        {
            throw new InstallBlockedException([new RecoveryPendingIssue(recordId)]);
        }

        var (record, files) = await LoadAsync(recordId, cancellationToken).ConfigureAwait(false);
        if (!GameFolder.IsGoneForGood(record.InstallDirectory, record.GameVolume, _volumes))
        {
            return ForgetOutcome.Kept;
        }

        _hook.Before(ChangeKind.Record);
        await _ownership.ReleaseAsync(record.InstallDirectory, recordId).ConfigureAwait(false);
        if (keepBackups && record.Backup.Location != BackupLocation.GameFolder && BackupFolders.KeepsOriginals(files))
        {
            Directory.CreateDirectory(_options.LeftoversDirectory);
            Directory.Move(folder.Root, _leftovers.FolderOf(recordId).Root);
            _logger.ForgottenKeepingBackups(recordId);
            return ForgetOutcome.ForgottenKeepingBackups;
        }

        var removed = BackupFolders.DeleteListed(record.Backup.Directory, files);
        InstallRecordStore.DeleteFolder(folder);
        _logger.Forgotten(recordId, removed);
        return ForgetOutcome.Forgotten;
    }

    private async Task<bool> IsGoneForGoodAsync(InstallRecord record, CancellationToken cancellationToken)
    {
        try
        {
            return await _folderChecks.ReadAsync(record.InstallDirectory,
                _ => Task.FromResult(GameFolder.IsGoneForGood(record.InstallDirectory, record.GameVolume, _volumes)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // Null when the list cannot be read.
    private async Task<InstalledFiles?> LoadLeftoverFilesAsync(InstallFolder folder, InstallRecord record, CancellationToken cancellationToken)
    {
        try
        {
            return await InstallRecordStore.LoadFilesAsync(folder, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            _logger.UnreadableFileList(record.Id, exception);
            return null;
        }
    }

    // Null while their folder is not there or does not answer, or their list names no copies, as a kept-files record holding
    // only notes of files without an original does.
    private async Task<long?> SizeOfLeftoversAsync(InstalledFiles files, InstallRecord record, CancellationToken cancellationToken)
    {
        if (!BackupFolders.KeepsOriginals(files))
        {
            return null;
        }

        var directory = record.Backup.Directory;
        try
        {
            return await _folderChecks.ReadAsync(directory, _ => Task.FromResult(BackupFolders.IsAvailable(directory)
                ? files.Files.Select(file => file.Backup is { } name ? new FileInfo(Path.Join(directory, name)) : null)
                    .Sum(file => file is { Exists: true } ? file.Length : 0)
                : (long?)null), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private async Task<PreflightReport> PredictCoreAsync(Guid recordId, InstallRequest replacement,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var package = await OpenAsync(replacement, cancellationToken).ConfigureAwait(false);
        await using (package.ConfigureAwait(false))
        {
            var (record, files) = await LoadAsync(recordId, cancellationToken).ConfigureAwait(false);
            var game = OpenGame(record.InstallDirectory);
            var records = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            var id = Guid.NewGuid();
            var (report, _, _, _) = await CheckLeavingOutAsync(package, replacement.Parts, async (included, hashes) =>
            {
                // The paths the new packages declare go over as they are, so they find them as they are now.
                var handedOver = DeclaredBy(included, game);
                var plan = (await Uninstaller.PlanAsync(record, files, game, handedOver, progress: null, cancellationToken)
                        .ConfigureAwait(false))
                    .With(await RuntimeUndo.PlanAsync(record, files, game, handedOver, replacement.Game, progress: null, cancellationToken)
                        .ConfigureAwait(false));
                var afterUninstall = plan.Steps.ToDictionary(step => step.Path, Uninstaller.AfterUndo, PathComparison.Comparer);
                var left = plan.Left.Count > 0
                    ? new KeptFilesRecord(_leftovers.FolderOf(recordId), record, KeptFilesRecordOf(plan.Left, files, game))
                    : null;
                return await CheckAsync(included, game, replacement, id, records, replacing: recordId,
                    path => afterUninstall.TryGetValue(path, out var file) ? file : SimulatedFile.OnDisk(path), left, hashes, progress,
                    cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
            return BackupsUnavailable(record, files) is { } unavailable
                ? report with { Issues = report.Issues.Add(unavailable) }
                : report;
        }
    }

    private async Task<UninstallResult> UninstallCoreAsync(Guid recordId, IReadOnlyList<string>? replacedBy,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
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
        var handedOver = await DeclaredByAsync(replacedBy, game, cancellationToken).ConfigureAwait(false);
        var plan = await Uninstaller.PlanAsync(record, files, game, handedOver, progress, cancellationToken).ConfigureAwait(false);
        // The files the package's software changes are read only once nothing runs from the game's folder.
        var probe = await _games.ProbeAsync(record.Game, cancellationToken).ConfigureAwait(false);
        ThrowIfAny([.. ReadinessIssues(probe, forUninstall: true)]);
        plan = plan.With(await RuntimeUndo.PlanAsync(record, files, game, handedOver, probe.Game, progress, cancellationToken)
            .ConfigureAwait(false));
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

        ThrowIfAny(issues);

        var header = new TransactionStarted(TransactionStarted.CurrentFormatVersion, TransactionKind.Uninstall, recordId, game.Root,
            game.StagingDirectory(recordId), record.Backup.Directory, record.GameVolume);
        var leftovers = _leftovers.FolderOf(recordId);
        IReadOnlyList<KeptFile> kept;
        InstalledFiles? left = null;
        using (var journal = TransactionJournal.Create(folder.JournalFile, header))
        {
            try
            {
                kept = await Uninstaller.RunAsync(plan, record, files, game, journal, movesBack, _hook, progress, _logger,
                    cancellationToken).ConfigureAwait(false);
                if (plan.Left.Count > 0)
                {
                    // Written before the commit, so a committed uninstall always has it and its copies are never lost.
                    left = KeptFilesRecordOf(plan.Left, files, game);
                    _hook.Before(ChangeKind.Record);
                    await _leftovers.SaveAsync(record, left, cancellationToken).ConfigureAwait(false);
                }

                // Removing the record commits the uninstall; what follows only tidies up.
                _hook.Before(ChangeKind.Record);
                File.Delete(folder.RecordFile);
            }
            catch (Exception exception)
            {
                progress?.Report(new InstallProgress(InstallStage.RollingBack, null, 0, 0, 0, 0));
                var rolledBack = await JournalRollback.RollBackAsync(journal, game, _hook, _logger).ConfigureAwait(false);
                if (rolledBack)
                {
                    Tidy(recordId, () => InstallRecordStore.DeleteFolder(leftovers));
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

        _hook.Before(ChangeKind.Tidy);
        await LeaveAsync(game.Root, recordId, () => Uninstaller.Finish(header, folder, files, left)).ConfigureAwait(false);
        _logger.Uninstalled(recordId, kept.Count);
        return new UninstallResult([.. kept]);
    }

    // The folders the install created stay listed while paths it leaves are inside them, for the install that takes those
    // paths over to remove once they are empty.
    private InstalledFiles KeptFilesRecordOf(IReadOnlyList<InstalledFile> left, InstalledFiles files, GameFolder game)
    {
        var paths = left.Select(file => game.Locate(file.Path)).ToList();
        return new InstalledFiles
        {
            Files = left,
            CreatedDirectories =
            [
                .. files.CreatedDirectories.Where(directory => game.Locate(directory) is var path && Directory.Exists(path)
                    && paths.Exists(file => PathComparison.IsWithin(path, file))),
            ],
            KeptAt = _time.GetUtcNow(),
        };
    }

    private async Task<IReadOnlySet<string>> DeclaredByAsync(IReadOnlyList<string>? packagePaths, GameFolder game,
        CancellationToken cancellationToken)
    {
        var declared = new HashSet<string>(PathComparison.Comparer);
        foreach (var path in packagePaths ?? [])
        {
            var package = await _packages.OpenAsync(path, cancellationToken).ConfigureAwait(false);
            await using (package.ConfigureAwait(false))
            {
                declared.UnionWith(DeclaredBy(package, game));
            }
        }

        return declared;
    }

    private static HashSet<string> DeclaredBy(IPackage package, GameFolder game) =>
        package.RuntimeFiles.Select(file => game.TryResolve(file.Path)).OfType<string>().ToHashSet(PathComparison.Comparer);
}
