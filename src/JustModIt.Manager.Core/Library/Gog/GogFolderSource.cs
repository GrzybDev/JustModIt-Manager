using System.Security;
using JustModIt.Manager.Core.Collections;
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
        var libraries = new List<LibraryLocation>();
        var records = new List<GogInstallRecord>();
        var folders = new List<WatchedFolder>();
        foreach (var folder in context.ManualFolders.Select(fileSystem.GetCanonicalPath).Distinct(IO.PathComparison.Comparer))
        {
            if (!fileSystem.DirectoryExists(folder))
            {
                var isOffline = Path.GetPathRoot(folder) is not { Length: > 0 } root || !fileSystem.DirectoryExists(root);
                libraries.Add(new LibraryLocation(folder, LibrarySource, folder, isOffline ? LibraryState.Offline : LibraryState.Online, IsManual: true));
                if (WatchedFolder.Toward(fileSystem, folder) is { } toward)
                {
                    folders.Add(toward);
                }

                continue;
            }

            libraries.Add(new LibraryLocation(folder, LibrarySource, folder, LibraryState.Online, IsManual: true));
            folders.Add(new WatchedFolder(folder, []));
            await foreach (var (gameId, gameFolder) in FindGamesAsync(folder, cancellationToken).ConfigureAwait(false))
            {
                records.Add(new GogInstallRecord(gameId, gameFolder, folder) { Platform = platform.AsOperatingSystem() });
            }
        }

        return new GogSourceScan([.. libraries], records, new WatchPlan([.. folders], []));
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
}
