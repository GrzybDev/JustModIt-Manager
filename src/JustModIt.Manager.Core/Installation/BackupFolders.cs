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
                    ? new BackupFolder(BackupLocation.CustomFolder, Path.Join(custom, options.InstanceName, GameFolderName(key), record))
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
    private static string OwnedRootOf(string backupDirectory) =>
        Path.GetDirectoryName(Path.GetDirectoryName(backupDirectory)!)!;

    /// <summary>Removes the record's backup folder and the app's folders above it while they are empty.</summary>
    /// <param name="backupDirectory">The record's backup folder.</param>
    public static void DeleteIfEmpty(string backupDirectory) =>
        FileOperations.DeleteEmptyDirectories(backupDirectory, OwnedRootOf(backupDirectory));

    /// <summary>
    /// Removes the backups a record lists and the folders the app made for them once they are empty, leaving anything
    /// else the player put there, since the app deletes only what its records list.
    /// </summary>
    /// <param name="backupDirectory">The record's backup folder.</param>
    /// <param name="files">The install's files, which name their backups.</param>
    /// <param name="spared">Names of backups that stay, such as the copies a kept-files record holds.</param>
    /// <returns>How many backups were removed.</returns>
    public static int DeleteListed(string backupDirectory, InstalledFiles files, IReadOnlySet<string>? spared = null)
    {
        var removed = 0;
        foreach (var file in files.Files)
        {
            if (file.Backup is { } name && spared?.Contains(name) != true && File.Exists(Path.Join(backupDirectory, name)))
            {
                FileOperations.DeleteFile(Path.Join(backupDirectory, name));
                removed++;
            }
        }

        DeleteIfEmpty(backupDirectory);
        return removed;
    }

    /// <summary>Whether the folder is there; a folder on a disconnected drive must not be recreated empty.</summary>
    /// <param name="backupDirectory">The record's backup folder.</param>
    public static bool IsAvailable(string backupDirectory) => Directory.Exists(backupDirectory);

    /// <summary>Whether an install keeps any originals in its backup folder.</summary>
    /// <param name="files">The install's files.</param>
    public static bool KeepsOriginals(InstalledFiles files) => files.Files.Any(file => file.Backup is not null);

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
