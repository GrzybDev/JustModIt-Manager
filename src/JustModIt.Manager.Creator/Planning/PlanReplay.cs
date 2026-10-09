using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Comparison;

namespace JustModIt.Manager.Creator.Planning;

/// <summary>
/// Plays the operations over the listed game folder without touching a file, the way the installer replays a manifest:
/// each sees the game as the ones before it left it, and one that cannot run is reported and not played.
/// </summary>
internal sealed class PlanReplay
{
    private readonly FolderComparison _comparison;
    private readonly GameIndex _game;

    // A path missing here is as the game has it.
    private readonly Dictionary<string, PathState> _overlay = new(PackagePath.Comparer);
    private readonly HashSet<string> _createdDirectories = new(PackagePath.Comparer);
    private readonly List<PlanFinding> _findings = [];
    private int _index;

    private PlanReplay(FolderComparison comparison)
    {
        _comparison = comparison;
        _game = comparison.Game;
    }

    private enum Origin
    {
        Absent,

        /// <summary>The game's own file, untouched so far.</summary>
        Game,

        /// <summary>An earlier operation put a file there: the author's, or a game file it renamed.</summary>
        Written,

        Deleted,

        /// <summary>An earlier operation renamed the file away.</summary>
        RenamedAway,
    }

    /// <summary>Replays the operations in order over the comparison's game listing.</summary>
    /// <param name="comparison">Gives the game listing and the author's files.</param>
    /// <param name="operations">In the order the package runs them.</param>
    /// <returns>The operations with the files they rely on, and what keeps them from running.</returns>
    public static (List<PlannedOperation> Operations, List<PlanFinding> Findings) Run(FolderComparison comparison,
        IReadOnlyList<PlannedOperation> operations)
    {
        var replay = new PlanReplay(comparison);
        if (operations.Count == 0)
        {
            replay._findings.Add(new PlanFinding(FindingKind.EmptyPackage));
        }

        var resolved = new List<PlannedOperation>(operations.Count);
        for (replay._index = 0; replay._index < operations.Count; replay._index++)
        {
            resolved.Add(replay.Play(operations[replay._index]));
        }

        return (resolved, replay._findings);
    }

    private PlannedOperation Play(PlannedOperation operation) => operation.Kind switch
    {
        OperationKind.Copy => Copy(operation),
        OperationKind.Patch => Patch(operation),
        OperationKind.Rename => Rename(operation),
        _ => Delete(operation),
    };

    private PlannedOperation Copy(PlannedOperation operation)
    {
        var source = SourceOf(operation);
        if (!IsValid(operation, operation.Path) || source is null)
        {
            return operation with { SourceFile = source };
        }

        var state = StateOf(operation.Path);
        if (state.Origin == Origin.Written)
        {
            Add(FindingKind.DuplicatePath, operation, operation.Path);
        }

        if (!CanHoldFile(operation, operation.Path))
        {
            return operation with { SourceFile = source };
        }

        // Content a rename moved elsewhere is kept, so only content overwritten or deleted in place is replaced.
        var replaces = state.Origin is Origin.Game or Origin.Deleted && _game.TryGetFile(operation.Path, out var game) ? game : null;
        Write(operation.Path, source);
        return operation with { SourceFile = source, Replaces = replaces };
    }

    private PlannedOperation Patch(PlannedOperation operation)
    {
        var source = SourceOf(operation);
        if (!IsValid(operation, operation.Path))
        {
            return operation with { SourceFile = source };
        }

        // Both are reported, so the author can fix them in one go.
        var original = HeldFile(operation, operation.Path);
        if (source is null || original is null)
        {
            return operation with { SourceFile = source };
        }

        Write(operation.Path, source);
        return operation with { SourceFile = source, Original = original };
    }

    private PlannedOperation Rename(PlannedOperation operation)
    {
        var from = operation.From ?? string.Empty;
        if (!(IsValid(operation, from) & IsValid(operation, operation.Path)))
        {
            return operation;
        }

        if (PackagePath.Comparer.Equals(from, operation.Path))
        {
            Add(FindingKind.ConflictingOperations, operation, from);
            return operation;
        }

        if (HeldFile(operation, from) is not { } moved || !CanHoldFile(operation, operation.Path))
        {
            return operation;
        }

        switch (StateOf(operation.Path).Origin)
        {
            case Origin.Game:
                Add(FindingKind.RenameOverwritesFile, operation, operation.Path);
                break;
            case Origin.Written:
                Add(FindingKind.DuplicatePath, operation, operation.Path);
                break;
        }

        Write(operation.Path, moved);
        _overlay[from] = new PathState(Origin.RenamedAway, null);
        return operation with { Original = moved };
    }

