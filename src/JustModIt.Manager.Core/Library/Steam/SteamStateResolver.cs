using System.Globalization;
using System.Text;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Library.Steam;

public enum SteamAppStatus
{
    Unknown,
    NotInstalled,
    Queued,
    Installing,
    Installed,
    UpdatePending,
    Updating,
    Validating,
    Uninstalling,
    MovingFolder,
    InUse,
    FilesMissing,
    FilesCorrupt,
    InstallDirectoryMissing,
}

/// <summary>Signals that show what Steam is doing right now, which the manifest on disk does not.</summary>
/// <param name="Download">What <c>steamapps/downloading</c> holds for the app.</param>
/// <param name="IsRegistryUpdating">The client's per-app <c>Updating</c> registry value is set.</param>
internal readonly record struct SteamLiveSignals(SteamDownload Download = SteamDownload.None, bool IsRegistryUpdating = false);

/// <summary>What Steam is doing with an app, as its manifest and the live signals tell.</summary>
/// <param name="Status">The app's state, such as installed, updating or in use.</param>
/// <param name="Flags">The manifest's state bits.</param>
/// <param name="IsPaused">Its install or update is paused.</param>
/// <param name="IsPartiallyDownloaded">Steam keeps download data for the app.</param>
/// <param name="HasOptionalUpdate">Steam offers an update the app does not need.</param>
/// <param name="ScheduledUpdate">When Steam plans to apply a pending update; null when it plans none.</param>
/// <param name="UpdateResult">How the last update job ended, when the manifest says.</param>
internal sealed record SteamInstallStatus(
    SteamAppStatus Status,
    SteamAppStates Flags,
    bool IsPaused = false,
    bool IsPartiallyDownloaded = false,
    bool HasOptionalUpdate = false,
    DateTimeOffset? ScheduledUpdate = null,
    SteamUpdateResult? UpdateResult = null)
{
    /// <summary>Null when nothing of the app is on disk yet, so it is not listed as a game.</summary>
    public (GameInstallState State, GameStateReason Reason)? GameState => Status switch
    {
        // Steam writes a manifest for every queued install, so the queue would otherwise flood the library.
        SteamAppStatus.NotInstalled or SteamAppStatus.Queued => null,
        SteamAppStatus.Installed => (GameInstallState.Installed,
            IsPartiallyDownloaded ? GameStateReason.UnfinishedDownload : GameStateReason.None),
        SteamAppStatus.Installing => (GameInstallState.Installing, IsPaused ? GameStateReason.UpdatePaused : GameStateReason.None),
        SteamAppStatus.UpdatePending => (GameInstallState.UpdatePending,
            IsPartiallyDownloaded ? GameStateReason.DownloadInProgress : GameStateReason.UpdateRequired),
        SteamAppStatus.Updating => (GameInstallState.Updating, IsPaused ? GameStateReason.UpdatePaused
            : IsPartiallyDownloaded ? GameStateReason.DownloadInProgress
            : GameStateReason.StoreBusy),
        SteamAppStatus.Validating => (GameInstallState.Validating, GameStateReason.None),
        SteamAppStatus.Uninstalling => (GameInstallState.Updating, GameStateReason.Uninstalling),
        SteamAppStatus.MovingFolder => (GameInstallState.Updating, GameStateReason.MovingFolder),
        SteamAppStatus.InUse => (GameInstallState.Running, GameStateReason.GameRunning),
        SteamAppStatus.FilesMissing => (GameInstallState.Broken, GameStateReason.FilesMissing),
        SteamAppStatus.FilesCorrupt => (GameInstallState.Broken, GameStateReason.FilesCorrupt),
        SteamAppStatus.InstallDirectoryMissing => (GameInstallState.Broken, GameStateReason.InstallDirectoryMissing),
        _ => (GameInstallState.Unknown, GameStateReason.UnknownStoreState),
    };

    /// <summary>
    /// Describes the status for the log, e.g. <c>Updating (paused); StateFlags 1542 [UpdateRequired, FullyInstalled, …]</c>.
    /// </summary>
    public string Describe()
    {
        var qualifiers = new List<string>();
        if (IsPaused)
        {
            qualifiers.Add("paused");
        }

        if (IsPartiallyDownloaded)
        {
            qualifiers.Add("partially downloaded");
        }

        if (HasOptionalUpdate)
        {
            qualifiers.Add("optional update available");
        }

        if (ScheduledUpdate is { } scheduled)
        {
            qualifiers.Add(string.Create(CultureInfo.InvariantCulture, $"update scheduled for {scheduled:u}"));
        }

        if (UpdateResult is { } result and not SteamUpdateResult.NoError)
        {
            qualifiers.Add($"last update result {result}");
        }

        var builder = new StringBuilder(Status.ToString());
        if (qualifiers.Count > 0)
        {
            builder.Append(" (").AppendJoin(", ", qualifiers).Append(')');
        }

        return builder.Append(CultureInfo.InvariantCulture, $"; StateFlags {(ulong)Flags} [{Flags}]").ToString();
    }
}

