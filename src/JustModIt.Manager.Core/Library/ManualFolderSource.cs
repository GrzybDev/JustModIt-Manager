using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library;

/// <summary>
/// Folders the player added for a store because none of its launchers reported them, where only games described by the
/// store's own files count, the same evidence the launchers' records are checked against.
/// </summary>
/// <param name="fileSystem">Reads the folders.</param>
/// <param name="librarySource">The <see cref="LibraryLocation.Source"/> of a folder the player added.</param>
public abstract class ManualFolderSource<TRecord>(IFileSystem fileSystem, string librarySource) : IInstallSource<TRecord>
    where TRecord : IInstallRecord
{
    protected IFileSystem FileSystem { get; } = fileSystem;

    public async Task<SourceScan<TRecord>> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var folders = await Task.WhenAll(context.ManualFolders.Distinct(PathComparison.Comparer)
            .Select(folder => ReadFolderAsync(folder, context.Access, cancellationToken))).ConfigureAwait(false);
        return new([.. folders.Select(folder => folder.Library)], [.. folders.SelectMany(folder => folder.Records)],
            WatchPlan.ForFolders(folders.Select(folder => folder.Watch)));
    }

    /// <summary>Finds the records of the games the store's files describe in <paramref name="folder"/>, their library.</summary>
    /// <param name="folder">The canonical folder the player added; it exists.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    protected abstract Task<IReadOnlyList<TRecord>> FindRecordsAsync(string folder, CancellationToken cancellationToken);

    private async Task<FolderRead> ReadFolderAsync(string folder, LibraryAccess access, CancellationToken cancellationToken)
    {
        try
        {
            return await access.ReadAsync(folder, token => ReadFolderAsync(folder, token), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Folders the player added are stored canonical, so the library keeps its identity without its drive.
            var path = PathComparison.Normalize(folder);
            return new FolderRead(new LibraryLocation(path, librarySource, path, LibraryState.Offline, IsManual: true), [], Watch: null);
        }
    }

    private async Task<FolderRead> ReadFolderAsync(string folder, CancellationToken cancellationToken)
    {
        var canonical = FileSystem.GetCanonicalPath(folder);
        if (!FileSystem.DirectoryExists(canonical))
        {
            var state = FileSystem.IsDriveMissing(canonical) ? LibraryState.Offline : LibraryState.Online;
            return new FolderRead(new LibraryLocation(canonical, librarySource, canonical, state, IsManual: true),
                [], WatchedFolder.Toward(FileSystem, canonical));
        }

        return new FolderRead(new LibraryLocation(canonical, librarySource, canonical, LibraryState.Online, IsManual: true),
            await FindRecordsAsync(canonical, cancellationToken).ConfigureAwait(false), new WatchedFolder(canonical, []));
    }

    /// <summary>A folder the player added, as a scan read it.</summary>
    /// <param name="Library">The folder as a library.</param>
    /// <param name="Records">The records of the games in it.</param>
    /// <param name="Watch">What shows the folder's games changing, or the folder coming back.</param>
    private sealed record FolderRead(LibraryLocation Library, IReadOnlyList<TRecord> Records, WatchedFolder? Watch);
}
