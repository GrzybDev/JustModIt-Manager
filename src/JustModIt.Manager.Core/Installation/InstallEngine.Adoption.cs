using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.Installation;

public sealed partial class InstallEngine
{
    public Task<InstallRecord?> AdoptMovedAsync(Guid recordId, InstalledGame game, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        return EnqueueAsync(() => AdoptCoreAsync(recordId, game, progress, cancellationToken), cancellationToken);
    }

    private async Task<InstallRecord?> AdoptCoreAsync(Guid recordId, InstalledGame game, IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_store.FolderOf(recordId).HasJournal)
        {
            throw new InstallBlockedException([new RecoveryPendingIssue(recordId)]);
        }

        var (record, files) = await LoadAsync(recordId, cancellationToken).ConfigureAwait(false);
        var target = OpenGame(game.InstallDirectory);
        if (PathComparison.AreSame(target.Root, record.InstallDirectory))
        {
            return record;
        }

        ThrowIfAny(PendingOn(target.Root));
        // The player may have installed into the new folder meanwhile; two records must never own one folder.
        var records = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        if (records.Any(load => load.Folder.Id != recordId
            && (load.IsUnreadable || (load.Record is { } other && PathComparison.Overlaps(other.InstallDirectory, target.Root)))))
        {
            return null;
        }

        using var gameLock = Lock(target);
        var tracker = new ProgressTracker(progress, InstallStage.Verifying, files.Files.Count, FileInspector.BytesToHash(files));
        var inspected = await FileInspector.InspectAllAsync(files, target, tracker, cancellationToken).ConfigureAwait(false);
        if (!IsThere(inspected))
        {
            return null;
        }

        var backup = record.Backup;
        if (backup.Location == BackupLocation.GameFolder)
        {
            // The store moved the hidden folder together with the game.
            var moved = BackupFolders.For(BackupChoice.GameFolder, target.Root, record.Game, recordId, _options)!;
            if (Directory.Exists(moved.Directory) || !Directory.Exists(backup.Directory))
            {
                backup = moved;
            }
        }

        var adopted = record with { InstallDirectory = target.Root, GameVolume = _volumes.GetVolumeId(target.Root), Backup = backup };
        _hook.Before(ChangeKind.Record);
        await _store.SaveRecordAsync(adopted, cancellationToken).ConfigureAwait(false);
        _logger.Adopted(recordId);
        return adopted;
    }

    // A file the install deleted being absent proves little on its own, so a file it wrote has to be there unless it only
    // deleted files.
    private static bool IsThere(List<InspectedFile> inspected)
    {
        var written = inspected.Where(file => file.File.Change != FileChange.Deleted).ToList();
        return written.Count > 0
            ? written.Any(file => file.Integrity == FileIntegrity.Intact)
            : inspected.Count > 0 && inspected.All(file => file.Integrity == FileIntegrity.Intact);
    }
}
