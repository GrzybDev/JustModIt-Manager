using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// What an install changed, kept so it can be shown, verified and undone offline even when the site or the project is
/// gone, with the list of files stored apart (<see cref="InstalledFiles"/>) and loaded only when it is needed.
/// </summary>
public sealed record InstallRecord
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required Guid Id { get; init; }

    public required GameKey Game { get; init; }

    public required string GameName { get; init; }

    /// <summary>The canonical game folder at install time.</summary>
    public required string InstallDirectory { get; init; }

    /// <summary>The volume of the game folder, to tell a deleted game from one on an unplugged drive.</summary>
    public string? GameVolume { get; init; }

    public string? GameBuildId { get; init; }

    /// <summary>
    /// The version of the game the install was made on, which tells whether originals taken then still fit the game; null
    /// in records written before the app recorded it.
    /// </summary>
    public GameVersion? GameVersion { get; init; }

    public required DateTimeOffset InstalledAt { get; init; }

    /// <summary>The first package of the install, a release's base, which older versions of the app show.</summary>
    public required InstalledPackage Package { get; init; }

    /// <summary>
    /// Every package the install came from, in the order they installed; null in records written before installs had parts.
    /// </summary>
    public IReadOnlyList<InstalledPart>? Parts { get; init; }

    /// <summary>
    /// The add-ons the install went without since their own checks failed on the game, which hold for the record's release
    /// and game build; null when none was left out.
    /// </summary>
    public IReadOnlyList<LeftOutFeature>? LeftOutFeatures { get; init; }

    public InstallSource? Source { get; init; }

    /// <summary>Where this install keeps the originals it replaced; a later change of the setting does not move them.</summary>
    public required BackupFolder Backup { get; init; }

    public int FileCount { get; init; }

    /// <summary>A package the package creator installed to try it out; it has no release to update to.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsTest { get; init; }

    /// <summary>
    /// The downloads the install's packages came from, which stay kept so that putting the install back downloads, and
    /// counts, nothing.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<Guid> PackageFileIds =>
        Parts is { } parts
            ? parts.Select(part => Guid.TryParse(part.CachedFileId ?? part.ReleaseFileId, out var file) ? file : (Guid?)null).OfType<Guid>()
            : Source?.ReleaseFileUuid is { } single ? [single] : [];
}

public sealed record BackupFolder(BackupLocation Location, string Directory);

/// <summary>The package an install came from.</summary>
/// <param name="Kind">A plain archive or a manifest package, as its content says.</param>
/// <param name="FileName">The package file as it was downloaded.</param>
/// <param name="ManifestId">The manifest's id; null for a plain archive.</param>
/// <param name="Title">The manifest's title.</param>
/// <param name="Version">The manifest's version.</param>
/// <param name="ThirdPartyNotices">The licences of third-party tools the manifest lists.</param>
/// <param name="RuntimeFiles">The game files the package's software changes while the game runs, as the manifest names them.</param>
public sealed record InstalledPackage(PackageKind Kind, string FileName, Guid? ManifestId = null, string? Title = null,
    string? Version = null, IReadOnlyList<ThirdPartyNotice>? ThirdPartyNotices = null, IReadOnlyList<string>? RuntimeFiles = null)
{
    /// <summary>What an install keeps of an opened package.</summary>
    /// <param name="package">The package.</param>
    public static InstalledPackage From(IPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new InstalledPackage(package.Kind, package.FileName, package.Manifest?.Id, package.Manifest?.Title,
            package.Manifest?.Version, package.Manifest?.ThirdPartyNotices,
            package.RuntimeFiles.Count > 0 ? [.. package.RuntimeFiles.Select(file => file.Path)] : null);
    }
}

/// <summary>One package an install came from.</summary>
/// <param name="Package">What the install keeps of the package.</param>
/// <param name="FeatureKey">The add-on it installed, by its key in the release; null for the base.</param>
/// <param name="FeatureName">The add-on's name when it was installed, shown offline.</param>
/// <param name="ReleaseFileId">The release file the package is; null for a package the package creator built.</param>
/// <param name="CachedFileId">The download the package was taken from, when that is another file whose copy it is.</param>
public sealed record InstalledPart(InstalledPackage Package, string? FeatureKey = null, string? FeatureName = null,
    string? ReleaseFileId = null, string? CachedFileId = null);

/// <summary>An add-on an install went without, since its own check failed on the game.</summary>
/// <param name="Key">The add-on's key.</param>
/// <param name="Name">Its name, shown offline.</param>
/// <param name="Reason">Why it was left out.</param>
public sealed record LeftOutFeature(string Key, string Name, PartLeftOutReason Reason);

/// <summary>Where a package came from on the site, supplied by the caller so an install can be shown offline.</summary>
public sealed record InstallSource
{
    public string? ProjectId { get; init; }

    public string? ProjectSlug { get; init; }

    public string? ProjectName { get; init; }

    public string? GameSlug { get; init; }

    public string? GameName { get; init; }

    public string? TeamSlug { get; init; }

