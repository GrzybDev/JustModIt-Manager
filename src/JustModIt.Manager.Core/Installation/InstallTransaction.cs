using System.Globalization;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// Applies a checked plan, journaling a path's original state and backing up an existing file the first time the path is
/// touched, before anything changes it; rollback, recovery and uninstall all follow from that first touch.
/// </summary>
internal sealed class InstallTransaction : IDisposable
{
    private readonly GameFolder _game;
    private readonly InstallPlan _plan;
    private readonly TransactionJournal _journal;
    private readonly ChangeHook _hook;
    private readonly OrderedDictionary<string, TouchedFile> _touched = new(PathComparison.Comparer);
    private readonly List<string> _createdDirectories = [];
    private readonly HashSet<string> _plannedDirectories = new(PathComparison.Comparer);
    private readonly List<Action> _actions = [];
    private readonly HashSet<string> _batchPaths = new(PathComparison.Comparer);
    private int _batchSteps;
    private int _staged;

    private InstallTransaction(GameFolder game, InstallPlan plan, TransactionJournal journal, ChangeHook hook)
    {
        _game = game;
        _plan = plan;
        _journal = journal;
        _hook = hook;
    }

    public TransactionJournal Journal => _journal;

    private string StagingDirectory => _journal.Header.StagingDirectory;

    private string BackupDirectory => _journal.Header.BackupDirectory;

    /// <summary>Starts an install by creating its journal, then the app's folders the journal names.</summary>
    /// <param name="game">The game folder.</param>
    /// <param name="folder">The install's folder, which holds the journal.</param>
    /// <param name="backup">Where the originals go.</param>
    /// <param name="gameVolume">The game folder's volume, for recovery; null when unknown.</param>
    /// <param name="plan">The plan the check made.</param>
    /// <param name="hook">Called right before each change.</param>
    public static InstallTransaction Begin(GameFolder game, InstallFolder folder, BackupFolder backup, string? gameVolume,
        InstallPlan plan, ChangeHook hook)
    {
        Directory.CreateDirectory(folder.Root);
        var header = new TransactionStarted(TransactionStarted.CurrentFormatVersion, TransactionKind.Install, folder.Id, game.Root,
            game.StagingDirectory(folder.Id), backup.Directory, gameVolume);
        var journal = TransactionJournal.Create(folder.JournalFile, header);
        try
        {
            // The app's own folders appear only once the journal names them, so recovery knows to remove them.
            Directory.CreateDirectory(header.StagingDirectory);
            Directory.CreateDirectory(header.BackupDirectory);
            return new InstallTransaction(game, plan, journal, hook);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing in the game changed yet, and recovery would remove the rest anyway.
            journal.Dispose();
            try
            {
                TransactionCleanup.RemoveUncommitted(header, folder);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }

            throw new InstallFailedException(rolledBack: true, exception);
        }
    }

