using JustModIt.Manager.Core;

namespace JustModIt.Manager.Creator;

public sealed record CreatorOptions
{
    /// <summary>The app's own folder at a game's root, which holds no game files and is never part of a package.</summary>
    public required string HiddenFolderName { get; init; }

    public static CreatorOptions For(AppIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new CreatorOptions { HiddenFolderName = identity.HiddenFolderName };
    }
}
