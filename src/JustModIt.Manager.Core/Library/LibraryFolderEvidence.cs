namespace JustModIt.Manager.Core.Library;

/// <summary>Why a folder the player picked cannot be added as a library.</summary>
public enum LibraryFolderRejection
{
    /// <summary>No store's own records of games are in or above the folder.</summary>
    NoEvidence,

    /// <summary>A store's game that its store does not list, so where it came from cannot be confirmed.</summary>
    GameOutsideLibrary,

    /// <summary>The folder holds only additional content (DLC), not a game.</summary>
    DlcOnly,

    AlreadyAdded,

    /// <summary>The store already reports this library or game.</summary>
    AlreadyDetected,

    /// <summary>The folder does not exist or could not be read in time.</summary>
    Unreachable,
}

/// <summary>What one store found in a folder the player picked.</summary>
/// <param name="Folder">The canonical folder to add, which may be an ancestor of the one picked.</param>
/// <param name="Rejection">Why the folder cannot be added; null when it can.</param>
public sealed record LibraryFolderEvidence(string? Folder, LibraryFolderRejection? Rejection)
{
    public static LibraryFolderEvidence None { get; } = Rejected(LibraryFolderRejection.NoEvidence);

    public bool IsAccepted => Folder is not null;

    /// <summary>Creates evidence that <paramref name="folder"/> can be added.</summary>
    /// <param name="folder">The canonical folder to add.</param>
    public static LibraryFolderEvidence Accepted(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return new(folder, null);
    }

    /// <summary>Creates evidence that the folder cannot be added.</summary>
    /// <param name="rejection">Why it cannot.</param>
    public static LibraryFolderEvidence Rejected(LibraryFolderRejection rejection) => new(null, rejection);
}
