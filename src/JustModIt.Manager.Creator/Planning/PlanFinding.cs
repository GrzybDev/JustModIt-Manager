namespace JustModIt.Manager.Creator.Planning;

public enum FindingKind
{
    /// <summary>The package would change nothing.</summary>
    EmptyPackage,

    InvalidPath,
    PathEscapesFolder,

    /// <summary>The path names the app's own folder in the game.</summary>
    ReservedPath,

    /// <summary>The package writes the path twice, so the first write would be lost.</summary>
    DuplicatePath,

    /// <summary>Operations undo each other, e.g. deleting a file the package wrote, or renaming a file onto itself.</summary>
    ConflictingOperations,

    /// <summary>A folder stands where the package needs a file, or a file where it needs a folder.</summary>
    PathConflict,

    /// <summary>The author's file is not in the author's folder, e.g. it was removed after the comparison.</summary>
    AuthorFileMissing,

    /// <summary>The game has no file to patch, rename or delete there.</summary>
    GameFileMissing,

    /// <summary>An earlier operation already deleted or renamed away the file.</summary>
    FileRemovedEarlier,

    /// <summary>A rename replaces a game file that is there.</summary>
    RenameOverwritesFile,

    /// <summary>
    /// A game file is renamed although the author neither removed it nor puts a file in its place, so the game may miss
    /// it. Needs an acknowledgement.
    /// </summary>
    RenamedFileNotReplaced,

    /// <summary>More files than the app installs from one package.</summary>
    TooManyFiles,

    /// <summary>More bytes than the app installs from one package.</summary>
    PackageTooLarge,

    /// <summary>A manifest package needs a title.</summary>
    MissingTitle,

    /// <summary>A manifest package needs a version.</summary>
    MissingVersion,
}

public enum FindingSeverity
{
    Warning,
    Error,
}

/// <summary>Something the author has to fix, accept or know before building.</summary>
/// <param name="Kind">What is wrong or worth knowing.</param>
/// <param name="Path">The game path, or the author's path for a missing source.</param>
public sealed record PlanFinding(FindingKind Kind, string? Path = null)
{
    public FindingSeverity Severity => Kind is FindingKind.RenameOverwritesFile or FindingKind.RenamedFileNotReplaced
        ? FindingSeverity.Warning
        : FindingSeverity.Error;

    /// <summary>A second path the finding is about, e.g. the game file a rename takes.</summary>
    public string? RelatedPath { get; init; }

    /// <summary>The operation of the plan it is about.</summary>
    public int? OperationIndex { get; init; }

    /// <summary>The author's step it is about; see <see cref="PlannedOperation.StepIndex"/>.</summary>
    public int? StepIndex { get; init; }

    /// <summary>When set, the warning blocks the build until the author acknowledges this key.</summary>
    public string? AcknowledgementKey { get; init; }

    public bool IsAcknowledged { get; init; }

    public bool BlocksBuild => Severity == FindingSeverity.Error || (AcknowledgementKey is not null && !IsAcknowledged);
}
