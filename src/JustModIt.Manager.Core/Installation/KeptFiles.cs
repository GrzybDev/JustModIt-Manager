using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// A kept-files record in the leftovers: the paths an uninstall left for later, with the copies of their originals in the
/// uninstalled install's backup folder.
/// </summary>
/// <param name="Folder">The record's folder among the leftovers.</param>
/// <param name="Record">The uninstalled install's record.</param>
/// <param name="Files">The paths left, as format 2 with <see cref="InstalledFiles.KeptAt"/>.</param>
internal sealed record KeptFilesRecord(InstallFolder Folder, InstallRecord Record, InstalledFiles Files)
{
    /// <summary>
    /// The install whose uninstall wrote the record is being put back, so it takes every entry over as a note handed to it:
    /// what it held back is what it would have kept had it never gone.
    /// </summary>
    public bool IsPutBack { get; init; }

    /// <summary>The copy of an entry's original; null when the entry has none.</summary>
    /// <param name="entry">An entry of the record.</param>
    public string? CopyOf(InstalledFile entry) => entry.Backup is { } name ? Path.Join(Record.Backup.Directory, name) : null;
}

/// <summary>An entry of a kept-files record that an install takes over.</summary>
/// <param name="Entry">The entry.</param>
/// <param name="From">The record that holds it.</param>
internal sealed record InheritedFile(InstalledFile Entry, KeptFilesRecord From)
{
    /// <summary>The copy of the original; null when the entry has none.</summary>
    public string? CopyPath => From.CopyOf(Entry);

    /// <summary>The version of the game the entry's original was taken from.</summary>
    public GameVersion Taken => Entry.Taken ?? From.Record.GameVersion ?? GameVersion.Unrecorded;
}

/// <summary>What an install does with the kept-files records of its game.</summary>
/// <param name="Inherited">The entries it takes over, by full path.</param>
/// <param name="Issues">Paths still held for the software, and folders of copies it needs that are not there.</param>
/// <param name="CopiedBytes">The bytes of the copies it takes over.</param>
internal sealed record KeptFilesCheck(IReadOnlyDictionary<string, InheritedFile> Inherited, IReadOnlyList<InstallIssue> Issues,
    long CopiedBytes);

