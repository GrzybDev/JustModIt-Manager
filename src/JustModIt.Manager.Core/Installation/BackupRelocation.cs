using JustModIt.Manager.Core.IO;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// Moves an install's backups to another folder when the player changes where backups live: every backup reaches the new
/// folder and is checked before the record switches to it, which commits the move, and only then do the old copies go.
/// </summary>
internal static class BackupRelocation
{
    /// <summary>Moves the backups as a journaled operation.</summary>
    /// <param name="record">The install.</param>
    /// <param name="files">The install's files, which name their backups.</param>
    /// <param name="target">The new backup folder.</param>
    /// <param name="folder">The install's folder, which holds the journal.</param>
    /// <param name="store">Saves the record that commits the move.</param>
    /// <param name="moves">Whether both folders are on one volume, so backups are renamed rather than copied and checked.</param>
    /// <param name="stagingDirectory">The game's staging folder, for the journal; empty when the game folder is gone.</param>
    /// <param name="hook">Called right before each change.</param>
    /// <param name="progress">Reports each backup.</param>
    /// <param name="logger">Logs the finished move.</param>
    /// <param name="cancellationToken">Stops the move, which is then undone.</param>
    /// <returns>The record with its new backup folder.</returns>
    public static async Task<InstallRecord> RunAsync(InstallRecord record, InstalledFiles files, BackupFolder target,
        InstallFolder folder, InstallRecordStore store, bool moves, string stagingDirectory, ChangeHook hook,
        IProgress<InstallProgress>? progress, ILogger logger, CancellationToken cancellationToken)
    {
        var source = record.Backup.Directory;
        var backups = files.Files
            .Where(file => file.Backup is not null && File.Exists(Path.Join(source, file.Backup)))
            .ToList();
        var tracker = new ProgressTracker(progress, InstallStage.MovingBackups, backups.Count,
            moves ? 0 : backups.Sum(file => file.Original?.Size ?? 0));
        var header = new TransactionStarted(TransactionStarted.CurrentFormatVersion, TransactionKind.MoveBackups, record.Id,
            record.InstallDirectory, stagingDirectory, source, record.GameVolume, target);
        using var journal = TransactionJournal.Create(folder.JournalFile, header);
        Directory.CreateDirectory(target.Directory);
        InstallRecord moved;
        try
        {
            foreach (var batch in backups.Chunk(TransactionJournal.MaxBatch))
            {
                foreach (var file in batch)
                {
                    journal.Add(new BackupRelocated(file.Backup!, moves));
                }

                hook.Before(ChangeKind.Commit);
                journal.Commit();
                foreach (var file in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    tracker.StartFile(file.Path);
                    await RelocateAsync(file, source, target.Directory, moves, hook, tracker, cancellationToken).ConfigureAwait(false);
                    tracker.FinishFile();
                }
            }

            moved = record with { Backup = target };
            hook.Before(ChangeKind.Record);
            await store.SaveRecordAsync(moved, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            journal.CloseAfterFailure();
            if (Undo(journal.Entries, header))
            {
                File.Delete(folder.JournalFile);
            }

            throw;
        }

        journal.Dispose();
        hook.Before(ChangeKind.Tidy);
        Finish(journal.Entries, header);
        File.Delete(folder.JournalFile);
        logger.BackupsMoved(record.Id, target.Location.ToString());
        return moved;
    }

    /// <summary>Finishes a move the record already committed, or undoes one it did not.</summary>
    /// <param name="folder">The install's folder, which holds the journal.</param>
    /// <param name="header">The journal's first entry.</param>
    /// <param name="record">The install's record as it is on disk; null when there is none.</param>
    public static RecoveryOutcome Recover(InstallFolder folder, TransactionStarted header, InstallRecord? record)
    {
        var entries = TransactionJournal.Read(folder.JournalFile);
        if (record is not null && header.TargetBackup is { } target
            && PathComparison.AreSame(record.Backup.Directory, target.Directory))
        {
            Finish(entries, header);
        }
        else if (!Undo(entries, header))
        {
            return RecoveryOutcome.Failed;
        }

        File.Delete(folder.JournalFile);
        return RecoveryOutcome.CleanedUp;
    }

    private static async Task RelocateAsync(InstalledFile file, string source, string target, bool moves, ChangeHook hook,
        ProgressTracker tracker, CancellationToken cancellationToken)
    {
        var from = Path.Join(source, file.Backup);
        var to = Path.Join(target, file.Backup);
        hook.Before(ChangeKind.Relocate, file.Path);
        if (moves)
        {
            File.Move(from, to);
            return;
        }

        FileOperations.TryDeleteFile(to);
        var copy = await FileOperations.CopyNewFileAsync(from, to, tracker.Bytes, cancellationToken).ConfigureAwait(false);
        if (file.Original is { } original && !copy.HasSameContent(original))
        {
            throw new IOException($"The backup of '{file.Path}' does not match its original.");
        }
    }

    private static bool Undo(IReadOnlyList<JournalEntry> entries, TransactionStarted header)
    {
        var target = header.TargetBackup!.Directory;
        var complete = true;
        foreach (var relocated in entries.OfType<BackupRelocated>().Reverse())
        {
            var from = Path.Join(header.BackupDirectory, relocated.Name);
            var to = Path.Join(target, relocated.Name);
            try
            {
                if (!relocated.Moved)
                {
                    FileOperations.DeleteFile(to);
                }
                else if (File.Exists(to) && !File.Exists(from))
                {
                    File.Move(to, from);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                complete = false;
            }
        }

        // What is left in the new folder are copies cut short; a backup that could not go back stays where it is.
        if (complete)
        {
            FileOperations.DeleteOwnDirectory(target);
            BackupFolders.DeleteIfEmpty(target);
        }

        return complete;
    }

    private static void Finish(IReadOnlyList<JournalEntry> entries, TransactionStarted header)
    {
        foreach (var relocated in entries.OfType<BackupRelocated>().Where(relocated => !relocated.Moved))
        {
            // The old copy goes only once the new one is surely there.
            if (File.Exists(Path.Join(header.TargetBackup!.Directory, relocated.Name)))
            {
                FileOperations.DeleteFile(Path.Join(header.BackupDirectory, relocated.Name));
            }
        }

        BackupFolders.DeleteIfEmpty(header.BackupDirectory);
    }
}
