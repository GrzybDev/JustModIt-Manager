using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Gog;

internal readonly record struct GogState(GameInstallState State, GameStateReason Reason)
{
    /// <summary>Lower is more trustworthy: installed and idle, then installed with doubts, then busy, then broken.</summary>
    public int Rank => (State, Reason) switch
    {
        (GameInstallState.Installed, GameStateReason.None) => 0,
        (GameInstallState.Installed, _) => 1,
        (GameInstallState.Broken, _) => 3,
        _ => 2,
    };
}

/// <summary>GOG keeps no install-state flags like Steam's, so the state is inferred from the folder.</summary>
internal static class GogStateResolver
{
    /// <summary>heroic-gogdl keeps these in the game folder until a download or update completes.</summary>
    public static readonly string[] DownloadMarkers = [".gogdl-resume", ".gogdl-download-cache"];

    public static GogState Resolve(IFileSystem fileSystem, string directory, string? recordedBuildId, GogGameInfo? info)
    {
        if (!fileSystem.DirectoryExists(directory))
        {
            // A folder on an unplugged drive is not an uninstall.
            return Path.GetPathRoot(directory) is { Length: > 0 } root && fileSystem.DirectoryExists(root)
                ? new(GameInstallState.Broken, GameStateReason.InstallDirectoryMissing)
                : new(GameInstallState.Offline, GameStateReason.LibraryOffline);
        }

        if (DownloadMarkers.Any(marker => fileSystem.GetEntry(Path.Join(directory, marker)) is not null))
        {
            return new(info is null ? GameInstallState.Installing : GameInstallState.Updating, GameStateReason.DownloadInProgress);
        }

        if (info is null)
        {
            // Very old installers write no .info file, so the game still counts as installed.
            return new(GameInstallState.Installed, GameStateReason.InfoFileMissing);
        }

        // The launcher rewrites its record and the .info file at different moments of an update.
        return recordedBuildId is not null && info.BuildId is not null && recordedBuildId != info.BuildId
            ? new(GameInstallState.Updating, GameStateReason.BuildMismatch)
            : new(GameInstallState.Installed, GameStateReason.None);
    }
}
