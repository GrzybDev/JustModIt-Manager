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

    /// <summary>
    /// The game is another version than the one the originals of files the package's software changes were taken from, so
    /// those files stay as they are and the copies of their originals are kept.
    /// </summary>
    GameUpdatedSinceInstall,

    /// <summary>
    /// A file the package's software changes is as it was before the install, but it may not have been the store's file
    /// even then, so the store's check of the files is advised.
    /// </summary>
    OriginalUnknown,
}

public sealed record KeptFile(string Path, KeptReason Reason);

/// <summary>How the reasons an uninstall left files for rank when the player is told only one.</summary>
public static class KeptReasons
{
    /// <summary>The reason to tell first: a game update, then a doubted original, then a lost copy, then a changed file.</summary>
    /// <param name="reasons">The reasons of the files left; empty for none.</param>
    /// <returns>The most pressing reason; null when there is none.</returns>
    public static KeptReason? MostPressing(IEnumerable<KeptReason> reasons)
    {
        ArgumentNullException.ThrowIfNull(reasons);
        return reasons.Select(reason => (KeptReason?)reason).MinBy(reason => Rank(reason!.Value));
    }

    /// <summary>Whether the store's check of the game's files brings back what the uninstall could not.</summary>
    /// <param name="reason">Why a file was left.</param>
    public static bool AdvisesStoreCheck(KeptReason reason) => reason != KeptReason.ChangedSinceInstall;

    /// <summary>
    /// The case a text selects to word a reason: <c>GameUpdated</c>, <c>OriginalUnknown</c>, <c>Backup</c> for a copy missing
    /// or damaged, <c>Changed</c>, or <c>HandedOver</c> for files a replacement that did not finish handed over.
    /// </summary>
    /// <param name="reason">Why files were left; null for files handed over.</param>
    public static string CaseOf(KeptReason? reason) => reason switch
    {
        KeptReason.GameUpdatedSinceInstall => "GameUpdated",
        KeptReason.OriginalUnknown => "OriginalUnknown",
        KeptReason.BackupMissing or KeptReason.BackupDamaged => "Backup",
        KeptReason.ChangedSinceInstall => "Changed",
        _ => "HandedOver",
    };

    private static int Rank(KeptReason reason) => reason switch
    {
        KeptReason.GameUpdatedSinceInstall => 0,
        KeptReason.OriginalUnknown => 1,
        KeptReason.BackupDamaged => 2,
        KeptReason.BackupMissing => 3,
        _ => 4,
    };
}

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
/// <param name="Kept">The files that stay as they are, or come back with advice to check them, and why.</param>
internal sealed record UninstallPlan(IReadOnlyList<UndoStep> Steps, IReadOnlyList<KeptFile> Kept)
{
    /// <summary>
    /// What the kept-files record holds: files the package's software changed that the uninstall leaves for the player, and
    /// paths handed over to the install that replaces this one.
    /// </summary>
    public IReadOnlyList<InstalledFile> Left { get; init; } = [];

    /// <summary>
    /// Files whose originals the uninstall would put back but whose copies are gone, game-relative; while there are any, the
    /// uninstall is refused, since it would leave the package's files with nothing to tell the store's from.
    /// </summary>
    public IReadOnlyList<string> MissingOriginals { get; init; } = [];

    /// <summary>This plan together with the plan for the files the package's software changes.</summary>
    /// <param name="runtime">The plan for those files.</param>
    public UninstallPlan With(RuntimeUndoPlan runtime) =>
        new([.. Steps, .. runtime.Steps], [.. Kept, .. runtime.Kept])
        {
            Left = [.. Left, .. runtime.Left],
            MissingOriginals = MissingOriginals,
        };
}