/// <summary>
/// Works out what Steam is doing with an app, failing safe: any bit but those of an idle installed app blocks writing,
/// while download data no job claims only warns, since a cancelled or abandoned job leaves it behind for good.
/// </summary>
internal static class SteamStateResolver
{
    /// <summary>Works out what Steam is doing with the app of <paramref name="manifest"/>.</summary>
    /// <param name="manifest">The app's manifest.</param>
    /// <param name="installDirectoryExists">The app's folder under <c>steamapps/common</c> exists.</param>
    /// <param name="live">What Steam shows outside the manifest; none by default.</param>
    public static SteamInstallStatus Resolve(SteamAppManifest manifest, bool installDirectoryExists, SteamLiveSignals live = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var flags = manifest.StateFlags;
        var paused = flags.HasFlag(SteamAppStates.UpdatePaused)
            || manifest.UpdateResult is SteamUpdateResult.Paused or SteamUpdateResult.Canceled or SteamUpdateResult.Suspended;
        var status = new SteamInstallStatus(SteamAppStatus.Unknown, flags, UpdateResult: manifest.UpdateResult);

        if (flags == SteamAppStates.None)
        {
            return status;
        }

        if (flags.HasFlag(SteamAppStates.Uninstalling))
        {
            return status with { Status = SteamAppStatus.Uninstalling };
        }

        if (flags.HasFlag(SteamAppStates.MovingFolder))
        {
            return status with { Status = SteamAppStatus.MovingFolder };
        }

        if (!flags.HasFlag(SteamAppStates.FullyInstalled))
        {
            return status with { Status = ResolveNotInstalled(manifest, flags, live), IsPaused = paused };
        }

        if (flags.HasFlag(SteamAppStates.FilesMissing))
        {
            return status with { Status = SteamAppStatus.FilesMissing };
        }

        if (flags.HasFlag(SteamAppStates.FilesCorrupt))
        {
            return status with { Status = SteamAppStatus.FilesCorrupt };
        }

        if (!installDirectoryExists)
        {
            return status with { Status = SteamAppStatus.InstallDirectoryMissing };
        }

        if ((flags & SteamAppStateGroups.Verifying) != 0 && (flags & SteamAppStateGroups.Writing) == 0)
        {
            return status with { Status = SteamAppStatus.Validating };
        }

        if ((flags & SteamAppStateGroups.Busy) != 0 || live.IsRegistryUpdating)
        {
            return status with { Status = SteamAppStatus.Updating, IsPaused = paused };
        }

        status = status with { IsPartiallyDownloaded = live.Download != SteamDownload.None };
        if (flags.HasFlag(SteamAppStates.UpdateRequired) || manifest.HasPendingTargetBuild)
        {
            return status with { Status = SteamAppStatus.UpdatePending, ScheduledUpdate = manifest.ScheduledAutoUpdate };
        }

        if (live.Download == SteamDownload.Active)
        {
            return status with { Status = SteamAppStatus.Updating };
        }

        var unexpected = flags & ~SteamAppStateGroups.Neutral;
        return status with
        {
            Status = (unexpected & SteamAppStateGroups.InUse) != 0 ? SteamAppStatus.InUse
                : unexpected != 0 ? SteamAppStatus.Unknown
                : SteamAppStatus.Installed,
            HasOptionalUpdate = flags.HasFlag(SteamAppStates.UpdateOptional),
        };
    }

    private static SteamAppStatus ResolveNotInstalled(SteamAppManifest manifest, SteamAppStates flags, SteamLiveSignals live)
    {
        if ((flags & (SteamAppStates.Uninstalled | SteamAppStates.SharedOnly)) != 0
            && (flags & (SteamAppStates.UpdateRequired | SteamAppStates.UpdateStarted)) == 0)
        {
            return SteamAppStatus.NotInstalled;
        }

        var started = flags.HasFlag(SteamAppStates.UpdateStarted)
            || manifest.BytesDownloaded > 0
            || live.Download != SteamDownload.None
            || live.IsRegistryUpdating;
        return started ? SteamAppStatus.Installing : SteamAppStatus.Queued;
    }
}