/// <summary>
/// The kept-files records of a game: written by an uninstall for files it left as the package's software changed them, and
/// for paths a replacement hands over to the next install, then taken over, refused over or dropped by later installs.
/// </summary>
internal static class KeptFiles
{
    /// <summary>The committed kept-files records of a game; forgotten installs' backups and unreadable records are left out.</summary>
    /// <param name="leftovers">The leftovers' folders.</param>
    /// <param name="installs">The installs' folders, whose record still there means its uninstall has not committed.</param>
    /// <param name="gameRoot">The game's folder.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    public static async Task<IReadOnlyList<KeptFilesRecord>> LoadForAsync(InstallRecordStore leftovers, InstallRecordStore installs,
        string gameRoot, CancellationToken cancellationToken)
    {
        var records = new List<KeptFilesRecord>();
        foreach (var load in await leftovers.LoadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (load.Record is not { } record || !PathComparison.AreSame(record.InstallDirectory, gameRoot)
                || installs.FolderOf(record.Id).HasRecord)
            {
                continue;
            }

            try
            {
                var files = await InstallRecordStore.LoadFilesAsync(load.Folder, cancellationToken).ConfigureAwait(false);
                if (files.KeptAt is not null)
                {
                    records.Add(new KeptFilesRecord(load.Folder, record, files));
                }
            }
            catch (InvalidDataException)
            {
            }
        }

        return records;
    }

    /// <summary>
    /// Decides, for each kept path the package declares or writes, whether the install takes the entry over, is refused, or
    /// drops it because the path no longer holds what the record says.
    /// </summary>
    /// <param name="records">The game's kept-files records.</param>
    /// <param name="game">The game folder.</param>
    /// <param name="simulation">The check's replay of the package, with its declarations placed.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    public static async Task<KeptFilesCheck> CheckAsync(IReadOnlyList<KeptFilesRecord> records, GameFolder game,
        InstallSimulation simulation, CancellationToken cancellationToken)
    {
        var declared = simulation.Declared.ToDictionary(file => file.FullPath, PathComparison.Comparer);
        var inherited = new Dictionary<string, InheritedFile>(PathComparison.Comparer);
        var issues = new List<InstallIssue>();
        long copiedBytes = 0;
        foreach (var record in records)
        {
            var needsCopies = false;
            foreach (var entry in record.Files.Files)
            {
                if (game.TryResolve(entry.Path) is not { } path || inherited.ContainsKey(path))
                {
                    continue;
                }

                var isDeclared = declared.TryGetValue(path, out var declaration);
                if ((!isDeclared && !simulation.IsTouched(path))
                    // Anything else there means the store's check, or someone, wrote the path since; the entry is dropped.
                    || !await HoldsAsync(path, entry.Installed, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                switch (record.IsPutBack ? null : entry.Kept)
                {
                    case null when isDeclared || entry.Original is null || entry.Backup is not null:
                    case KeptReason.OriginalUnknown when isDeclared:
                        break;
                    case null:
                    case KeptReason.OriginalUnknown:
                        // A step's own backup of what is there tells as much.
                        continue;
                    default:
                        // The store's file is back when it is one the package names, whatever the record says.
                        if (!(isDeclared && entry.Installed is { } content && declaration!.Declaration.IsOriginal(content.Size, content.Sha256)))
                        {
                            issues.Add(new KeptFileIssue(game.RelativePathOf(path), record.Record.Id));
                        }

                        continue;
                }

                inherited[path] = new InheritedFile(entry, record);
                if (entry.Backup is not null && entry.Original is { } original)
                {
                    needsCopies = true;
                    copiedBytes += original.Size;
                }
            }

            if (needsCopies && !BackupFolders.IsAvailable(record.Record.Backup.Directory))
            {
                issues.Add(new BackupFolderUnavailableIssue(record.Record.Backup.Directory));
            }
        }

        return new KeptFilesCheck(inherited, issues, copiedBytes);
    }

    /// <summary>
    /// Drops from the game's kept-files records every entry at a path a committed install lists, which it took over or made
    /// stale, and every note without a copy, which no later install could use; each goes with its copy, and a record left
    /// without entries goes as a whole.
    /// </summary>
    /// <param name="installed">The committed install's files.</param>
    /// <param name="records">The game's kept-files records.</param>
    /// <param name="game">The game folder.</param>
    /// <param name="leftovers">Rewrites the records that keep entries.</param>
    /// <param name="hook">Called right before each change.</param>
    /// <param name="cancellationToken">Stops the writes.</param>
    public static async Task TidyAsync(InstalledFiles installed, IReadOnlyList<KeptFilesRecord> records, GameFolder game,
        InstallRecordStore leftovers, ChangeHook hook, CancellationToken cancellationToken)
    {
        var listed = installed.Files.Select(file => game.Locate(file.Path)).ToHashSet(PathComparison.Comparer);
        foreach (var record in records)
        {
            var dropped = record.Files.Files
                .Where(entry => entry is { Kept: null, Backup: null } || listed.Contains(game.Locate(entry.Path)))
                .ToList();
            // A copy on a drive that is not attached could not go, and would be left behind unlisted.
            if (dropped.Count == 0 || (dropped.Exists(entry => entry.Backup is not null)
                    && !BackupFolders.IsAvailable(record.Record.Backup.Directory)))
            {
                continue;
            }

            hook.Before(ChangeKind.Tidy);
            // The copies go first, so a crash leaves the record naming copies that are gone, which the next tidy drops.
            foreach (var copy in dropped.Select(record.CopyOf).OfType<string>())
            {
                FileOperations.DeleteFile(copy);
            }

            var remaining = record.Files.Files.Except(dropped).ToList();
            if (remaining.Count == 0)
            {
                InstallRecordStore.DeleteFolder(record.Folder);
                BackupFolders.DeleteIfEmpty(record.Record.Backup.Directory);
            }
            else
            {
                await leftovers.SaveAsync(record.Record, record.Files with { Files = remaining }, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Whether a path holds exactly some content, read without locking out a program that writes it.</summary>
    /// <param name="path">The full path.</param>
    /// <param name="content">The content; null for no file.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public static async Task<bool> HoldsAsync(string path, FileSnapshot? content, CancellationToken cancellationToken)
    {
        if (content is null)
        {
            return !File.Exists(path) && !Directory.Exists(path);
        }

        var file = new FileInfo(path);
        if (!file.Exists || file.Length != content.Size)
        {
            return false;
        }

        try
        {
            return (await FileOperations.HashSharedAsync(path, progress: null, cancellationToken).ConfigureAwait(false))
                .HasSameContent(content);
        }
        catch (FileChangedWhileReadException)
        {
            return false;
        }
    }
}