/// <summary>
/// Puts originals back and removes added files, never touching a file changed since the install, as a journaled
/// transaction that moves installed files aside, so a failed, cancelled or crashed uninstall leaves the install as it was.
/// </summary>
internal static class Uninstaller
{
    /// <summary>
    /// Decides what to do with each of the install's files that a check reads, without changing anything; those the
    /// package's software changes are left to <see cref="RuntimeUndo"/>.
    /// </summary>
    /// <param name="record">The install.</param>
    /// <param name="files">The install's files.</param>
    /// <param name="game">The game folder.</param>
    /// <param name="handedOver">Full paths handed over as they are to the install that replaces this one.</param>
    /// <param name="progress">Reports the check of the files.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    public static async Task<UninstallPlan> PlanAsync(InstallRecord record, InstalledFiles files, GameFolder game,
        IReadOnlySet<string> handedOver, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        var checkedFiles = files with
        {
            Files = [.. FileInspector.Checked(files).Where(file => !handedOver.Contains(game.Locate(file.Path)))],
        };
        var tracker = new ProgressTracker(progress, InstallStage.Checking, checkedFiles.Files.Count,
            FileInspector.BytesToHash(checkedFiles));
        var steps = new List<UndoStep>();
        var kept = new List<KeptFile>();
        var missing = new List<string>();
        foreach (var (file, path, integrity) in await FileInspector.InspectAllAsync(checkedFiles, game, tracker, cancellationToken)
                     .ConfigureAwait(false))
        {
            switch (Decide(file, integrity))
            {
                case null:
                    kept.Add(new KeptFile(file.Path, KeptReason.ChangedSinceInstall));
                    break;
                case UndoAction.Nothing:
                    break;
                case UndoAction.Restore when !HasCopy(file, record.Backup.Directory):
                    missing.Add(file.Path);
                    break;
                case { } action:
                    steps.Add(new UndoStep(file, path!, action));
                    break;
            }
        }

        return new UninstallPlan(steps, kept) { MissingOriginals = missing };
    }

    /// <summary>Whether an uninstall would put the file's original back from a copy that is not there.</summary>
    /// <param name="file">The file as the install's record lists it.</param>
    /// <param name="integrity">What the file is now.</param>
    /// <param name="backupDirectory">The install's backup folder.</param>
    public static bool MissesOriginal(InstalledFile file, FileIntegrity integrity, string backupDirectory) =>
        Decide(file, integrity) == UndoAction.Restore && !HasCopy(file, backupDirectory);

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
                    // The software's files come back all together or not at all, so the whole uninstall is undone.
                    if (file.Runtime)
                    {
                        throw new IOException($"The copy of the original of '{file.Path}' no longer holds it.");
                    }

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
    /// Tidies up after an uninstall committed: the installed files it moved aside, the backups the record lists but its
    /// kept-files record does not, and the rest of the record go.
    /// </summary>
    /// <param name="header">The uninstall's journal header.</param>
    /// <param name="folder">The install's folder.</param>
    /// <param name="files">
    /// The install's files; null when an earlier attempt removed their list already, so the record's own backup folder goes
    /// as a whole, except the copies the kept-files record names.
    /// </param>
    /// <param name="kept">The uninstall's kept-files record, whose copies stay; null when it wrote none.</param>
    public static void Finish(TransactionStarted header, InstallFolder folder, InstalledFiles? files, InstalledFiles? kept)
    {
        TransactionCleanup.RemoveStaging(header.StagingDirectory);
        var spared = (kept?.Files ?? []).Select(file => file.Backup).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (files is not null)
        {
            BackupFolders.DeleteListed(header.BackupDirectory, files, spared);
        }
        else if (spared.Count > 0)
        {
            foreach (var backup in Directory.Exists(header.BackupDirectory) ? Directory.EnumerateFiles(header.BackupDirectory) : [])
            {
                if (!spared.Contains(Path.GetFileName(backup)))
                {
                    FileOperations.DeleteFile(backup);
                }
            }
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

    private static bool HasCopy(InstalledFile file, string backupDirectory) =>
        file.Backup is { } name && File.Exists(Path.Join(backupDirectory, name));

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
