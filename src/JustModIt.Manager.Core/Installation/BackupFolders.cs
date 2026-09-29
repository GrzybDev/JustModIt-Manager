using System.Buffers;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// Where an install keeps the originals it replaces: a folder of its own per install, so two installs never mix their
/// originals and everything in an unfinished install's folder is known to be the app's.
/// </summary>
internal static class BackupFolders
{
    private const string GameFolderBackups = "backups";

    private static readonly SearchValues<char> InvalidNameCharacters = SearchValues.Create(Path.GetInvalidFileNameChars());

    /// <summary>The backup folder of an install.</summary>
    /// <param name="choice">Where the settings keep originals.</param>
    /// <param name="gameRoot">The canonical game folder.</param>
    /// <param name="key">The game, which names its folder outside the game folder.</param>
    /// <param name="recordId">The install's record id, which names its own folder.</param>
    /// <param name="options">The app's folder names.</param>
    /// <returns>The folder, or null when the player's folder is not there or lies inside the game.</returns>
    public static BackupFolder? For(BackupChoice choice, string gameRoot, GameKey key, Guid recordId, InstallEngineOptions options)
    {
        var record = recordId.ToString("N");
        switch (choice.Location)
        {
            case BackupLocation.AppData:
                return new BackupFolder(BackupLocation.AppData, Path.Join(options.BackupsDirectory, GameFolderName(key), record));
            case BackupLocation.CustomFolder when choice.CustomFolder is { } custom:
                return Directory.Exists(custom) && !PathComparison.Overlaps(custom, gameRoot)
                    ? new BackupFolder(BackupLocation.CustomFolder, Path.Join(custom, options.AppId, GameFolderName(key), record))
                    : null;
            case BackupLocation.CustomFolder:
                return null;
            default:
                return new BackupFolder(BackupLocation.GameFolder,
                    Path.Join(gameRoot, options.HiddenFolderName, GameFolderBackups, record));
        }
    }

    /// <summary>The top folder the app owns above a record's backup folder, which may go once it is empty.</summary>
    /// <param name="backupDirectory">The record's backup folder.</param>
    public static string OwnedRootOf(string backupDirectory) =>
        Path.GetDirectoryName(Path.GetDirectoryName(backupDirectory)!)!;

    /// <summary>Removes the record's backup folder and the app's folders above it while they are empty.</summary>
    /// <param name="backupDirectory">The record's backup folder.</param>
    public static void DeleteIfEmpty(string backupDirectory) =>
        FileOperations.DeleteEmptyDirectories(backupDirectory, OwnedRootOf(backupDirectory));

    /// <summary>Whether the folder is there; a folder on a disconnected drive must not be recreated empty.</summary>
    /// <param name="backupDirectory">The record's backup folder.</param>
    public static bool IsAvailable(string backupDirectory) => Directory.Exists(backupDirectory);

    private static string GameFolderName(GameKey key)
    {
        var name = $"{key.Store}-{key.StoreGameId}".ToCharArray();
        for (var i = 0; i < name.Length; i++)
        {
            if (InvalidNameCharacters.Contains(name[i]))
            {
                name[i] = '_';
            }
        }

        return new string(name);
    }
}