    /// <summary>Carries out the package's steps.</summary>
    /// <param name="package">The opened package.</param>
    /// <param name="progress">Counts steps and bytes.</param>
    /// <param name="cancellationToken">Stops the install before the record commits it; the caller then rolls it back.</param>
    public async Task ExecuteAsync(IPackage package, ProgressTracker progress, CancellationToken cancellationToken)
    {
        foreach (var step in package.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paths = PathsOf(step);
            // Each step prepares what needs no change to the game (staged content, copies of originals on another volume),
            // the batch's journal entries are made durable at once, and only then are the renames made. A step that
            // touches a path already in the batch starts a new one, so it sees the changes before it.
            if (_batchSteps >= TransactionJournal.MaxBatch || paths.Exists(_batchPaths.Contains))
            {
                ApplyBatch();
            }

            switch (step)
            {
                case CopyStep copy:
                    progress.StartFile(copy.Path);
                    await CopyAsync(package, copy, progress, cancellationToken).ConfigureAwait(false);
                    break;
                case PatchStep patch:
                    progress.StartFile(patch.Path);
                    await PatchAsync(package, patch, progress, cancellationToken).ConfigureAwait(false);
                    break;
                case RenameStep rename:
                    progress.StartFile(rename.To);
                    await RenameAsync(rename, progress, cancellationToken).ConfigureAwait(false);
                    break;
                case DeleteStep delete:
                    progress.StartFile(delete.Path);
                    await DeleteAsync(delete, progress, cancellationToken).ConfigureAwait(false);
                    break;
            }

            _batchPaths.UnionWith(paths);
            _batchSteps++;
            progress.FinishFile();
        }

        ApplyBatch();
        // The last chance to cancel before the record commits the install.
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>What the install changed, for its record.</summary>
    public InstalledFiles Result()
    {
        var files = new List<InstalledFile>();
        foreach (var (path, touched) in _touched)
        {
            FileChange? change = (touched.Original, touched.Current) switch
            {
                (null, null) => null,
                (null, _) => FileChange.Added,
                (_, null) => FileChange.Deleted,
                _ => FileChange.Replaced,
            };
            if (change is { } kind)
            {
                var installed = touched.Current is { } current
                    ? current with { LastWriteTimeUtc = File.GetLastWriteTimeUtc(path) }
                    : null;
                files.Add(new InstalledFile(touched.RelativePath, kind, touched.Original, installed, touched.Backup,
                    touched.Attributes));
            }
        }

        return new InstalledFiles
        {
            Files = files,
            CreatedDirectories = [.. _createdDirectories.Where(Directory.Exists).Select(_game.RelativePathOf)],
        };
    }

    /// <summary>Saves the record, which commits the install.</summary>
    /// <param name="record">The install's record.</param>
    /// <param name="files">What the install changed, from <see cref="Result"/>.</param>
    /// <param name="store">Writes the record.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    public async Task CommitAsync(InstallRecord record, InstalledFiles files, InstallRecordStore store,
        CancellationToken cancellationToken)
    {
        _hook.Before(ChangeKind.Record);
        await store.SaveAsync(record, files, cancellationToken).ConfigureAwait(false);
        _journal.Dispose();
    }

