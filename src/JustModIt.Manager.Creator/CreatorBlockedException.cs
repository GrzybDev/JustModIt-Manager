using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Creator;

/// <summary>An install the agent keeps a record of, as the creator is told about it.</summary>
/// <param name="RecordId">The install's record.</param>
/// <param name="InstallDirectory">The game folder the install changed.</param>
public sealed record ExistingInstall(Guid RecordId, string InstallDirectory);

public enum CreatorBlockReason
{
    GameFolderMissing,
    AuthorFolderMissing,

    /// <summary>One folder holds the other, so the author's files would count as game files or the other way round.</summary>
    FoldersOverlap,

    /// <summary>
    /// A package or a test install is on the game, whose files would be compared or patched as if they were the game's,
    /// giving every player a package that does not fit their game.
    /// </summary>
    GameHasInstall,

    /// <summary>
    /// Another app, or another build of this one, manages files of the game, which would be taken for the game's just
    /// like those of an install.
    /// </summary>
    GameManagedByOtherApp,
}

/// <summary>One reason the creator refused to work on a game.</summary>
/// <param name="Reason">Why the creator refused.</param>
/// <param name="Path">The folder the reason is about.</param>
/// <param name="InstallId">The install on the game, for <see cref="CreatorBlockReason.GameHasInstall"/>.</param>
/// <param name="App">The other app, for <see cref="CreatorBlockReason.GameManagedByOtherApp"/>.</param>
public sealed record CreatorBlock(CreatorBlockReason Reason, string? Path = null, Guid? InstallId = null, string? App = null);

/// <summary>The creator refused to compare or build before reading or writing anything.</summary>
public sealed class CreatorBlockedException : Exception
{
    /// <summary>Creates the exception for the reasons the creator refused.</summary>
    /// <param name="blocks">Every reason found, so the author can fix them in one go.</param>
    public CreatorBlockedException(IReadOnlyList<CreatorBlock> blocks)
        : base($"The creator cannot go on: {string.Join(", ", blocks)}.") =>
        Blocks = [.. blocks];

    public ValueList<CreatorBlock> Blocks { get; }

    /// <summary>Refuses a game that holds an install of this app or files another app manages.</summary>
    /// <param name="gameDirectory">Canonical, like the folders in install records.</param>
    /// <param name="installs">Every install the app keeps a record of.</param>
    /// <param name="fileSystem">Looks for other apps' folders in the game.</param>
    /// <param name="options">Names this copy of the app, whose own folders are not another app's.</param>
    internal static void ThrowIfManaged(string gameDirectory, IEnumerable<ExistingInstall> installs, IFileSystem fileSystem,
        CreatorOptions options)
    {
        var blocks = installs
            .Where(install => PathComparison.Overlaps(install.InstallDirectory, gameDirectory))
            .Select(install => new CreatorBlock(CreatorBlockReason.GameHasInstall, install.InstallDirectory, install.RecordId))
            .Concat(AppFolders.FindOthers(fileSystem, gameDirectory, options.InstanceName)
                .Select(other => new CreatorBlock(CreatorBlockReason.GameManagedByOtherApp, other.Directory, App: other.App)))
            .ToList();
        if (blocks.Count > 0)
        {
            throw new CreatorBlockedException(blocks);
        }
    }
}
