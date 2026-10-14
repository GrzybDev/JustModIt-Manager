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

    public required InstalledPackage Package { get; init; }

    public InstallSource? Source { get; init; }

    /// <summary>Where this install keeps the originals it replaced; a later change of the setting does not move them.</summary>
    public required BackupFolder Backup { get; init; }

    public int FileCount { get; init; }

    /// <summary>A package the package creator installed to try it out; it has no release to update to.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsTest { get; init; }
}

public sealed record BackupFolder(BackupLocation Location, string Directory);

/// <summary>The package an install came from.</summary>
/// <param name="Kind">A plain archive or a manifest package, as its content says.</param>
/// <param name="FileName">The package file as it was downloaded.</param>
/// <param name="ManifestId">The manifest's id; null for a plain archive.</param>
/// <param name="Title">The manifest's title.</param>
/// <param name="Version">The manifest's version.</param>
/// <param name="ThirdPartyNotices">The licences of third-party tools the manifest lists.</param>
public sealed record InstalledPackage(PackageKind Kind, string FileName, Guid? ManifestId = null, string? Title = null,
    string? Version = null, IReadOnlyList<ThirdPartyNotice>? ThirdPartyNotices = null)
{
    /// <summary>What an install keeps of an opened package.</summary>
    /// <param name="package">The package.</param>
    public static InstalledPackage From(IPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new InstalledPackage(package.Kind, package.FileName, package.Manifest?.Id, package.Manifest?.Title,
            package.Manifest?.Version, package.Manifest?.ThirdPartyNotices);
    }
}

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

    public string? ReleaseFileId { get; init; }

    public string? VariantName { get; init; }

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

/// <summary>The files an install touched and the folders it created, in the order it touched them.</summary>
public sealed record InstalledFiles
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required IReadOnlyList<InstalledFile> Files { get; init; }

    /// <summary>Game-relative folders the install created, parents first.</summary>
    public required IReadOnlyList<string> CreatedDirectories { get; init; }
}

public enum FileChange
{
    Added,
    Replaced,
    Deleted,
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

/// <summary>A file an install touched.</summary>
/// <param name="Path">Game-relative.</param>
/// <param name="Change">What the install did to it.</param>
/// <param name="Original">The content before the install; null for an added file.</param>
/// <param name="Installed">The content the install left; null for a deleted file.</param>
/// <param name="Backup">The name of the original's backup in the record's backup folder; null for added files.</param>
/// <param name="OriginalAttributes">Attributes such as read-only that come back with the original.</param>
public sealed record InstalledFile(
    string Path,
    FileChange Change,
    FileSnapshot? Original = null,
    FileSnapshot? Installed = null,
    string? Backup = null,
    FileAttributes? OriginalAttributes = null);
