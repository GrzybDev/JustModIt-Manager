using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Installation;

public enum RecoveryOutcome
{
    /// <summary>An interrupted install or uninstall was undone, so the game is as it was before it began.</summary>
    RolledBack,

    /// <summary>The operation had already committed, or had changed nothing; only leftovers were removed.</summary>
    CleanedUp,

    /// <summary>The game folder is gone for good, so there was nothing left to undo.</summary>
    Discarded,

    /// <summary>The game folder or the backups are on a drive that is not attached; the journal waits for it.</summary>
    Unavailable,

    /// <summary>Some change could not be undone; the journal is kept for the next attempt.</summary>
    Failed,
}

/// <summary>An interrupted operation that recovery looked at.</summary>
/// <param name="RecordId">The operation's record id.</param>
/// <param name="GameDirectory">Null when the journal is too damaged to tell.</param>
/// <param name="Outcome">What recovery did.</param>
public sealed record RecoveredOperation(Guid RecordId, string? GameDirectory, RecoveryOutcome Outcome);

/// <summary>Finishes or undoes what a crash or a power cut interrupted. Runs first when the agent starts.</summary>
internal sealed class InstallRecovery(InstallRecordStore store, InstallEngineOptions options, IVolumes volumes, ChangeHook hook,
    ILogger logger)
{
    /// <summary>Finishes or undoes every operation that left a journal.</summary>
    /// <param name="cancellationToken">Stops recovery between operations.</param>
    public async Task<IReadOnlyList<RecoveredOperation>> RunAsync(CancellationToken cancellationToken)
    {
        var recovered = new List<RecoveredOperation>();
        foreach (var folder in store.Folders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!folder.HasJournal)
            {
                // Left by an uninstall that finished its work, or an install that failed before its first change.
                if (!folder.HasRecord)
                {
                    InstallRecordStore.DeleteFolder(folder);
                }

                continue;
            }

            try
            {
                if (await RecoverAsync(folder, cancellationToken).ConfigureAwait(false) is { } operation)
                {
                    logger.Recovered(folder.Id, operation.Outcome);
                    recovered.Add(operation);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                logger.RecoveryFailed(folder.Id, exception);
                recovered.Add(new RecoveredOperation(folder.Id, GameDirectory: null, RecoveryOutcome.Failed));
            }
        }

        return recovered;
    }

    private async Task<RecoveredOperation?> RecoverAsync(InstallFolder folder, CancellationToken cancellationToken)
    {
        if (TransactionJournal.ReadHeader(folder.JournalFile) is not { } header)
        {
            // The header is written before any change, so a journal without one never described a change.
            File.Delete(folder.JournalFile);
            if (!folder.HasRecord)
            {
                InstallRecordStore.DeleteFolder(folder);
            }

            return null;
        }

        var load = await InstallRecordStore.LoadAsync(folder, cancellationToken).ConfigureAwait(false);
        return header.Kind switch
        {
            TransactionKind.Install => await RecoverInstallAsync(folder, header).ConfigureAwait(false),
            TransactionKind.Uninstall => await RecoverUninstallAsync(folder, header, cancellationToken).ConfigureAwait(false),
            // Without a readable record there is no telling whether the move committed.
            _ when load.IsUnreadable => Report(header, RecoveryOutcome.Failed),
            _ => Report(header, BackupRelocation.Recover(folder, header, load.Record)),
        };
    }

    private async Task<RecoveredOperation?> RecoverInstallAsync(InstallFolder folder, TransactionStarted header)
    {
        // The record is the commit point, whether or not this version can read it.
        if (folder.HasRecord)
        {
            File.Delete(folder.JournalFile);
            TransactionCleanup.RemoveStaging(header.StagingDirectory);
            BackupFolders.DeleteIfEmpty(header.BackupDirectory);
            return Report(header, RecoveryOutcome.CleanedUp);
        }

        if (GameFolder.TryOpen(header.GameDirectory, options.HiddenFolderName) is not { } game)
        {
            if (!IsGoneForGood(header))
            {
                // Once the drive is back, only this journal and its backups can undo what the install changed.
                logger.RecoveryUnavailable(folder.Id);
                return Report(header, RecoveryOutcome.Unavailable);
            }

            TransactionCleanup.RemoveUncommitted(header, folder);
            return Report(header, RecoveryOutcome.Discarded);
        }

        var entries = TransactionJournal.Read(folder.JournalFile);
        if (entries.OfType<FileBackedUp>().Any() && !BackupFolders.IsAvailable(header.BackupDirectory))
        {
            logger.RecoveryUnavailable(folder.Id);
            return Report(header, RecoveryOutcome.Unavailable);
        }

        using var gameLock = GameFolderLock.TryAcquire(game);
        if (gameLock is null)
        {
            return null;
        }

        if (!await JournalRollback.UndoAsync(entries, game, hook, logger).ConfigureAwait(false))
        {
            return Report(header, RecoveryOutcome.Failed);
        }

        TransactionCleanup.RemoveUncommitted(header, folder);
        return Report(header, RecoveryOutcome.RolledBack);
    }

    // A committed uninstall is tidied up; one that did not commit is undone, which leaves the install as it was.
    private async Task<RecoveredOperation?> RecoverUninstallAsync(InstallFolder folder, TransactionStarted header,
        CancellationToken cancellationToken)
    {
        if (!folder.HasRecord)
        {
            // What is left to tidy, such as the installed files moved aside, may be on the game's drive.
            if (!Directory.Exists(header.GameDirectory) && !IsGoneForGood(header))
            {
                logger.RecoveryUnavailable(folder.Id);
                return Report(header, RecoveryOutcome.Unavailable);
            }

            InstalledFiles? files = null;
            try
            {
                files = await InstallRecordStore.LoadFilesAsync(folder, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
            }

            Uninstaller.Finish(header, folder, files);
            return Report(header, RecoveryOutcome.CleanedUp);
        }

        if (GameFolder.TryOpen(header.GameDirectory, options.HiddenFolderName) is not { } game)
        {
            if (!IsGoneForGood(header))
            {
                logger.RecoveryUnavailable(folder.Id);
                return Report(header, RecoveryOutcome.Unavailable);
            }

            // The record stays, so its backups can still be listed and removed.
            File.Delete(folder.JournalFile);
            return Report(header, RecoveryOutcome.Discarded);
        }

        var entries = TransactionJournal.Read(folder.JournalFile);
        if (entries.OfType<FileRestored>().Any(restored => restored.Moved) && !BackupFolders.IsAvailable(header.BackupDirectory))
        {
            logger.RecoveryUnavailable(folder.Id);
            return Report(header, RecoveryOutcome.Unavailable);
        }

        using var gameLock = GameFolderLock.TryAcquire(game);
        if (gameLock is null)
        {
            return null;
        }

        if (!await JournalRollback.UndoAsync(entries, game, hook, logger).ConfigureAwait(false))
        {
            return Report(header, RecoveryOutcome.Failed);
        }

        TransactionCleanup.RemoveStaging(header.StagingDirectory);
        File.Delete(folder.JournalFile);
        return Report(header, RecoveryOutcome.RolledBack);
    }

    private bool IsGoneForGood(TransactionStarted header) => GameFolder.IsGoneForGood(header.GameDirectory, header.GameVolume, volumes);

    private static RecoveredOperation Report(TransactionStarted header, RecoveryOutcome outcome) =>
        new(header.RecordId, header.GameDirectory, outcome);
}
