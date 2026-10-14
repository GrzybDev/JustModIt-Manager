using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>The package formats the app installs.</summary>
public enum PackageFormat
{
    /// <summary>A ZIP archive of files laid out relative to the game root, also offered for download on the site.</summary>
    Zip,

    /// <summary>A manifest package, which only the app installs; each app gives it an extension of its own.</summary>
    Manifest,
}

public sealed record Release
{
    public required Guid Uuid { get; init; }

    /// <summary>Counts the project's releases from 1; a newer release has a higher number.</summary>
    public int Version { get; init; }

    /// <summary>The label the site shows: the team's own name, or one made from the number when it gave none.</summary>
    public string Name { get; init => field = value ?? ""; } = "";

    /// <summary>The team's own name for the release; empty when it gave none.</summary>
    public string NameRaw { get; init => field = value ?? ""; } = "";

    public string Changelog { get; init => field = value ?? ""; } = "";

    public bool IsPublic { get; init; }

    /// <summary>Hidden by staff, so offered to nobody; <see cref="IsPublic"/> keeps its value for when it is restored.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Null when the site does not know, which never means the release's creation.</summary>
    public DateTimeOffset? FirstPublishedAt { get; init; }

    /// <summary>
    /// The base's files, those every install of the release takes, including files whose upload has not finished; see
    /// <see cref="ReleaseFile.IsReady"/>.
    /// </summary>
    public IReadOnlyList<ReleaseFile> Files { get; init => field = value ?? []; } = [];

    /// <summary>
    /// The release's optional add-ons in the order they install; null from a backend that does not say, which also has no
    /// endpoint that gives the files of a release together.
    /// </summary>
    public IReadOnlyList<ReleaseFeature>? Features { get; init; }

    /// <summary>The file the site offers first; never an app-only package.</summary>
    public ReleaseFile? DefaultFile { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>An optional part of a release, with packages of its own.</summary>
public sealed record ReleaseFeature
{
    public required Guid Uuid { get; init; }

    /// <summary>The add-on's name as a slug, which stays when it is renamed and carries over to the project's next release.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary>Plain text.</summary>
    public string Description { get; init => field = value ?? ""; } = "";

    public int Position { get; init; }

    /// <summary>Whether the add-on comes with the release unless the player says otherwise.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Add-ons sharing it exclude each other, and it is the caption of the choice; empty for none.</summary>
    public string Group { get; init => field = value ?? ""; } = "";

    /// <summary>Includes files whose upload has not finished; see <see cref="ReleaseFile.IsReady"/>.</summary>
    public IReadOnlyList<ReleaseFile> Files { get; init => field = value ?? []; } = [];
}

/// <summary>One downloadable package of a release's base or of one of its add-ons, made for some stores or systems.</summary>
public sealed record ReleaseFile
{
    private const string ReadyStatus = "ready";

    public required Guid Uuid { get; init; }

    /// <summary>
    /// The base files of a release from before add-ons that share it are packages of one variant; empty for an add-on's
    /// files.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>The add-on the file belongs to; null for a base file.</summary>
    public Guid? FeatureUuid { get; init; }

    /// <summary>
    /// The file this one copies, with the same bytes, as the project's next release takes over an earlier one's add-ons;
    /// null for a file uploaded itself.
    /// </summary>
    public Guid? CopiedFrom { get; init; }

    public string Description { get; init => field = value ?? ""; } = "";

    /// <summary>Set for packages in the app's own format, which the site does not offer as direct downloads.</summary>
    public bool RequiresManager { get; init; }

    public bool IsDefault { get; init; }

    public long DownloadCount { get; init; }

    /// <summary>Not unique: two files of one release may share it.</summary>
    public string DownloadFilename { get; init => field = value ?? ""; } = "";

    /// <summary>In bytes; null while the upload is pending.</summary>
    public long? Size { get; init; }

    /// <summary>Lower-case hex, when the backend records it.</summary>
    public string Md5 { get; init => field = value ?? ""; } = "";

    /// <summary>Lower-case hex, when the backend records it.</summary>
    public string Sha256 { get; init => field = value ?? ""; } = "";

    /// <summary><c>ready</c> once downloadable, <c>pending</c> while uploading, or a value added later.</summary>
    public string Status { get; init => field = value ?? ""; } = "";

    public int Position { get; init; }

    /// <summary>The project's storefront links this package is made for; empty means every store version.</summary>
    public IReadOnlyList<Guid> StorefrontLinkUuids { get; init => field = value ?? []; } = [];

    /// <summary>Empty means every system.</summary>
    public IReadOnlyList<OperatingSystemKind> OperatingSystems { get; init => field = value ?? []; } = [];

    /// <summary>Pages on other hosts; never direct and never counted, so never used for installing.</summary>
    public IReadOnlyList<ReleaseFileMirror> Mirrors { get; init => field = value ?? []; } = [];

    public DateTimeOffset? CreatedAt { get; init; }

    [JsonIgnore]
    public bool IsReady => Status == ReadyStatus;

    /// <summary>
    /// How the app installs the file, null when it does not; a file that needs the app is known by the site's flag,
    /// whatever its extension, since apps name manifest packages differently.
    /// </summary>
    [JsonIgnore]
    public PackageFormat? Format =>
        RequiresManager ? PackageFormat.Manifest
        : Path.GetExtension(DownloadFilename).Equals(PackageLayout.ZipExtension, StringComparison.OrdinalIgnoreCase) ? PackageFormat.Zip
        : null;
}

public sealed record ReleaseFileMirror
{
    public required Guid Uuid { get; init; }

    public string Label { get; init => field = value ?? ""; } = "";

    public string Url { get; init => field = value ?? ""; } = "";

    public int Position { get; init; }
}

/// <summary>What the download endpoint needs to name one release file.</summary>
/// <param name="TeamSlug">The slug of the team that made the project.</param>
/// <param name="ProjectSlug">The project's slug within the team.</param>
/// <param name="ReleaseUuid">The release the file belongs to.</param>
/// <param name="FileUuid">The file to download.</param>
public readonly record struct ReleaseFileLocator(string TeamSlug, string ProjectSlug, Guid ReleaseUuid, Guid FileUuid)
{
    /// <summary>The release the file belongs to.</summary>
    public ReleaseLocator Release => new(TeamSlug, ProjectSlug, ReleaseUuid);
}

/// <summary>What the release's download endpoint needs to name a release.</summary>
/// <param name="TeamSlug">The slug of the team that made the project.</param>
/// <param name="ProjectSlug">The project's slug within the team.</param>
/// <param name="ReleaseUuid">The release.</param>
public readonly record struct ReleaseLocator(string TeamSlug, string ProjectSlug, Guid ReleaseUuid);

internal sealed record DownloadLink
{
    public string DownloadUrl { get; init => field = value ?? ""; } = "";
}

internal sealed record ReleaseDownloadLinks
{
    public IReadOnlyList<ReleaseDownloadLink> Files { get; init => field = value ?? []; } = [];
}

internal sealed record ReleaseDownloadLink
{
    public Guid Uuid { get; init; }

    public string DownloadUrl { get; init => field = value ?? ""; } = "";
}
