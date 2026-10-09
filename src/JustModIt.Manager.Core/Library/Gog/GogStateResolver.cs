using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>GOG keeps no install-state flags like Steam's, so the state is inferred from the folder.</summary>
internal static class GogStateResolver
{
    /// <summary>heroic-gogdl keeps these in the game folder until a download or update completes.</summary>
    public static readonly string[] DownloadMarkers = [".gogdl-resume", ".gogdl-download-cache"];

    /// <summary>Infers the state of a recorded game from its folder.</summary>
    /// <param name="fileSystem">Reads the folder.</param>
    /// <param name="directory">The game's canonical folder.</param>
    /// <param name="record">What the launcher recorded, with what it reports doing with the game.</param>
    /// <param name="info">The game's <c>.info</c> file; null when the folder has none.</param>
    public static (GameInstallState State, GameStateReason Reason) Resolve(IFileSystem fileSystem, string directory, GogInstallRecord record,
        GogGameInfo? info)
    {
        if (!fileSystem.DirectoryExists(directory))
        {
            return InstallFolder.MissingState(fileSystem, directory);
        }

        if (record.Activity is { } activity)
        {
            return (activity, GameStateReason.StoreBusy);
        }

        if (DownloadMarkers.Any(marker => fileSystem.GetEntry(Path.Join(directory, marker)) is not null))
        {
            return (info is null ? GameInstallState.Installing : GameInstallState.Updating, GameStateReason.DownloadInProgress);
        }

        if (info is null)
        {
            // Very old installers write no .info file, so the game still counts as installed.
            return (GameInstallState.Installed, GameStateReason.InfoFileMissing);
        }

        // The launcher rewrites its record and the .info file at different moments of an update.
        return record.BuildId is not null && info.BuildId is not null && record.BuildId != info.BuildId
            ? (GameInstallState.Updating, GameStateReason.BuildMismatch)
            : (GameInstallState.Installed, GameStateReason.None);
    }
}
