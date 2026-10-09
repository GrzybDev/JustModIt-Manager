using System.Globalization;
using JustModIt.Manager.Core.Packages;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// Undoes a journal in reverse, each undo idempotent so a rollback cut short by another crash simply runs again, leaving
/// alone a file someone else wrote after the operation touched it, since undoing over it would break the game.
/// </summary>
internal static class JournalRollback
{
    private enum PathState
    {
        Absent,
        Own,
        Original,
        Foreign,
    }

    /// <summary>Closes the journal of an operation that failed and undoes it.</summary>
    /// <param name="journal">The operation's journal, still open.</param>
    /// <param name="game">The game folder the entries' paths are relative to.</param>
    /// <param name="hook">Called right before each change.</param>
    /// <param name="logger">Logs the files left alone and the entries that could not be undone.</param>
    /// <returns>False when some change could not be undone; the journal must then stay for another attempt.</returns>
    public static Task<bool> RollBackAsync(TransactionJournal journal, GameFolder game, ChangeHook hook, ILogger logger)
    {
        journal.CloseAfterFailure();
        return UndoAsync(journal.Entries, game, hook, logger);
    }

    /// <summary>Undoes every entry after the header, the last first.</summary>
    /// <param name="entries">The journal's lines, the header first.</param>
    /// <param name="game">The game folder the entries' paths are relative to.</param>
    /// <param name="hook">Called right before each change.</param>
    /// <param name="logger">Logs the files left alone and the entries that could not be undone.</param>
    /// <returns>False when some change could not be undone; the journal must then stay for another attempt.</returns>
    public static async Task<bool> UndoAsync(IReadOnlyList<JournalEntry> entries, GameFolder game, ChangeHook hook,
        ILogger logger)
    {
        var header = (TransactionStarted)entries[0];
        var written = entries.OfType<FileWritten>().ToLookup(entry => entry.Path, entry => entry.Content, PackagePath.Comparer);
        var restores = 0;
        var complete = true;
        for (var i = entries.Count - 1; i > 0; i--)
        {
            try
            {
                switch (entries[i])
                {
                    case FileBackedUp backup:
                        await RestoreAsync(backup, header, game, written[backup.Path], () => restores++, hook, logger)
                            .ConfigureAwait(false);
                        break;
                    case FileWasAbsent absent:
                        await RemoveAsync(absent, header, game, written[absent.Path], hook, logger).ConfigureAwait(false);
                        break;
                    case DirectoryCreated directory:
                        FileOperations.DeleteDirectoryIfEmpty(Resolve(game, directory.Path));
                        break;
                    case FileRestored restored:
                        await TakeBackAsync(restored, header, game, hook).ConfigureAwait(false);
                        break;
                    case FileDisplaced displaced:
                        PutBack(displaced, header, game, hook, logger);
                        break;
                    case DirectoryRemoved directory:
                        Directory.CreateDirectory(Resolve(game, directory.Path));
                        break;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                complete = false;
                logger.UndoFailed(entries[i].GetType().Name, header.RecordId, exception);
            }
        }

        return complete;
    }

    private static async Task RestoreAsync(FileBackedUp entry, TransactionStarted header, GameFolder game,
        IEnumerable<FileSnapshot> own, Func<int> nextRestore, ChangeHook hook, ILogger logger)
    {
        var backup = Path.Join(header.BackupDirectory, entry.Name);
        // Never moved there, or already put back by an earlier attempt.
        if (!File.Exists(backup))
        {
            return;
        }

        var path = Resolve(game, entry.Path);
        switch (await StateOfAsync(path, own, entry.Original).ConfigureAwait(false))
        {
            case PathState.Foreign:
                logger.ChangedByOthers(entry.Path, header.RecordId);
                return;
            case PathState.Original:
                FileOperations.RestoreAttributes(path, entry.Attributes);
                return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (entry.Moved)
        {
            hook.Before(ChangeKind.Restore, entry.Path);
            FileOperations.MoveOver(backup, path);
        }
        else if (!await CopyBackAsync(backup, path, entry.Original, header, nextRestore(), hook).ConfigureAwait(false))
        {
            logger.BackupDamaged(entry.Path, header.RecordId);
            return;
        }

        FileOperations.RestoreAttributes(path, entry.Attributes);
    }

    private static async Task RemoveAsync(FileWasAbsent entry, TransactionStarted header, GameFolder game,
        IEnumerable<FileSnapshot> own, ChangeHook hook, ILogger logger)
    {
        var path = Resolve(game, entry.Path);
        switch (await StateOfAsync(path, own, original: null).ConfigureAwait(false))
        {
            case PathState.Own:
                hook.Before(ChangeKind.Delete, entry.Path);
                FileOperations.DeleteFile(path);
                break;
            case PathState.Foreign:
                logger.ChangedByOthers(entry.Path, header.RecordId);
                break;
        }
    }

    // Returns an original an uninstall put back to where it came from.
    private static async Task TakeBackAsync(FileRestored entry, TransactionStarted header, GameFolder game, ChangeHook hook)
    {
        var path = Resolve(game, entry.Path);
        var backup = Path.Join(header.BackupDirectory, entry.Backup);
        switch (await StateOfAsync(path, [], entry.Original).ConfigureAwait(false))
        {
            // A backup still in its folder never moved, so the original at the path came from elsewhere, e.g. the store.
            case PathState.Original when entry.Moved && !File.Exists(backup):
                hook.Before(ChangeKind.Backup, entry.Path);
                File.Move(path, backup);
                break;
            case PathState.Original when entry.Moved:
                break;
            case PathState.Original:
                hook.Before(ChangeKind.Delete, entry.Path);
                FileOperations.DeleteFile(path);
                break;
        }
    }

    // Puts back an installed file an uninstall moved aside.
    private static void PutBack(FileDisplaced entry, TransactionStarted header, GameFolder game, ChangeHook hook, ILogger logger)
    {
        var staged = Path.Join(header.StagingDirectory, entry.Name);
        if (!File.Exists(staged))
        {
            return;
        }

        var path = Resolve(game, entry.Path);
        if (File.Exists(path))
        {
            logger.ChangedByOthers(entry.Path, header.RecordId);
            return;
        }

        hook.Before(ChangeKind.Restore, entry.Path);
        File.Move(staged, path);
    }

    // A backup from another volume goes through the staging folder and is checked before it replaces anything; false when
    // it no longer holds the original.
    private static async Task<bool> CopyBackAsync(string backup, string path, FileSnapshot original, TransactionStarted header,
        int number, ChangeHook hook)
    {
        Directory.CreateDirectory(header.StagingDirectory);
        var staged = Path.Join(header.StagingDirectory, "restore-" + number.ToString(CultureInfo.InvariantCulture));
        FileOperations.TryDeleteFile(staged);
        var copied = await FileOperations.CopyNewFileAsync(backup, staged, progress: null, CancellationToken.None)
            .ConfigureAwait(false);
        if (!copied.HasSameContent(original))
        {
            FileOperations.DeleteFile(staged);
            return false;
        }

        if (original.LastWriteTimeUtc is { } time)
        {
            File.SetLastWriteTimeUtc(staged, time);
        }

        hook.Before(ChangeKind.Restore, PackagePath.FromFullPath(header.GameDirectory, path));
        FileOperations.MoveOver(staged, path);
        return true;
    }

    private static async Task<PathState> StateOfAsync(string path, IEnumerable<FileSnapshot> own, FileSnapshot? original)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return PathState.Absent;
        }

        var ownSizes = own.Where(snapshot => snapshot.Size == file.Length).ToList();
        if (ownSizes.Count == 0 && original?.Size != file.Length)
        {
            return PathState.Foreign;
        }

        var actual = await FileOperations.HashAsync(path, progress: null, CancellationToken.None).ConfigureAwait(false);
        return ownSizes.Exists(actual.HasSameContent) ? PathState.Own
            : original is not null && actual.HasSameContent(original) ? PathState.Original
            : PathState.Foreign;
    }

    private static string Resolve(GameFolder game, string relativePath) =>
        game.TryResolve(relativePath) ?? throw new IOException($"'{relativePath}' no longer resolves inside the game folder.");
}
