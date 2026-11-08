using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Packages;

/// <summary>The <c>manifest.json</c> of a manifest package (see docs/package-format.md).</summary>
public sealed record PackageManifest
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required string Version { get; init; }

    // The source generator sets every init-only property, so a missing list arrives as null rather than as its default.
    public IReadOnlyList<string> Authors
    {
        get => field ?? [];
        init;
    }

    public DateTimeOffset? CreatedAt { get; init; }

    public string? Generator { get; init; }

    public IReadOnlyList<PackageTarget> Targets
    {
        get => field ?? [];
        init;
    }

    /// <summary>
    /// Capabilities the package needs (see <see cref="PackageCapabilities"/>), so an app that lacks one refuses the package
    /// as made for a newer version instead of calling it damaged.
    /// </summary>
    public IReadOnlyList<string>? Requires { get; init; }

    /// <summary>Third-party components the package carries, which older apps ignore, so it needs no capability.</summary>
    public IReadOnlyList<ThirdPartyNotice>? ThirdPartyNotices { get; init; }

    /// <summary>
    /// Game files the package's software changes while the game runs, which an app that ignored them would never undo, so
    /// a list that is not empty needs <see cref="PackageCapabilities.RuntimeFiles"/>.
    /// </summary>
    public IReadOnlyList<RuntimeFile>? RuntimeFiles { get; init; }

    public required IReadOnlyList<PackageOperation> Operations { get; init; }

    /// <summary>Where the package's folders lie on a game, as the first target with its store and id names them.</summary>
    /// <param name="game">The game; null for the package as written.</param>
    /// <returns>The target's rules; null when no target names the game or its target names none.</returns>
    public IReadOnlyList<FolderRename>? FoldersOn(GameKey? game) =>
        Targets.FirstOrDefault(target => target.Store == game?.Store && target.Id == game?.StoreGameId)?.Folders;
}

/// <summary>A game the package is made for.</summary>
/// <param name="Store">The store the game comes from.</param>
/// <param name="Id">The store's id of the game, e.g. a Steam AppID or a GOG product id.</param>
/// <param name="BuildId">The build the package was made and tested with.</param>
/// <param name="Folders">
/// Where the package's folders lie on this game, which an app that ignored them would miss, so a list that is not empty
/// needs <see cref="PackageCapabilities.TargetFolders"/>; null while the game has the package's layout.
/// </param>
public sealed record PackageTarget(StoreId Store, string Id, string? BuildId = null, IReadOnlyList<FolderRename>? Folders = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(CopyOperation), PackageCapabilities.Copy)]
[JsonDerivedType(typeof(PatchOperation), PackageCapabilities.Patch)]
[JsonDerivedType(typeof(RenameOperation), PackageCapabilities.Rename)]
[JsonDerivedType(typeof(DeleteOperation), PackageCapabilities.Delete)]
public abstract record PackageOperation
{
    /// <summary>
    /// The stores whose games the operation runs on, which an app that ignored them would run it on everywhere, so a list
    /// needs <see cref="PackageCapabilities.TargetsOnly"/>; null for every store.
    /// </summary>
    public IReadOnlyList<StoreId>? Only { get; init; }
}

/// <summary>Copies a payload file into the game.</summary>
/// <param name="Path">Game-relative destination.</param>
/// <param name="Source">The payload entry, under <c>files/</c>.</param>
/// <param name="Size">The payload's size in bytes.</param>
/// <param name="Sha256">The payload's SHA-256 in hex.</param>
public sealed record CopyOperation(string Path, string Source, long Size, string Sha256) : PackageOperation;

/// <summary>Patches a game file with an xdelta3 payload, or with the one of several that fits what the file holds.</summary>
/// <param name="Path">The game-relative file to patch.</param>
/// <param name="Patch">The xdelta3 payload entry, under <c>patches/</c>; null with <paramref name="Alternatives"/>.</param>
/// <param name="SourceSize">The original's size in bytes; null with <paramref name="Alternatives"/>.</param>
/// <param name="SourceSha256">The original's SHA-256 in hex; null with <paramref name="Alternatives"/>.</param>
/// <param name="TargetSize">The patched file's size in bytes; null with <paramref name="Alternatives"/>.</param>
/// <param name="TargetSha256">The patched file's SHA-256 in hex; null with <paramref name="Alternatives"/>.</param>
/// <param name="Alternatives">
/// The patches for each original the file may hold, which an app that ignored them would call damaged, so a list needs
/// <see cref="PackageCapabilities.PatchAlternatives"/>; null for one patch.
/// </param>
public sealed record PatchOperation(string Path, string? Patch = null, long? SourceSize = null, string? SourceSha256 = null,
    long? TargetSize = null, string? TargetSha256 = null, IReadOnlyList<PatchAlternative>? Alternatives = null) : PackageOperation
{
    public const int MaxAlternatives = 8;
}

/// <summary>A patch for one of the originals a file may hold.</summary>
/// <param name="Patch">The xdelta3 payload entry, under <c>patches/</c>, which alternatives may share.</param>
/// <param name="SourceSize">The original's size in bytes.</param>
/// <param name="SourceSha256">The original's SHA-256 in hex.</param>
/// <param name="TargetSize">The patched file's size in bytes.</param>
/// <param name="TargetSha256">The patched file's SHA-256 in hex.</param>
public sealed record PatchAlternative(string Patch, long SourceSize, string SourceSha256, long TargetSize, string TargetSha256);

/// <summary>Renames a game file.</summary>
/// <param name="From">The game-relative file to rename.</param>
/// <param name="To">Its new game-relative path.</param>
public sealed record RenameOperation(string From, string To) : PackageOperation;

/// <summary>Deletes a game file.</summary>
/// <param name="Path">The game-relative file to delete.</param>
public sealed record DeleteOperation(string Path) : PackageOperation;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    // Not the system's line ending, so a manifest's bytes do not depend on the OS that wrote it.
    NewLine = "\n",
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    // Hand-written manifests may put "type" after the other fields.
    AllowOutOfOrderMetadataProperties = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(StoreIdJsonConverter)])]
[JsonSerializable(typeof(PackageManifest))]
public sealed partial class PackageJsonContext : JsonSerializerContext;
