namespace JustModIt.Manager.Core.Catalog;

/// <summary>Why nothing of a project can be installed on a game.</summary>
public enum SelectionUnavailability
{
    /// <summary>No release is public, or the project's status holds its releases back.</summary>
    NoPublicRelease,

    /// <summary>The public release has no finished file.</summary>
    NoReadyFiles,

    /// <summary>The package made for the game's store version and system comes in a format the app does not install.</summary>
    UnsupportedFormat,

    /// <summary>The packages are made for other store versions of the game.</summary>
    OtherStores,

    /// <summary>The packages for this store version are made for other systems than the game build's.</summary>
    OtherSystems,
}

/// <summary>
/// A choice the player makes on purpose, such as "text only" next to "text and voice", whose files are the same content
/// packaged for different stores or systems.
/// </summary>
public sealed record ReleaseVariant
{
    public required string Name { get; init; }

    public required IReadOnlyList<ReleaseFile> Files { get; init; }

    /// <summary>The file that fits the game; null when none does or the app does not install it.</summary>
    public ReleaseFile? Package { get; init; }

    /// <summary>Null exactly when <see cref="Package"/> is set.</summary>
    public SelectionUnavailability? Unavailable { get; init; }

    public bool IsInstallable => Package is not null;

    /// <summary>The first description a file of the variant gives, as the site shows it.</summary>
    public string Description => Files.Select(file => file.Description).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "";
}

/// <summary>What a project offers one game: its variants and the one to recommend.</summary>
public sealed record ReleaseSelection
{
    /// <summary>The public release; null when there is none to offer.</summary>
    public Release? Release { get; init; }

    /// <summary>The project's storefront links to the game; empty when the project no longer links it.</summary>
    public required IReadOnlyList<StorefrontLink> Links { get; init; }

    public required IReadOnlyList<ReleaseVariant> Variants { get; init; }

    /// <summary>The variant to recommend; null exactly when nothing can be installed.</summary>
    public ReleaseVariant? Default { get; init; }

    /// <summary>Why nothing can be installed; null when <see cref="Default"/> is set.</summary>
    public SelectionUnavailability? Unavailable { get; init; }

    public bool IsInstallable => Default is not null;

    /// <summary>The variant the player chose earlier, as long as it can still be installed.</summary>
    /// <param name="variantName">The variant's name as the player chose it.</param>
    public ReleaseVariant? FindInstallable(string variantName) =>
        Variants.FirstOrDefault(variant => variant.IsInstallable && variant.Name == variantName);

    /// <summary>
    /// The remembered variant while it can be installed, otherwise the recommended one; <c>FellBack</c> tells the player
    /// that their choice is gone.
    /// </summary>
    /// <param name="variantName">The remembered variant's name; null when the player never chose one.</param>
    public (ReleaseVariant? Variant, bool FellBack) Choose(string? variantName) =>
        variantName is null ? (Default, false)
        : FindInstallable(variantName) is { } remembered ? (remembered, false)
        : (Default, Default is not null);
}
