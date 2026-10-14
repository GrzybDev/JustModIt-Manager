using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.Installation;

/// <summary>Where a new install keeps originals, as the settings say at the time.</summary>
/// <param name="Location">Which kind of folder holds them.</param>
/// <param name="CustomFolder">The player's folder for <see cref="BackupLocation.CustomFolder"/>.</param>
public sealed record BackupChoice(BackupLocation Location, string? CustomFolder = null)
{
    public static BackupChoice GameFolder { get; } = new(BackupLocation.GameFolder);

    /// <summary>The choice the settings make now.</summary>
    /// <param name="settings">The player's settings.</param>
    public static BackupChoice From(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new BackupChoice(settings.BackupLocation, settings.CustomBackupFolder);
    }
}

/// <summary>A package to install on a game.</summary>
/// <param name="Game">The game as the library detected it.</param>
/// <param name="PackagePath">The package file's full path.</param>
/// <param name="Backup">Where the install keeps the originals.</param>
/// <param name="Source">What the site says about the package, kept in the record.</param>
/// <param name="IsTest">A package the package creator installs to try it out, which is never updated.</param>
public sealed record InstallRequest(InstalledGame Game, string PackagePath, BackupChoice Backup, InstallSource? Source = null,
    bool IsTest = false)
{
    /// <summary>
    /// The install a replacement removed that this one puts back, taking over what its uninstall left for later; null for any
    /// other install.
    /// </summary>
    public Guid? Restores { get; init; }
}
