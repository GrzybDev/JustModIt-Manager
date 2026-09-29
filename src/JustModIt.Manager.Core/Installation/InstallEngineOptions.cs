namespace JustModIt.Manager.Core.Installation;

/// <summary>Where the install engine keeps its data, named after the app so two apps never share it.</summary>
public sealed record InstallEngineOptions
{
    /// <summary>Names the app's folder inside a backup folder the player picked.</summary>
    public required string AppId { get; init; }

    /// <summary>The hidden folder at a game's root that holds staging files and, by default, the originals.</summary>
    public required string HiddenFolderName { get; init; }

    /// <summary>One folder per install record, holding the record and its journal.</summary>
    public required string InstallsDirectory { get; init; }

    /// <summary>Originals of games whose backups the player keeps in the app's data folder.</summary>
    public required string BackupsDirectory { get; init; }

    /// <summary>From this many bytes of originals copied to another drive, the player is told that it takes a while.</summary>
    public long SlowBackupCopyBytes { get; init; } = 1L << 30;

    /// <summary>Called right before every change, so tests can capture the state a crash would leave at that point.</summary>
    internal Action<ChangeKind, string?>? BeforeChange { get; init; }

    /// <summary>The options of an app.</summary>
    /// <param name="identity">The app's names.</param>
    /// <param name="locations">The app's data folders.</param>
    public static InstallEngineOptions For(AppIdentity identity, AppDataLocations locations)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(locations);
        return new InstallEngineOptions
        {
            AppId = identity.AppId,
            HiddenFolderName = identity.HiddenFolderName,
            InstallsDirectory = locations.InstallsDirectory,
            BackupsDirectory = locations.BackupsDirectory,
        };
    }
}