    private PlannedOperation Delete(PlannedOperation operation)
    {
        if (!IsValid(operation, operation.Path))
        {
            return operation;
        }

        if (_game.IsDirectory(operation.Path) || _createdDirectories.Contains(operation.Path))
        {
            Add(FindingKind.PathConflict, operation, operation.Path);
            return operation;
        }

        var state = StateOf(operation.Path);
        if (HeldFile(operation, operation.Path) is null)
        {
            return operation;
        }

        // Deleting what the package itself wrote undoes an earlier operation.
        if (state.Origin == Origin.Written)
        {
            Add(FindingKind.ConflictingOperations, operation, operation.Path);
        }

        _overlay[operation.Path] = new PathState(Origin.Deleted, null);
        return operation;
    }

    private PathState StateOf(string path) =>
        _overlay.TryGetValue(path, out var state) ? state
        : _game.TryGetFile(path, out var file) ? new PathState(Origin.Game, new FileReference(file.FullPath, file.Stamp))
        : new PathState(Origin.Absent, null);

    // The file at the path at this point; reports why there is none.
    private FileReference? HeldFile(PlannedOperation operation, string path)
    {
        var state = StateOf(path);
        switch (state.Origin)
        {
            case Origin.Absent:
                Add(FindingKind.GameFileMissing, operation, path);
                return null;
            case Origin.Deleted or Origin.RenamedAway:
                Add(FindingKind.FileRemovedEarlier, operation, path);
                return null;
            default:
                return state.Content;
        }
    }

    private FileReference? SourceOf(PlannedOperation operation)
    {
        var source = operation.Source ?? string.Empty;
        if (!IsValid(operation, source))
        {
            return null;
        }

        if (_comparison.FindAuthorFile(source) is { AuthorPath: { } authorPath, Author: { } stamp })
        {
            return new FileReference(_comparison.AuthorFullPath(authorPath), stamp);
        }

        Add(FindingKind.AuthorFileMissing, operation, source);
        return null;
    }

    private bool IsValid(PlannedOperation operation, string path)
    {
        switch (PackagePath.Check(path))
        {
            case PackagePathCheck.EscapesFolder:
                Add(FindingKind.PathEscapesFolder, operation, path);
                return false;
            case PackagePathCheck.Invalid:
                Add(FindingKind.InvalidPath, operation, path);
                return false;
        }

        // The installer refuses all of them, so a package holding one could not be installed anywhere.
        if (AppFolders.IsReservedTopFolder(PackagePath.FirstSegment(path), _game.HiddenFolderName) || AppFolders.WouldMark(path))
        {
            Add(FindingKind.ReservedPath, operation, path);
            return false;
        }

        return true;
    }

    // A folder cannot become a file, and a file cannot hold other files.
    private bool CanHoldFile(PlannedOperation operation, string path)
    {
        var blocked = _game.IsDirectory(path) || _createdDirectories.Contains(path);
        for (var directory = GameIndex.DirectoryOf(path); !blocked && directory.Length > 0; directory = GameIndex.DirectoryOf(directory))
        {
            blocked = StateOf(directory).Origin is Origin.Game or Origin.Written;
        }

        if (blocked)
        {
            Add(FindingKind.PathConflict, operation, path);
        }

        return !blocked;
    }

    private void Write(string path, FileReference? content)
    {
        _overlay[path] = new PathState(Origin.Written, content);
        var directory = GameIndex.DirectoryOf(path);
        while (directory.Length > 0 && !_game.IsDirectory(directory) && _createdDirectories.Add(directory))
        {
            directory = GameIndex.DirectoryOf(directory);
        }
    }

    private void Add(FindingKind kind, PlannedOperation operation, string path) =>
        _findings.Add(new PlanFinding(kind, path) { OperationIndex = _index, StepIndex = operation.StepIndex });

    private readonly record struct PathState(Origin Origin, FileReference? Content);
}
