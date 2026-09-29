using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Rules;

namespace JustModIt.Manager.Creator;

public sealed record CreatorOptions
{
    /// <summary>The app's own folder at a game's root, which holds no game files and is never part of a package.</summary>
    public required string HiddenFolderName { get; init; }

    /// <summary>What builds the packages, e.g. the product name and version, written into their manifests.</summary>
    public required string Generator { get; init; }

    /// <summary>The app's extension for manifest packages, with its dot; the format is shared, the name is the app's.</summary>
    public required string PackageExtension { get; init; }

    /// <summary>What the app accepts when it installs a package; a package past them could never be installed.</summary>
    public PackageLimits Limits { get; init; } = PackageLimits.Default;

    public HostingRuleSet Rules { get; init; } = HostingRuleSet.Default;

    /// <summary>The extension of a package of the kind, with its dot: a plain archive is always a <c>.zip</c>.</summary>
    /// <param name="kind">A plain archive or a manifest package.</param>
    public string ExtensionFor(PackageKind kind) => kind == PackageKind.Zip ? PackageLayout.ZipExtension : PackageExtension;

    public static CreatorOptions For(AppIdentity identity, string generator, string packageExtension)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(generator);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageExtension);
        return new CreatorOptions
        {
            HiddenFolderName = identity.HiddenFolderName,
            Generator = generator,
            PackageExtension = packageExtension,
        };
    }
}
