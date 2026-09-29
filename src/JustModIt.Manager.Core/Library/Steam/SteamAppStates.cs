namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>
/// The current client's EAppState bits, as its <c>content_log.txt</c> names them; the table that Playnite and steamlocate
/// use is out of date from 0x8 upwards (it puts AppRunning at 0x40), so do not "fix" these values against it.
/// </summary>
[Flags]
public enum SteamAppStates : ulong
{
    None = 0,
    Uninstalled = 0x1,
    UpdateRequired = 0x2,
    FullyInstalled = 0x4,
    UpdateQueued = 0x8,
    UpdateOptional = 0x10,
    FilesMissing = 0x20,
    SharedOnly = 0x40,
    FilesCorrupt = 0x80,
    UpdateRunning = 0x100,
    UpdatePaused = 0x200,
    UpdateStarted = 0x400,
    Uninstalling = 0x800,
    BackupRunning = 0x1000,
    AppRunning = 0x2000,
    ComponentInUse = 0x4000,
    MovingFolder = 0x8000,
    Terminating = 0x10000,
    PrefetchingInfo = 0x20000,
    PeerServer = 0x40000,
    UpdateDisabledByApp = 0x80000,
    Reconfiguring = 0x100000,
    VerifyingInstalled = 0x200000,
    Preallocating = 0x400000,
    Downloading = 0x800000,
    Staging = 0x1000000,
    Committing = 0x2000000,
    VerifyingStaged = 0x4000000,
    Stopping = 0x8000000,
}

internal static class SteamAppStateGroups
{
    /// <summary>The only bits that allow writing; anything else means Steam is, or may be, doing something.</summary>
    public const SteamAppStates Neutral =
        SteamAppStates.FullyInstalled | SteamAppStates.UpdateOptional | SteamAppStates.UpdateDisabledByApp;

    public const SteamAppStates InUse = SteamAppStates.AppRunning | SteamAppStates.ComponentInUse | SteamAppStates.PeerServer;

    public const SteamAppStates Verifying = SteamAppStates.VerifyingInstalled | SteamAppStates.VerifyingStaged;

    public const SteamAppStates Writing = SteamAppStates.Downloading | SteamAppStates.Staging | SteamAppStates.Committing;

    public const SteamAppStates Busy =
        SteamAppStates.UpdateQueued | SteamAppStates.UpdateRunning | SteamAppStates.UpdatePaused
        | SteamAppStates.UpdateStarted | SteamAppStates.Uninstalling | SteamAppStates.BackupRunning
        | SteamAppStates.MovingFolder | SteamAppStates.Terminating | SteamAppStates.PrefetchingInfo
        | SteamAppStates.Reconfiguring | SteamAppStates.VerifyingInstalled | SteamAppStates.Preallocating
        | SteamAppStates.Downloading | SteamAppStates.Staging | SteamAppStates.Committing
        | SteamAppStates.VerifyingStaged | SteamAppStates.Stopping;
}

/// <summary>The client's EAppUpdateError, stored as <c>UpdateResult</c> in an app manifest.</summary>
public enum SteamUpdateResult
{
    NoError = 0,
    Unspecified = 1,
    Paused = 2,
    Canceled = 3,
    Suspended = 4,
    NoSubscription = 5,
    NoConnection = 6,
    ConnectionTimeout = 7,
    MissingDecryptionKey = 8,
    MissingConfiguration = 9,
    DiskReadFailure = 10,
    DiskWriteFailure = 11,
    NotEnoughDiskSpace = 12,
    CorruptGameFiles = 13,
    WaitingForDisk = 14,
    InvalidInstallPath = 15,
    ApplicationRunning = 16,
    DependencyFailure = 17,
    NotInstalled = 18,
    UpdateRequired = 19,
    StillBusy = 20,
    NoConnectionToContentServers = 21,
    InvalidApplicationConfiguration = 22,
    InvalidContentConfiguration = 23,
    ManifestUnavailable = 24,
    NotReleased = 25,
    RegionRestricted = 26,
    CorruptDepotCache = 27,
    MissingExecutable = 28,
    InvalidPlatform = 29,
    InvalidFileSystem = 30,
    CorruptUpdateFiles = 31,
    DownloadDisabled = 32,
    SharedLibraryLocked = 33,
    PurchasePending = 34,
    OtherSessionPlaying = 35,
    CorruptDownload = 36,
    CorruptDisk = 37,
    MissingFilePermissions = 38,
    FileLocked = 39,
    ContentUnavailable = 40,
    Requires64BitOS = 41,
    MissingUpdateFiles = 42,
    NotEnoughDiskQuota = 43,
    SiteLicenseLocked = 44,
    ParentalControlBlocked = 45,
    CreateProcessFailed = 46,
    SteamClientOutOfDate = 47,
    AllowedPlaytimeExceeded = 48,
    CorruptFileSignature = 49,
    MissingGameFiles = 50,
    CompatToolFailed = 51,
    InstallPathRemoved = 52,
    InvalidBackupPath = 53,
    InvalidPasscode = 54,
    SelfUpdating = 55,
    BlockedArguments = 56,
}
