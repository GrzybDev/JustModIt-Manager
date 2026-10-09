using System.Globalization;
using JustModIt.Manager.Core.Collections;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Installation;

public enum KeptReason
{
    /// <summary>Something, usually a game update, changed the file after the install; restoring would break the game.</summary>
    ChangedSinceInstall,

    /// <summary>The original's backup is gone, so the installed file stays rather than leaving nothing.</summary>
    BackupMissing,

    /// <summary>The original's backup no longer matches it, so the installed file stays rather than a damaged copy.</summary>
    BackupDamaged,
}

public sealed record KeptFile(string Path, KeptReason Reason);

public sealed record UninstallResult(ValueList<KeptFile> KeptFiles);

internal enum UndoAction
{
    Nothing,
    Restore,
    Delete,
}

internal sealed record UndoStep(InstalledFile File, string Path, UndoAction Action);

/// <summary>What an uninstall will do.</summary>
/// <param name="Steps">What the uninstall changes; every other file stays as it is.</param>
/// <param name="Kept">The files that stay as they are, and why.</param>
internal sealed record UninstallPlan(IReadOnlyList<UndoStep> Steps, IReadOnlyList<KeptFile> Kept);

/// <summary>
/// Puts originals back and removes added files, never touching a file changed since the install, as a journaled
/// transaction that moves installed files aside, so a failed, cancelled or crashed uninstall leaves the install as it was.
/// </summary>
internal static class Uninstaller
{
    /// <summary>Decides what to do with each of the install's files, without changing anything.</summary>
    /// <param name="record">The install.</param>
    /// <param name="files">The install's files.</param>
    /// <param name="game">The game folder.</param>
    /// <param name="progress">Reports the check of the files.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    public static async Task<UninstallPlan> PlanAsync(InstallRecord record, InstalledFiles files, GameFolder game,
        IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var tracker = new ProgressTracker(progress, InstallStage.Checking, files.Files.Count, FileInspector.BytesToHash(files));
        var steps = new List<UndoStep>();
        var kept = new List<KeptFile>();
        foreach (var (file, path, integrity) in await FileInspector.InspectAllAsync(files, game, tracker, cancellationToken)
                     .ConfigureAwait(false))
        {
            switch (Decide(file, integrity))
            {
                case null:
                    kept.Add(new KeptFile(file.Path, KeptReason.ChangedSinceInstall));
                    break;
                case UndoAction.Nothing:
                    break;
                case UndoAction.Restore when file.Backup is null || !File.Exists(Path.Join(record.Backup.Directory, file.Backup)):
                    kept.Add(new KeptFile(file.Path, KeptReason.BackupMissing));
                    break;
                case { } action:
                    steps.Add(new UndoStep(file, path!, action));
                    break;
            }
        }

