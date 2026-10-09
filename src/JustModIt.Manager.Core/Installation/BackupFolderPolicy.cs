using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Installation;

public enum BackupFolderProblem
{
    NotFullyQualified,
    Missing,

    /// <summary>A network share may be gone exactly when a game needs its originals back.</summary>
    NetworkFolder,

    NotWritable,

    /// <summary>A store may delete or verify the game folder and take the backups with it.</summary>
    InsideGameFolder,

    /// <summary>App updates replace the install folder and uninstalling the app removes it.</summary>
    InsideAppFolder,
}

/// <summary>Which folders the player may pick for backups.</summary>
public static class BackupFolderPolicy
{
    /// <summary>Checks a folder the player picked for backups.</summary>
    /// <param name="folder">The folder as the player gave it.</param>
    /// <param name="gameFolders">The folders of every detected or installed game.</param>
    /// <param name="appFolder">The folder the app itself is installed in.</param>
    /// <param name="volumes">Tells network shares from local disks.</param>
    /// <returns>Null when the folder is fine.</returns>
    public static BackupFolderProblem? Check(string folder, IEnumerable<string> gameFolders, string appFolder, IVolumes volumes)
    {
        ArgumentNullException.ThrowIfNull(gameFolders);
        ArgumentNullException.ThrowIfNull(volumes);
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
        {
            return BackupFolderProblem.NotFullyQualified;
        }

        if (volumes.IsNetworkPath(folder))
        {
            return BackupFolderProblem.NetworkFolder;
        }

        if (!Directory.Exists(folder))
        {
            return BackupFolderProblem.Missing;
        }

        var canonical = PathCanonicalizer.Canonicalize(folder);
        if (gameFolders.Any(game => PathComparison.Overlaps(PathCanonicalizer.Canonicalize(game), canonical)))
        {
            return BackupFolderProblem.InsideGameFolder;
        }

        if (PathComparison.Overlaps(PathCanonicalizer.Canonicalize(appFolder), canonical))
        {
            return BackupFolderProblem.InsideAppFolder;
        }

        return FileOperations.TryCreateProbe(canonical) == true ? null : BackupFolderProblem.NotWritable;
    }
}
