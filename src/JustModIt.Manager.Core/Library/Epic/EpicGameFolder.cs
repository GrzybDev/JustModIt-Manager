using System.Security;
using System.Text.Json;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Epic;

/// <summary>
/// The <c>.egstore</c> folder that the Epic Games Launcher and Legendary keep in a game's folder: what they installed
/// there, and their downloads while they update it.
/// </summary>
public static class EpicGameFolder
{
    /// <summary>The launchers' own folder at a game's root, which only they may write.</summary>
    public const string StoreFolderName = ".egstore";

    private static readonly EnumerationOptions TopLevelOnly = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        MatchType = MatchType.Simple,
    };

    /// <summary>
    /// Tells what the folder says the launcher is doing with the game: downloading an update while its data is there;
    /// null otherwise.
    /// </summary>
    /// <param name="fileSystem">Reads the folder.</param>
    /// <param name="gameDirectory">The game's folder.</param>
    internal static (GameInstallState State, GameStateReason Reason)? Activity(IFileSystem fileSystem, string gameDirectory)
    {
        var store = Path.Join(gameDirectory, StoreFolderName);
        var pending = Path.Join(store, "Pending");
        var downloading = fileSystem.DirectoryExists(Path.Join(store, "bps"))
            || (fileSystem.DirectoryExists(pending) && List(fileSystem, pending, "*").Count > 0);
        return downloading ? (GameInstallState.Updating, GameStateReason.DownloadInProgress) : null;
    }

    /// <summary>Tells when a launcher last wrote its records in the folder; null when it keeps none there.</summary>
    /// <param name="fileSystem">Reads the folder.</param>
    /// <param name="gameDirectory">The game's folder.</param>
    internal static DateTimeOffset? LastWrittenAt(IFileSystem fileSystem, string gameDirectory)
    {
        var store = Path.Join(gameDirectory, StoreFolderName);
        return fileSystem.DirectoryExists(store) ? List(fileSystem, store, "*").Max(entry => (DateTimeOffset?)entry.LastWriteTimeUtc) : null;
    }

    /// <summary>
    /// Reads the <c>.mancpn</c> files of the folder, each naming an app installed there; damaged ones are left out.
    /// </summary>
    /// <param name="fileSystem">Reads the folder.</param>
    /// <param name="gameDirectory">The game's folder.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    internal static async Task<IReadOnlyList<EpicFolderManifest>> ReadManifestsAsync(IFileSystem fileSystem, string gameDirectory,
        CancellationToken cancellationToken)
    {
        var store = Path.Join(gameDirectory, StoreFolderName);
        var manifests = new List<EpicFolderManifest>();
        foreach (var file in fileSystem.DirectoryExists(store) ? List(fileSystem, store, "*.mancpn") : [])
        {
            if (await TryReadAsync(fileSystem, file.FullPath, cancellationToken).ConfigureAwait(false) is { } manifest
                && LauncherJson.NullIfBlank(manifest.CatalogNamespace) is not null && LauncherJson.NullIfBlank(manifest.AppName) is not null)
            {
                manifests.Add(manifest);
            }
        }

        return manifests;
    }

    private static async Task<EpicFolderManifest?> TryReadAsync(IFileSystem fileSystem, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await LauncherJson.ReadAsync(fileSystem, path, EpicJsonContext.Default.EpicFolderManifest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static List<FileEntry> List(IFileSystem fileSystem, string folder, string pattern)
    {
        try
        {
            return [.. fileSystem.Enumerate(folder, pattern, TopLevelOnly).OrderBy(entry => entry.Name, StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
        }
    }
}
