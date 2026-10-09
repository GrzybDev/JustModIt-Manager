using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>
/// Folders the player added: a game folder installed by GOG's offline installer, or a folder of such games, each
/// described by its own <c>goggame-&lt;id&gt;.info</c>.
/// </summary>
/// <param name="fileSystem">Reads the folders.</param>
/// <param name="platform">The system the app runs on, taken as the system the games are built for.</param>
internal sealed class GogFolderSource(IFileSystem fileSystem, AppPlatform platform)
    : ManualFolderSource<GogInstallRecord>(fileSystem, LibrarySource)
{
    /// <summary>The <see cref="LibraryLocation.Source"/> of a folder the player added.</summary>
    public const string LibrarySource = "gog-folder";

    private readonly GogInfoReader _infos = new(fileSystem);

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

    protected override async Task<IReadOnlyList<GogInstallRecord>> FindRecordsAsync(string folder, CancellationToken cancellationToken)
    {
        var records = new List<GogInstallRecord>();
        await foreach (var (gameId, gameFolder) in FindGamesAsync(folder, cancellationToken).ConfigureAwait(false))
        {
            records.Add(new GogInstallRecord(gameId, gameFolder, folder) { Platform = platform.AsOperatingSystem() });
        }

        return records;
    }

    // Links are not followed: they may lead to games that are not in the folder the player picked.
    private List<string> ListSubfolders(string folder) =>
        [.. FileSystem.TryList(folder, "*", FileListing.Everything).Where(entry => entry.IsDirectory && !entry.IsLink)
            .Select(entry => entry.FullPath).Order(StringComparer.Ordinal)];
}
