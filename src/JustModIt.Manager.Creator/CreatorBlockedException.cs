using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;

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
    /// A package or a test install is on the game. Its files would be compared or patched as if they were the game's,
    /// and every player would get a package that does not fit their game.
    /// </summary>
    GameHasInstall,
}

/// <param name="Reason">Why the creator refused.</param>
/// <param name="Path">The folder the reason is about.</param>
/// <param name="InstallId">The install on the game, for <see cref="CreatorBlockReason.GameHasInstall"/>.</param>
public sealed record CreatorBlock(CreatorBlockReason Reason, string? Path = null, Guid? InstallId = null);

/// <summary>The creator refused to compare or build before reading or writing anything.</summary>
public sealed class CreatorBlockedException : Exception
{
    /// <summary>Creates the exception for the reasons the creator refused.</summary>
    /// <param name="blocks">Every reason found, so the author can fix them in one go.</param>
    public CreatorBlockedException(IReadOnlyList<CreatorBlock> blocks)
        : base($"The creator cannot go on: {string.Join(", ", blocks ?? [])}.") =>
        Blocks = [.. blocks ?? []];

    /// <summary>Creates the exception without a reason.</summary>
    public CreatorBlockedException()
        : this([])
    {
    }

    /// <summary>Creates the exception with a message for logs.</summary>
    /// <param name="message">Meant for logs, not for users.</param>
    public CreatorBlockedException(string message)
        : this(message, null!)
    {
    }

    /// <summary>Creates the exception with a message for logs and the error behind it.</summary>
    /// <param name="message">Meant for logs, not for users.</param>
    /// <param name="innerException">The error behind the refusal.</param>
    public CreatorBlockedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ValueList<CreatorBlock> Blocks { get; }

    /// <param name="gameDirectory">Canonical, like the folders in install records.</param>
    /// <param name="installs">The installs the app keeps records of.</param>
    internal static void ThrowIfInstalled(string gameDirectory, IEnumerable<ExistingInstall> installs)
    {
        var blocks = installs
            .Where(install => PathComparison.Overlaps(install.InstallDirectory, gameDirectory))
            .Select(install => new CreatorBlock(CreatorBlockReason.GameHasInstall, install.InstallDirectory, install.RecordId))
            .ToList();
        if (blocks.Count > 0)
        {
            throw new CreatorBlockedException(blocks);
        }
    }
}
