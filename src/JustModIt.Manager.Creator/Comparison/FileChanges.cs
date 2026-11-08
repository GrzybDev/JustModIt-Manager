using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator.Projects;

namespace JustModIt.Manager.Creator.Comparison;

/// <summary>How many of an author's files differ, by size and time alone, from what an earlier look at the folder listed.</summary>
/// <param name="Changed">Files listed then whose size or time differs now.</param>
/// <param name="Added">Files that were not listed then.</param>
/// <param name="Removed">Files listed then that are gone.</param>
public readonly record struct FileChanges(int Changed, int Added, int Removed)
{
    // The default skips hidden and system files, which in an author's folder are .git, Thumbs.db and desktop.ini.
    private static readonly EnumerationOptions AuthorFiles = new() { RecurseSubdirectories = true, IgnoreInaccessible = false };

    public int Count => Changed + Added + Removed;

    /// <summary>Lists an author's folder off the calling thread and sets its files against their stamps; no file is opened.</summary>
    /// <param name="folder">The author's folder.</param>
    /// <param name="files">The stamps listed before, by path inside the folder.</param>
    /// <param name="skipped">Folders within it that hold other parts' files.</param>
    /// <param name="access">Gives up on opening the folder when its drive does not answer.</param>
    /// <param name="fileSystem">Lists the folder.</param>
    /// <param name="cancellationToken">Stops the listing, or the wait for one that a drive holds up.</param>
    /// <returns>The changes; null when the folder is not there, cannot be listed or its drive does not answer.</returns>
    public static async Task<FileChanges?> CheckAsync(string folder, IReadOnlyDictionary<string, FileStamp> files, IReadOnlyList<string> skipped,
        LibraryAccess access, IFileSystem fileSystem, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(async () =>
            {
                // Only opening the folder has a time limit, since a large listing may well outlast it.
                if (!await access.ReadAsync(folder, _ => Task.FromResult(fileSystem.DirectoryExists(folder)), cancellationToken).ConfigureAwait(false))
                {
                    return (FileChanges?)null;
                }

                int changed = 0, added = 0, listed = 0;
                foreach (var entry in List(fileSystem, folder, skipped, cancellationToken))
                {
                    if (!files.TryGetValue(PackagePath.FromFullPath(folder, entry.FullPath), out var stamp))
                    {
                        added++;
                        continue;
                    }

                    listed++;
                    if (!stamp.Matches(entry))
                    {
                        changed++;
                    }
                }

                return new FileChanges(changed, added, files.Count - listed);
            }, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// What changed in an author's folder since a build read it, as a comparison found the folder. A file whose stamp alone
    /// changed is read only when the build holds its hash and its size is the same, and it counts as changed unless its bytes are.
    /// </summary>
    /// <param name="comparison">The folder as compared now.</param>
    /// <param name="built">The folder as the build read it.</param>
    /// <param name="fileSystem">Reads the files; null for the real file system.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    public static Task<BuildChanges> SinceAsync(FolderComparison comparison, FolderBuild built, IFileSystem? fileSystem,
        CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var hashes = new ContentHashes(fileSystem ?? PhysicalFileSystem.Instance);
        var before = new Dictionary<string, BuiltFile>(built.Files);
        List<ComparedFile> changed = [];
        Dictionary<string, BuiltFile> restamped = [];
        foreach (var file in comparison.Files.Where(file => file.Author is not null))
        {
            var stamp = file.Author!.Value;
            if (!before.Remove(file.AuthorPath!, out var read) || read.Size != stamp.Size)
            {
                changed.Add(file);
            }
            else if (read.LastWriteTime != stamp.LastWriteTime)
            {
                // Only the files the build carried have a hash; any other under a new stamp counts as changed.
                if (read.Sha256 is { } hash
                    && await hashes.TryHashAsync(comparison.AuthorFullPath(file.AuthorPath!), stamp.Size, null, cancellationToken).ConfigureAwait(false) is { } now
                    && FileHash.Matches(now, hash))
                {
                    restamped[file.AuthorPath!] = read with { LastWriteTime = stamp.LastWriteTime };
                }
                else
                {
                    changed.Add(file);
                }
            }
        }

        return new BuildChanges(changed, before.Count, restamped);
    }, cancellationToken);

    /// <summary>The files of an author's folder, without links and the folders of other parts.</summary>
    /// <param name="fileSystem">Lists the folder.</param>
    /// <param name="folder">The author's folder.</param>
    /// <param name="skipped">Folders within it that hold other parts' files.</param>
    /// <param name="cancellationToken">Stops the listing between files.</param>
    internal static IEnumerable<FileEntry> List(IFileSystem fileSystem, string folder, IReadOnlyList<string> skipped, CancellationToken cancellationToken)
    {
        var left = skipped.Select(fileSystem.GetCanonicalPath).ToList();
        foreach (var entry in fileSystem.Enumerate(folder, "*", AuthorFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.IsDirectory && !entry.IsLink && !left.Exists(skip => PathComparison.IsWithin(skip, entry.FullPath)))
            {
                yield return entry;
            }
        }
    }
}

/// <summary>What changed in an author's folder since a build read it.</summary>
/// <param name="Changed">The compared files added or changed since.</param>
/// <param name="Removed">How many files the build read are gone.</param>
/// <param name="Restamped">The files whose bytes are still those the build read under a new stamp, with that stamp, for the record.</param>
public sealed record BuildChanges(IReadOnlyList<ComparedFile> Changed, int Removed, IReadOnlyDictionary<string, BuiltFile> Restamped)
{
    public int Count => Changed.Count + Removed;
}