        return new UninstallPlan(steps, kept);
    }

    /// <summary>What a file holds once the uninstall is done: its original, nothing, or whatever it holds now.</summary>
    /// <param name="step">What the uninstall does with the file.</param>
    public static SimulatedFile? AfterUndo(UndoStep step) => step switch
    {
        { Action: UndoAction.Delete } => null,
        { File.Original: { } original } => new SimulatedFile(original.Size, original.Sha256, DiskPath: null),
        _ => SimulatedFile.OnDisk(step.Path),
    };

    /// <summary>Carries out the plan, committing each batch of renames to the journal before it happens.</summary>
    /// <param name="plan">What to change.</param>
    /// <param name="record">The install.</param>
    /// <param name="files">The install's files, which name the folders it created.</param>
    /// <param name="game">The game folder.</param>
    /// <param name="journal">The uninstall's journal, with its header written.</param>
    /// <param name="movesBack">The backups are on the game's volume, so originals are moved back rather than copied.</param>
    /// <param name="hook">Called right before each change.</param>
    /// <param name="progress">Reports each file.</param>
    /// <param name="logger">Logs a damaged backup.</param>
    /// <param name="cancellationToken">Stops the uninstall before the record goes, which the caller then rolls back.</param>
    /// <returns>Files kept as they are, and why.</returns>
    public static async Task<IReadOnlyList<KeptFile>> RunAsync(UninstallPlan plan, InstallRecord record, InstalledFiles files,
        GameFolder game, TransactionJournal journal, bool movesBack, ChangeHook hook, IProgress<InstallProgress>? progress,
        ILogger logger, CancellationToken cancellationToken)
    {
        var restoring = plan.Steps.Where(step => step.Action == UndoAction.Restore).Sum(step => step.File.Original?.Size ?? 0);
        var tracker = new ProgressTracker(progress, InstallStage.Uninstalling, plan.Steps.Count, restoring);
        var staging = journal.Header.StagingDirectory;
        Directory.CreateDirectory(staging);
        var kept = plan.Kept.ToList();
        var actions = new List<Action>();
        var staged = 0;
        string NextStagingPath() => Path.Join(staging, (staged++).ToString(CultureInfo.InvariantCulture));

        foreach (var (file, path, action) in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tracker.StartFile(file.Path);
            if (actions.Count >= TransactionJournal.MaxBatch)
            {
                journal.CommitThen(actions, hook);
            }

            string? source = null;
            if (action == UndoAction.Restore)
            {
                source = await PrepareOriginalAsync(file, record, movesBack, NextStagingPath(), tracker, cancellationToken)
                    .ConfigureAwait(false);
                if (source is null)
                {
                    logger.BackupDamaged(file.Path, record.Id);
                    kept.Add(new KeptFile(file.Path, KeptReason.BackupDamaged));
                    tracker.FinishFile();
                    continue;
                }
            }

            if (File.Exists(path))
            {
                var aside = NextStagingPath();
                journal.Add(new FileDisplaced(file.Path, Path.GetFileName(aside)));
                actions.Add(() =>
                {
                    hook.Before(ChangeKind.Delete, file.Path);
                    File.Move(path, aside);
                });
            }

            if (source is not null)
            {
                journal.Add(new FileRestored(file.Path, file.Backup!, movesBack, file.Original!));
                actions.Add(() =>
                {
                    hook.Before(ChangeKind.Restore, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.Move(source, path);
                    FileOperations.RestoreAttributes(path, file.OriginalAttributes);
                });
            }

            tracker.FinishFile();
        }

        journal.CommitThen(actions, hook);
        RemoveCreatedDirectories(files, game, journal, hook);
        // The last chance to cancel before the record goes.
        cancellationToken.ThrowIfCancellationRequested();
        return kept;
    }

    /// <summary>
    /// Tidies up after an uninstall committed: the installed files it moved aside, the backups the record lists and the
    /// rest of the record go.
    /// </summary>
    /// <param name="header">The uninstall's journal header.</param>
    /// <param name="folder">The install's folder.</param>
    /// <param name="files">
    /// The install's files; null when an earlier attempt removed their list already, so the record's own backup folder goes
    /// as a whole.
    /// </param>
    public static void Finish(TransactionStarted header, InstallFolder folder, InstalledFiles? files)
    {
        TransactionCleanup.RemoveStaging(header.StagingDirectory);
        if (files is not null)
        {
            BackupFolders.DeleteListed(header.BackupDirectory, files);
        }
        else
        {
            FileOperations.DeleteOwnDirectory(header.BackupDirectory);
            BackupFolders.DeleteIfEmpty(header.BackupDirectory);
        }

        InstallRecordStore.DeleteFolder(folder);
    }

    // Null when the file has to be kept as it is.
    private static UndoAction? Decide(InstalledFile file, FileIntegrity integrity) => (file.Change, integrity) switch
    {
        (_, FileIntegrity.Modified) => null,
        (_, FileIntegrity.OriginalRestored) => UndoAction.Nothing,
        (FileChange.Added, FileIntegrity.Intact) => UndoAction.Delete,
        (FileChange.Added, _) => UndoAction.Nothing,
        // A deleted original is intact while it is absent, and a missing replaced file gets its original back.
        _ => UndoAction.Restore,
    };

    // The backup is checked against its original before it may go back, moved as it is or copied to staging. Returns the
    // file to rename into place, or null when the backup no longer holds the original.
    private static async Task<string?> PrepareOriginalAsync(InstalledFile file, InstallRecord record, bool movesBack,
        string staged, ProgressTracker tracker, CancellationToken cancellationToken)
    {
        var original = file.Original!;
        var backup = Path.Join(record.Backup.Directory, file.Backup!);
        if (movesBack)
        {
            var check = await FileOperations.HashAsync(backup, tracker.Bytes, cancellationToken).ConfigureAwait(false);
            return check.HasSameContent(original) ? backup : null;
        }

        return await FileOperations.CopyCheckedAsync(backup, staged, original, tracker.Bytes, cancellationToken).ConfigureAwait(false)
            ? staged
            : null;
    }

    private static void RemoveCreatedDirectories(InstalledFiles files, GameFolder game, TransactionJournal journal, ChangeHook hook)
    {
        foreach (var directory in files.CreatedDirectories.Reverse())
        {
            if (game.TryResolve(directory) is { } path && Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                journal.Add(new DirectoryRemoved(directory));
                journal.Commit();
                hook.Before(ChangeKind.Delete, directory);
                Directory.Delete(path);
            }
        }
    }
}
