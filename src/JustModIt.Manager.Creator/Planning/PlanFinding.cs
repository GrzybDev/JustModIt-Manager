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
    /// it; needs an acknowledgement.
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

    /// <summary>The package touches a library of a store or of its copy protection.</summary>
    StoreLibrary,

    /// <summary>The package carries a file of a crack or a store emulator.</summary>
    CircumventionFile,

    /// <summary>The package carries a copy of a game file; <see cref="PlanFinding.RelatedPath"/> names it.</summary>
    CopyOfGameFile,

    /// <summary>The package carries a whole game executable, which may only change through a patch.</summary>
    WholeGameExecutable,

    /// <summary>The package carries a whole file in place of a game file. Needs an acknowledgement.</summary>
    WholeGameFile,

    /// <summary>
    /// Like <see cref="WholeGameFile"/>, for a game archive or a large file, which carries a large share of the game's
    /// assets. Needs an acknowledgement.
    /// </summary>
    WholeGameArchive,

    /// <summary>
    /// A patch is almost as large as its file, so it carries most of the file, e.g. of an encrypted or recompressed
    /// archive. Only a build finds it; needs an acknowledgement.
    /// </summary>
    LargePatch,

    /// <summary>
    /// The package carries a third-party tool, e.g. a mod loader. Needs the tool's licence and the address of its source
    /// code (<see cref="ThirdPartyNotice"/>).
    /// </summary>
    ThirdPartyTool,
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
        or FindingKind.WholeGameFile or FindingKind.WholeGameArchive or FindingKind.LargePatch or FindingKind.ThirdPartyTool
        ? FindingSeverity.Warning
        : FindingSeverity.Error;

    /// <summary>A second path the finding is about, e.g. the game file a rename takes.</summary>
    public string? RelatedPath { get; init; }

    /// <summary>The size of the file the finding is about, e.g. of a whole game file or of a patch.</summary>
    public long? Size { get; init; }

    /// <summary>The third-party tool (<see cref="Rules.ThirdPartyTool.Id"/>).</summary>
    public string? ToolId { get; init; }

    /// <summary>The operation of the plan it is about.</summary>
    public int? OperationIndex { get; init; }

    /// <summary>The author's step it is about; see <see cref="PlannedOperation.StepIndex"/>.</summary>
    public int? StepIndex { get; init; }

    /// <summary>When set, the warning blocks the build until the author acknowledges this key.</summary>
    public string? AcknowledgementKey { get; init; }

    public bool IsAcknowledged { get; init; }

    public bool BlocksBuild => Severity == FindingSeverity.Error || (AcknowledgementKey is not null && !IsAcknowledged);
}
