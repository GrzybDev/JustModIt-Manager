using JustModIt.Manager.Creator.Comparison;

namespace JustModIt.Manager.Creator.Planning;

public enum OperationKind
{
    Copy,
    Patch,
    Rename,
    Delete,
}

/// <summary>Why the plan holds an operation.</summary>
public enum OperationReason
{
    /// <summary>The game has no such file.</summary>
    NewFile,

    /// <summary>The author changed a game file, so the package carries only the difference.</summary>
    ChangedFile,

    /// <summary>The author chose to carry a changed game file whole.</summary>
    ReplacedFile,

    /// <summary>A changed game file is carried whole because the package renames its original away first.</summary>
    OriginalRenamedAway,

    /// <summary>A new file holds a game file's content, so the package renames that game file instead of copying it.</summary>
    RenamedGameFile,

    /// <summary>The author removed the game file and confirmed that the package deletes it.</summary>
    RemovedFile,

    /// <summary>A step the author added in simple mode.</summary>
    AddedStep,

    /// <summary>A step of an advanced project.</summary>
    Step,
}

/// <summary>A file on disk as the plan relies on it.</summary>
/// <param name="FullPath">The file on disk.</param>
/// <param name="Stamp">The file as the comparison listed it.</param>
public sealed record FileReference(string FullPath, FileStamp Stamp);

/// <summary>A game file the package's software writes while the game runs, as the plan declares it in the manifest.</summary>
/// <param name="Path">The game-relative path, spelt as the game spells it.</param>
/// <param name="Kind">Whether the game has the file or the software creates it.</param>
/// <param name="GameFile">The game's own file at the path, whose content the manifest names; null when the game has none.</param>
/// <param name="WrittenByPackage">An operation of the package copies, renames or deletes the path too.</param>
/// <param name="FromChoice">A file of the comparison declared with <see cref="FileChoice.Runtime"/>.</param>
public sealed record PlannedRuntimeFile(string Path, RuntimeFileKind Kind, FileReference? GameFile, bool WrittenByPackage, bool FromChoice);

/// <summary>One change the package makes, with game-relative, '/'-separated paths.</summary>
public sealed record PlannedOperation
{
    public required OperationKind Kind { get; init; }

    /// <summary>Where a copy, patch or rename writes, or the file a delete removes.</summary>
    public required string Path { get; init; }

    /// <summary>The file a rename takes.</summary>
    public string? From { get; init; }

    /// <summary>The author's file a copy or patch puts in place, relative to the author's folder.</summary>
    public string? Source { get; init; }

    public required OperationReason Reason { get; init; }

    /// <summary>The author's step it comes from: an extra step in simple mode, a step in advanced mode.</summary>
    public int? StepIndex { get; init; }

    /// <summary>The author's file behind <see cref="Source"/>, once the plan has checked that it is there.</summary>
    public FileReference? SourceFile { get; init; }

    /// <summary>
    /// The file a patch is made against, or a rename takes: the game's file at that point of the package, or an
    /// author's file that an earlier step put there.
    /// </summary>
    public FileReference? Original { get; init; }

    /// <summary>The game file whose content a copy overwrites; null when the path is new or a rename freed it.</summary>
    public GameFile? Replaces { get; init; }

    public bool HasPayload => Kind is OperationKind.Copy or OperationKind.Patch;
}
