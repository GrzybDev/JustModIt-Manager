using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Rules;

namespace JustModIt.Manager.Creator;

public sealed record CreatorOptions
{
    /// <summary>The app's own folder at a game's root, which holds no game files and is never part of a package.</summary>
    public required string HiddenFolderName { get; init; }

    /// <summary>What the app accepts when it installs a package; a package past them could never be installed.</summary>
    public PackageLimits Limits { get; init; } = PackageLimits.Default;

    public HostingRuleSet Rules { get; init; } = HostingRuleSet.Default;

    public static CreatorOptions For(AppIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new CreatorOptions { HiddenFolderName = identity.HiddenFolderName };
    }
}
