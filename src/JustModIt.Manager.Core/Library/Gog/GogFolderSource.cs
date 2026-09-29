using System.Security;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>
/// Folders the player added: a game folder installed by GOG's offline installer, or a folder of such games. Only games
/// described by their own <c>goggame-&lt;id&gt;.info</c> count, the same evidence the other sources are checked against.
/// </summary>
public sealed class GogFolderSource(IFileSystem fileSystem, AppPlatform platform) : IGogInstallSource
{
    /// <summary>The <see cref="LibraryLocation.Source"/> of a folder the player added.</summary>
    public const string LibrarySource = "gog-folder";

    private static readonly EnumerationOptions Subfolders = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

    private readonly GogInfoReader _infos = new(fileSystem);

    public async Task<GogSourceScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var folders = await Task.WhenAll(context.ManualFolders.Distinct(PathComparison.Comparer)
            .Select(folder => ReadFolderAsync(folder, context.Access, cancellationToken))).ConfigureAwait(false);
        return new GogSourceScan([.. folders.Select(folder => folder.Library)], [.. folders.SelectMany(folder => folder.Records)],
            new WatchPlan([.. folders.Select(folder => folder.Watch).OfType<WatchedFolder>()], []));
    }

    /// <summary>Finds the game in the folder itself or, failing that, the games in its direct subfolders.</summary>
    /// <param name="folder">The folder the player picked.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    internal async IAsyncEnumerable<(string GameId, string Folder)> FindGamesAsync(string folder,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if ((await _infos.ReadAsync(folder, cancellationToken).ConfigureAwait(false)).Base is { } game)
        {
            yield return (game.GameId, folder);
            yield break;
        }

        foreach (var subfolder in ListSubfolders(folder))
        {
            if ((await _infos.ReadAsync(subfolder, cancellationToken).ConfigureAwait(false)).Base is { } found)
            {
                yield return (found.GameId, subfolder);
            }
        }
    }

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
            return new FolderRead(new LibraryLocation(path, LibrarySource, path, LibraryState.Offline, IsManual: true), [], Watch: null);
        }
    }

    private async Task<FolderRead> ReadFolderAsync(string folder, CancellationToken cancellationToken)
    {
        var canonical = fileSystem.GetCanonicalPath(folder);
        if (!fileSystem.DirectoryExists(canonical))
        {
            var isOffline = Path.GetPathRoot(canonical) is not { Length: > 0 } root || !fileSystem.DirectoryExists(root);
            return new FolderRead(
                new LibraryLocation(canonical, LibrarySource, canonical, isOffline ? LibraryState.Offline : LibraryState.Online, IsManual: true),
                [], WatchedFolder.Toward(fileSystem, canonical));
        }

        var records = new List<GogInstallRecord>();
        await foreach (var (gameId, gameFolder) in FindGamesAsync(canonical, cancellationToken).ConfigureAwait(false))
        {
            records.Add(new GogInstallRecord(gameId, gameFolder, canonical) { Platform = platform.AsOperatingSystem() });
        }

        return new FolderRead(new LibraryLocation(canonical, LibrarySource, canonical, LibraryState.Online, IsManual: true), records,
            new WatchedFolder(canonical, []));
    }

    private List<string> ListSubfolders(string folder)
    {
        try
        {
            // Links are not followed: they may lead to games that are not in the folder the player picked.
            return [.. fileSystem.Enumerate(folder, "*", Subfolders).Where(entry => entry.IsDirectory && !entry.IsLink)
                .Select(entry => entry.FullPath).Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
        }
    }

    /// <param name="Library">The folder as a library.</param>
    /// <param name="Records">The records of the games in it.</param>
    /// <param name="Watch">What shows the folder's games changing, or the folder coming back.</param>
    private sealed record FolderRead(LibraryLocation Library, IReadOnlyList<GogInstallRecord> Records, WatchedFolder? Watch);
}
