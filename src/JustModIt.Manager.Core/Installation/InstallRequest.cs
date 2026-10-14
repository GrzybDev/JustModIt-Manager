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

/// <summary>Packages to install on a game as one install, the first of a release's being its base.</summary>
/// <param name="Game">The game as the library detected it.</param>
/// <param name="Parts">The packages in the order they install, so a later one's file replaces an earlier one's.</param>
/// <param name="Backup">Where the install keeps the originals.</param>
/// <param name="Source">What the site says about the packages, kept in the record.</param>
/// <param name="IsTest">Packages the package creator installs to try them out, which are never updated.</param>
public sealed record InstallRequest(InstalledGame Game, IReadOnlyList<InstallPart> Parts, BackupChoice Backup, InstallSource? Source = null,
    bool IsTest = false)
{
    /// <summary>A request to install one package.</summary>
    /// <param name="game">The game as the library detected it.</param>
    /// <param name="packagePath">The package file's full path.</param>
    /// <param name="backup">Where the install keeps the originals.</param>
    /// <param name="source">What the site says about the package, kept in the record.</param>
    /// <param name="isTest">A package the package creator installs to try it out, which is never updated.</param>
    public InstallRequest(InstalledGame game, string packagePath, BackupChoice backup, InstallSource? source = null, bool isTest = false)
        : this(game, [new InstallPart(packagePath)], backup, source, isTest)
    {
    }

    /// <summary>
    /// The install a replacement removed that this one puts back, taking over what its uninstall left for later; null for any
    /// other install.
    /// </summary>
    public Guid? Restores { get; init; }
}

/// <summary>One package of an install, with what the site says about it for the install's record.</summary>
/// <param name="PackagePath">The package file's full path.</param>
public sealed record InstallPart(string PackagePath)
{
    /// <summary>
    /// The install may go without the part when its own check fails on the game, such as an add-on; never set on a part the
    /// install is for, such as a release's base.
    /// </summary>
    public bool IsOptional { get; init; }

    /// <summary>The add-on the part installs, by its key in the release; null for the base.</summary>
    public string? FeatureKey { get; init; }

    /// <summary>The add-on's name, shown offline.</summary>
    public string? FeatureName { get; init; }

    /// <summary>The release file the package is; null for a package the package creator built.</summary>
    public string? ReleaseFileId { get; init; }

    /// <summary>The download the package was taken from, when that is another file whose copy it is.</summary>
    public string? CachedFileId { get; init; }
}