    public string? TeamName { get; init; }

    public string? ReleaseId { get; init; }

    /// <summary>The team's own name for the release, if it gave one.</summary>
    public string? ReleaseLabel { get; init; }

    public int? ReleaseVersion { get; init; }

    /// <summary>Tells an older release from a newer one when the backend numbers none.</summary>
    public DateTimeOffset? ReleasePublishedAt { get; init; }

    /// <summary>The release file of the install's first package, its base.</summary>
    public string? ReleaseFileId { get; init; }

    /// <summary>The variant of a release from before add-ons that the install took, by its name; null for any other release.</summary>
    public string? VariantName { get; init; }

    /// <summary>
    /// The release offered several variants that fit the game, so the one taken was the player's choice; null in records
    /// written before the app recorded it.
    /// </summary>
    public bool? HadVariantChoice { get; init; }

    public Uri? CoverUrl { get; init; }

    /// <summary>A copy of the cover in the app's cache, shown offline.</summary>
    public string? CoverPath { get; init; }

    // The ids stay text in the record, so one that is not a GUID reads as missing instead of making the record unreadable.
    [JsonIgnore]
    public Guid? ProjectUuid => Guid.TryParse(ProjectId, out var id) ? id : null;

    [JsonIgnore]
    public Guid? ReleaseUuid => Guid.TryParse(ReleaseId, out var id) ? id : null;

    [JsonIgnore]
    public Guid? ReleaseFileUuid => Guid.TryParse(ReleaseFileId, out var id) ? id : null;
}

/// <summary>
/// The files an install touched and the folders it created, in the order it touched them; also the paths an uninstall left
/// for later in a kept-files record (<see cref="KeptAt"/>).
/// </summary>
public sealed record InstalledFiles
{
    public const int CurrentFormatVersion = 2;

    /// <summary>
    /// The format of a list without entries the package's software changes, still written for one, since a version that
    /// read only that format would half-undo a list with such entries; it refuses a newer one as unreadable instead.
    /// </summary>
    public const int FirstFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required IReadOnlyList<InstalledFile> Files { get; init; }

    /// <summary>Game-relative folders the install created, parents first.</summary>
    public required IReadOnlyList<string> CreatedDirectories { get; init; }

    /// <summary>Set on a kept-files record: when the uninstall left its paths.</summary>
    public DateTimeOffset? KeptAt { get; init; }

    /// <summary>The format the list needs: the first unless it holds what only the current format describes.</summary>
    [JsonIgnore]
    public int RequiredFormatVersion =>
        KeptAt is not null || Files.Any(file => file.Runtime) ? CurrentFormatVersion : FirstFormatVersion;
}

public enum FileChange
{
    Added,
    Replaced,
    Deleted,

    /// <summary>The install left the path as it was; it is listed because the package's software changes it.</summary>
    Unchanged,
}

/// <summary>A file's content as its size and hash tell it.</summary>
/// <param name="Size">The size in bytes.</param>
/// <param name="Sha256">The SHA-256 in lower-case hex.</param>
/// <param name="LastWriteTimeUtc">Lets verification skip hashing a file whose size and time are unchanged.</param>
public sealed record FileSnapshot(long Size, string Sha256, DateTime? LastWriteTimeUtc = null)
{
    /// <summary>Whether both snapshots hold the same content, whatever their times.</summary>
    /// <param name="other">Another snapshot.</param>
    public bool HasSameContent(FileSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Size == other.Size && FileHash.Matches(Sha256, other.Sha256);
    }
}

/// <summary>A file an install touched, or one the package's software changes while the game runs.</summary>
/// <param name="Path">Game-relative.</param>
/// <param name="Change">What the install did to it.</param>
/// <param name="Original">The content before the install; null for an added file or one the game did not have.</param>
/// <param name="Installed">
/// The content the install left; null for a deleted file and a path the install left as it was. In a kept-files record,
/// what the path held when the record was written.
/// </param>
/// <param name="Backup">The name of the original's backup in the record's backup folder; null for added files.</param>
/// <param name="OriginalAttributes">Attributes such as read-only that come back with the original.</param>
public sealed record InstalledFile(
    string Path,
    FileChange Change,
    FileSnapshot? Original = null,
    FileSnapshot? Installed = null,
    string? Backup = null,
    FileAttributes? OriginalAttributes = null)
{
    /// <summary>The package declares the path: its software changes it while the game runs, so no check reads it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Runtime { get; init; }

    /// <summary>The original may not be the store's file, so putting it back is followed by advice to check the files.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool OriginalUncertain { get; init; }

    /// <summary>The game's version the original was taken from, when an earlier install took it.</summary>
    public GameVersion? Taken { get; init; }

    /// <summary>The kept-files record the entry was inherited from; it loses the entry once this install commits.</summary>
    public Guid? InheritedFrom { get; init; }

    /// <summary>In a kept-files record: why the uninstall left the path; null for a path handed over to the next install.</summary>
    public KeptReason? Kept { get; init; }
}
