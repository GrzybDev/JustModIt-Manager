namespace JustModIt.Manager.Creator.Planning;

public enum FindingKind
{
    /// <summary>The package would change nothing.</summary>
    EmptyPackage,

    InvalidPath,
    PathEscapesFolder,

    /// <summary>
    /// The path names the app's own folder in the game or a store's, or would pass a hidden folder off as an app's.
    /// </summary>
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

    /// <summary>The package carries a whole file in place of a game file; needs an acknowledgement.</summary>
    WholeGameFile,

    /// <summary>
    /// Like <see cref="WholeGameFile"/>, for a game archive or a large file, which carries a large share of the game's
    /// assets; needs an acknowledgement.
    /// </summary>
    WholeGameArchive,

    /// <summary>
    /// A patch is almost as large as its file, so it carries most of the file, e.g. of an encrypted or recompressed
    /// archive; only a build finds it, and it needs an acknowledgement.
    /// </summary>
    LargePatch,

    /// <summary>
    /// The package carries a third-party tool, e.g. a mod loader, and needs the tool's licence and the address of its
    /// source code (<see cref="ToolNotice"/>) for the package's manifest.
    /// </summary>
    ThirdPartyTool,

    /// <summary>
    /// The package adds a library that no rule names, which is the author's own code unless the author says it is another
    /// author's tool (<see cref="LibraryNotice"/>); a tool needs its licence and source, which the author's own code may
    /// carry too.
    /// </summary>
    AddedLibrary,

    /// <summary>More tools and libraries with a licence and source than a manifest names.</summary>
    TooManyThirdPartyNotices,

    /// <summary>The built package is larger than the site takes as a release file.</summary>
    LargerThanSiteTakes,

    /// <summary>The site takes no files of the package's extension, or none that install through the app.</summary>
    FormatNotOnSite,

    /// <summary>
    /// The game already has a file the package's software creates, so it probably ran the software before, and the file
    /// would be taken for the game's own.
    /// </summary>
    RuntimeFileInGame,

    /// <summary>
    /// Undoing a test left a game file the package's software changes as the software wrote it, so the game no longer has
    /// the file its store ships.
    /// </summary>
    RuntimeFileKept,

    /// <summary>An operation patches a file the software rewrites while the game runs, which a patch cannot rely on.</summary>
    RuntimeFilePatched,

    /// <summary>More files changed while the game runs than a manifest names.</summary>
    TooManyRuntimePaths,

    /// <summary>Files are declared as changed while the game runs, yet the package carries no program that could change them.</summary>
    RuntimeWithoutCode,

    /// <summary>A program changes while the game runs, which the app's checks then leave out; needs an acknowledgement.</summary>
    RuntimeCode,

    /// <summary>A game file that may hold the player's settings would come back as it was; needs an acknowledgement.</summary>
    RuntimeSettings,

    /// <summary>A save of the game would come back as it was, losing the player's progress.</summary>
    RuntimeSave,
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
        or FindingKind.AddedLibrary or FindingKind.LargerThanSiteTakes or FindingKind.FormatNotOnSite or FindingKind.RuntimeWithoutCode
        or FindingKind.RuntimeCode or FindingKind.RuntimeSettings
        ? FindingSeverity.Warning
        : FindingSeverity.Error;

    /// <summary>A second path the finding is about, e.g. the game file a rename takes.</summary>
    public string? RelatedPath { get; init; }

    /// <summary>
    /// For a <see cref="FindingKind.GameFileMissing"/>, the file the path most likely means, as the game holds it at that
    /// point of the package; null when none is near.
    /// </summary>
    public string? SuggestedPath { get; init; }

    /// <summary>The size of the file the finding is about, e.g. of a whole game file, a patch or the package.</summary>
    public long? Size { get; init; }

    /// <summary>The third-party tool (<see cref="Rules.ThirdPartyTool.Id"/>).</summary>
    public string? ToolId { get; init; }

    /// <summary>The operation of the plan it is about.</summary>
    public int? OperationIndex { get; init; }

    /// <summary>The author's step it is about; see <see cref="PlannedOperation.StepIndex"/>.</summary>
    public int? StepIndex { get; init; }

    /// <summary>The file changed while the game runs it is about, by its place in <see cref="PackagePlan.RuntimeFiles"/>.</summary>
    public int? RuntimeIndex { get; init; }

    /// <summary>When set, the warning blocks the build until the author acknowledges this key.</summary>
    public string? AcknowledgementKey { get; init; }

    public bool IsAcknowledged { get; init; }

    public bool BlocksBuild => Severity == FindingSeverity.Error || (AcknowledgementKey is not null && !IsAcknowledged);
}