    /// <summary>Removes what a committed install no longer needs.</summary>
    /// <param name="folder">The install's folder.</param>
    /// <param name="logger">Logs what could not be removed.</param>
    public void Tidy(InstallFolder folder, ILogger logger)
    {
        try
        {
            _hook.Before(ChangeKind.Tidy);
            File.Delete(folder.JournalFile);
            TransactionCleanup.RemoveStaging(StagingDirectory);
            BackupFolders.DeleteIfEmpty(BackupDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Recovery treats a journal next to its record as committed and only tidies up.
            logger.CleanupFailed(folder.Id, exception);
        }
    }

    /// <summary>Undoes everything the install changed.</summary>
    /// <param name="logger">Logs the files left alone and the changes that could not be undone.</param>
    /// <returns>False when some change could not be undone and the journal stays for recovery.</returns>
    public Task<bool> RollBackAsync(ILogger logger) => JournalRollback.RollBackAsync(_journal, _game, _hook, logger);

    public void Dispose() => _journal.Dispose();

    private List<string> PathsOf(PackageStep step) => step switch
    {
        CopyStep copy => [_plan.Paths[copy.Path]],
        PatchStep patch => [_plan.Paths[patch.Path]],
        RenameStep rename => [_plan.Paths[rename.From], _plan.Paths[rename.To]],
        DeleteStep delete => [_plan.Paths[delete.Path]],
        _ => [],
    };

    private void ApplyBatch()
    {
        _journal.CommitThen(_actions, _hook);
        _batchPaths.Clear();
        _batchSteps = 0;
    }

    private async Task CopyAsync(IPackage package, CopyStep step, ProgressTracker progress, CancellationToken cancellationToken)
    {
        var target = _plan.Paths[step.Path];
        var staged = NextStagingPath();
        FileSnapshot content;
        var payload = await package.OpenEntryAsync(step.Entry, cancellationToken).ConfigureAwait(false);
        await using (payload.ConfigureAwait(false))
        {
            content = await FileOperations.WriteNewFileAsync(payload, staged, progress.Bytes, cancellationToken).ConfigureAwait(false);
        }

        if (step.Sha256 is { } expected && !FileHash.Matches(content.Sha256, expected))
        {
            throw new InvalidPackageException(PackageError.PayloadMismatch, step.Entry);
        }

        PlanDirectories(target);
        var (touched, _) = await TouchAsync(target, progress, cancellationToken).ConfigureAwait(false);
        Replace(touched, staged, content);
    }

    private async Task PatchAsync(IPackage package, PatchStep step, ProgressTracker progress, CancellationToken cancellationToken)
    {
        var target = _plan.Paths[step.Path];
        var staged = NextStagingPath();
        var patch = await package.OpenEntryAsync(step.Entry, cancellationToken).ConfigureAwait(false);
        await using (patch.ConfigureAwait(false))
        {
            // The file is still the one the check hashed: in this batch nothing touched it yet.
            await XDelta3Patcher.DecodeAsync(target, patch, staged, new PatchTarget(step.TargetSize, step.TargetSha256), progress.Bytes,
                cancellationToken).ConfigureAwait(false);
        }

        var (touched, _) = await TouchAsync(target, progress, cancellationToken).ConfigureAwait(false);
        Replace(touched, staged, new FileSnapshot(step.TargetSize, step.TargetSha256));
    }

    private async Task RenameAsync(RenameStep step, ProgressTracker progress, CancellationToken cancellationToken)
    {
        var source = _plan.Paths[step.From];
        var target = _plan.Paths[step.To];
        if (PathComparison.AreSame(source, target))
        {
            return;
        }

        var (moved, isFirstTouch) = await TouchAsync(source, progress, cancellationToken).ConfigureAwait(false);
        var content = moved.Current ?? throw new IOException($"'{step.From}' is gone.");
        var from = source;
        if (isFirstTouch && _plan.MovesOriginals)
        {
            // The original itself goes to the backup folder, so its content reaches the new name as a copy.
            from = NextStagingPath();
            await FileOperations.CopyNewFileAsync(source, from, progress.Bytes, cancellationToken).ConfigureAwait(false);
        }

        PlanDirectories(target);
        var (replaced, _) = await TouchAsync(target, progress, cancellationToken).ConfigureAwait(false);
        Replace(replaced, from, content);
        moved.Current = null;
    }

    private async Task DeleteAsync(DeleteStep step, ProgressTracker progress, CancellationToken cancellationToken)
    {
        var target = _plan.Paths[step.Path];
        var exists = _touched.TryGetValue(target, out var known) ? known.Current is not null : File.Exists(target);
        if (!exists)
        {
            return;
        }

        var (touched, isFirstTouch) = await TouchAsync(target, progress, cancellationToken).ConfigureAwait(false);
        // Moving an original into the backup folder already removes it.
        if (!isFirstTouch || !_plan.MovesOriginals)
        {
            var relative = touched.RelativePath;
            _actions.Add(() =>
            {
                _hook.Before(ChangeKind.Delete, relative);
                FileOperations.DeleteFile(target);
            });
        }

        touched.Current = null;
    }

    private void Replace(TouchedFile touched, string source, FileSnapshot content)
    {
        _journal.Add(new FileWritten(touched.RelativePath, content));
        _actions.Add(() =>
        {
            _hook.Before(ChangeKind.Write, touched.RelativePath);
            FileOperations.MoveOver(source, touched.FullPath);
        });
        touched.Current = content;
    }

    private async Task<(TouchedFile File, bool IsFirstTouch)> TouchAsync(string path, ProgressTracker progress,
        CancellationToken cancellationToken)
    {
        if (_touched.TryGetValue(path, out var touched))
        {
            return (touched, false);
        }

        var relative = _game.RelativePathOf(path);
        if (File.Exists(path))
        {
            var attributes = FileOperations.PreservedAttributesOf(path);
            var name = _touched.Count.ToString(CultureInfo.InvariantCulture);
            var backup = Path.Join(BackupDirectory, name);
            var lastWrite = File.GetLastWriteTimeUtc(path);
            FileSnapshot original;
            if (_plan.MovesOriginals)
            {
                original = (_plan.Hashes.TryGetValue(path, out var known) ? known.ForUnchanged(path) : null)
                    ?? await FileOperations.HashAsync(path, progress.Bytes, cancellationToken).ConfigureAwait(false);
                _actions.Add(() =>
                {
                    _hook.Before(ChangeKind.Backup, relative);
                    File.Move(path, backup);
                });
            }
            else
            {
                original = await CopyOriginalAsync(path, backup, relative, progress, cancellationToken).ConfigureAwait(false);
            }

            original = original with { LastWriteTimeUtc = lastWrite };
            _journal.Add(new FileBackedUp(relative, name, original, _plan.MovesOriginals, attributes));
            touched = new TouchedFile(path, relative)
            {
                Original = original,
                Backup = name,
                Attributes = attributes,
                Current = original,
            };
        }
        else
        {
            _journal.Add(new FileWasAbsent(relative));
            touched = new TouchedFile(path, relative);
        }

        _touched.Add(path, touched);
        return (touched, true);
    }

    // A copy on another volume is read back before the original may be replaced.
    private async Task<FileSnapshot> CopyOriginalAsync(string path, string backup, string relative, ProgressTracker progress,
        CancellationToken cancellationToken)
    {
        var original = await FileOperations.CopyNewFileAsync(path, backup, progress.Bytes, cancellationToken).ConfigureAwait(false);
        _hook.Before(ChangeKind.ReadBack, relative);
        var copy = await FileOperations.HashAsync(backup, progress.Bytes, cancellationToken).ConfigureAwait(false);
        return copy.HasSameContent(original)
            ? original
            : throw new IOException($"The backup copy of '{path}' does not match the original.");
    }

    private void PlanDirectories(string path)
    {
        var missing = new Stack<string>();
        for (var directory = Path.GetDirectoryName(path);
             directory is not null && !Directory.Exists(directory) && !_plannedDirectories.Contains(directory);
             directory = Path.GetDirectoryName(directory))
        {
            missing.Push(directory);
        }

        while (missing.TryPop(out var directory))
        {
            var relative = _game.RelativePathOf(directory);
            _plannedDirectories.Add(directory);
            _journal.Add(new DirectoryCreated(relative));
            _actions.Add(() =>
            {
                _hook.Before(ChangeKind.CreateDirectory, relative);
                Directory.CreateDirectory(directory);
                _createdDirectories.Add(directory);
            });
        }
    }

    private string NextStagingPath() => Path.Join(StagingDirectory, (_staged++).ToString(CultureInfo.InvariantCulture));

    private sealed class TouchedFile(string fullPath, string relativePath)
    {
        public string FullPath => fullPath;

        public string RelativePath => relativePath;

        public FileSnapshot? Original { get; init; }

        public string? Backup { get; init; }

        public FileAttributes? Attributes { get; init; }

        public FileSnapshot? Current { get; set; }
    }
}

/// <summary>Removal of what an operation leaves in the app's own folders.</summary>
internal static class TransactionCleanup
{
    /// <summary>
    /// Removes a staging folder, everything in which is the operation's own, and the folders above it up to the app's
    /// hidden folder at the game root once they are empty.
    /// </summary>
    /// <param name="stagingDirectory">The operation's staging folder.</param>
    public static void RemoveStaging(string stagingDirectory)
    {
        FileOperations.DeleteOwnDirectory(stagingDirectory);
        var staging = Path.GetDirectoryName(stagingDirectory)!;
        FileOperations.DeleteEmptyDirectories(staging, Path.GetDirectoryName(staging)!);
    }

    /// <summary>
    /// Removes what an install that was undone, or never got to change anything, left behind, including all of its
    /// backup folder, which was created for it alone.
    /// </summary>
    /// <param name="header">The install's journal header.</param>
    /// <param name="folder">The install's folder.</param>
    public static void RemoveUncommitted(TransactionStarted header, InstallFolder folder)
    {
        RemoveStaging(header.StagingDirectory);
        FileOperations.DeleteOwnDirectory(header.BackupDirectory);
        BackupFolders.DeleteIfEmpty(header.BackupDirectory);
        InstallRecordStore.DeleteFolder(folder);
    }
}
