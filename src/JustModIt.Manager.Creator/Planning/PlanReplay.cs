using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Comparison;

namespace JustModIt.Manager.Creator.Planning;

/// <summary>A file the author declared as one the package's software writes while the game runs, before the plan checks it.</summary>
/// <param name="Path">The game-relative path, spelt as the game spells it where it can.</param>
/// <param name="Kind">Whether the game has the file or the software creates it.</param>
/// <param name="FromChoice">A file of the comparison declared with <see cref="FileChoice.Runtime"/>.</param>
internal sealed record RuntimeDeclaration(string Path, RuntimeFileKind Kind, bool FromChoice);

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

    /// <summary>Replays the operations in order over the comparison's game listing, then checks the declared files on the result.</summary>
    /// <param name="comparison">Gives the game listing and the author's files.</param>
    /// <param name="operations">In the order the package runs them.</param>
    /// <param name="declarations">The files the package's software writes while the game runs; null for none.</param>
    /// <param name="keptPaths">Game paths that an undone test left as the software wrote them; null for none.</param>
    /// <returns>The operations with the files they rely on, the declared files, and what keeps them from running.</returns>
    public static (List<PlannedOperation> Operations, List<PlanFinding> Findings, List<PlannedRuntimeFile> RuntimeFiles) Run(
        FolderComparison comparison, IReadOnlyList<PlannedOperation> operations, IReadOnlyList<RuntimeDeclaration>? declarations = null,
        IReadOnlySet<string>? keptPaths = null)
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

        var runtime = replay.Declare(declarations ?? [], resolved, keptPaths ?? new HashSet<string>());
        return (resolved, replay._findings, runtime);
    }

    // On the game as the whole package leaves it: the software runs only once everything is installed.
    private List<PlannedRuntimeFile> Declare(IReadOnlyList<RuntimeDeclaration> declarations, List<PlannedOperation> operations,
        IReadOnlySet<string> keptPaths)
    {
        if (declarations.Count > RuntimeFile.MaxPerPackage)
        {
            _findings.Add(new PlanFinding(FindingKind.TooManyRuntimePaths));
        }

        var declared = new HashSet<string>(PackagePath.Comparer);
        var planned = new List<PlannedRuntimeFile>(declarations.Count);
        for (var i = 0; i < declarations.Count; i++)
        {
            var (path, kind, fromChoice) = declarations[i];
            var game = _game.TryGetFile(path, out var file) ? new FileReference(file.FullPath, file.Stamp) : null;
            var written = operations.Any(operation =>
                PackagePath.Comparer.Equals(operation.Path, path) || PackagePath.Comparer.Equals(operation.From, path));
            planned.Add(new PlannedRuntimeFile(path, kind, game, written, fromChoice));
            if (RuntimePathProblem(path, _game.HiddenFolderName) is { } problem)
            {
                AddRuntime(problem, i, path);
                continue;
            }

            if (!declared.Add(path))
            {
                AddRuntime(FindingKind.DuplicatePath, i, path);
                continue;
            }

            if (IsBlocked(path))
            {
                AddRuntime(FindingKind.PathConflict, i, path);
                continue;
            }

            if (operations.Any(operation => operation.Kind == OperationKind.Patch && PackagePath.Comparer.Equals(operation.Path, path)))
            {
                AddRuntime(FindingKind.RuntimeFilePatched, i, path);
            }

            switch (kind)
            {
                case RuntimeFileKind.ChangesGameFile when game is null && StateOf(path).Origin != Origin.Written:
                    AddRuntime(FindingKind.GameFileMissing, i, path, NearestTo(path));
                    break;
                case RuntimeFileKind.ChangesGameFile when keptPaths.Contains(path):
                    AddRuntime(FindingKind.RuntimeFileKept, i, path);
                    break;
                case RuntimeFileKind.CreatesFile when game is not null && !written:
                    AddRuntime(FindingKind.RuntimeFileInGame, i, path);
                    break;
            }
        }

        return planned;
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
                Add(FindingKind.GameFileMissing, operation, path, NearestTo(path));
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
        if (PathProblem(path) is { } problem)
        {
            Add(problem, operation, path);
            return false;
        }

        return true;
    }

    /// <summary>What keeps a path from being declared as a file the package's software writes; null when nothing does.</summary>
    /// <param name="path">The game-relative path.</param>
    /// <param name="hiddenFolderName">The name of the app's own folder in the game.</param>
    internal static FindingKind? RuntimePathProblem(string path, string hiddenFolderName) =>
        // A file the software has not created yet keeps the name it is given, which a short name would never meet.
        PathProblem(path, hiddenFolderName) ?? (PackagePath.HasShortName(path) ? FindingKind.InvalidPath : null);

    private FindingKind? PathProblem(string path) => PathProblem(path, _game.HiddenFolderName);

    private static FindingKind? PathProblem(string path, string hiddenFolderName) => PackagePath.Check(path) switch
    {
        PackagePathCheck.EscapesFolder => FindingKind.PathEscapesFolder,
        PackagePathCheck.Invalid => FindingKind.InvalidPath,
        // The installer refuses all of them, so a package holding one could not be installed anywhere.
        _ when AppFolders.IsReservedTopFolder(PackagePath.FirstSegment(path), hiddenFolderName) || AppFolders.WouldMark(path) =>
            FindingKind.ReservedPath,
        _ => null,
    };

    private bool CanHoldFile(PlannedOperation operation, string path)
    {
        var blocked = IsBlocked(path);
        if (blocked)
        {
            Add(FindingKind.PathConflict, operation, path);
        }

        return !blocked;
    }

    // A folder cannot become a file, and a file cannot hold other files.
    private bool IsBlocked(string path)
    {
        var blocked = _game.IsDirectory(path) || _createdDirectories.Contains(path);
        for (var directory = GameIndex.DirectoryOf(path); !blocked && directory.Length > 0; directory = GameIndex.DirectoryOf(directory))
        {
            blocked = StateOf(directory).Origin is Origin.Game or Origin.Written;
        }

        return blocked;
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

    // A slip in a name is likelier than one in a folder, so a near name in the path's own folder goes first. An operation
    // can rely only on the files there at its point of the package, which earlier ones may have written or removed.
    private string? NearestTo(string path)
    {
        var folder = GameIndex.DirectoryOf(path);
        var name = GameIndex.NameOf(path);
        var written = _overlay.Where(entry => entry.Value.Origin == Origin.Written).Select(entry => entry.Key).ToList();
        var nearName = _game.FilesIn(folder).Select(file => file.Path)
            .Concat(written.Where(other => PackagePath.Comparer.Equals(GameIndex.DirectoryOf(other), folder)));
        var sameName = _game.FoldersWith(name).Select(other => _game.Spell(other.Length == 0 ? name : other + PackagePath.Separator + name))
            .Concat(written.Where(other => PackagePath.Comparer.Equals(GameIndex.NameOf(other), name)));
        return Nearest(nearName, GameIndex.NameOf, name, Math.Max(1, name.Length / 4))
            ?? Nearest(sameName, GameIndex.DirectoryOf, folder, int.MaxValue);
    }

    private string? Nearest(IEnumerable<string> candidates, Func<string, string> part, string typed, int limit) =>
        candidates.Where(candidate => StateOf(candidate).Origin is Origin.Game or Origin.Written)
            .Select(candidate => (Path: candidate, Distance: Distance(part(candidate), typed)))
            .Where(candidate => candidate.Distance <= limit)
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Path, PackagePath.Comparer)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();

    // Letter case does not count, as game paths ignore it, and two swapped letters, a frequent slip, count as one edit.
    private static int Distance(string first, string second)
    {
        var a = first.ToUpperInvariant();
        var b = second.ToUpperInvariant();
        var edits = new int[a.Length + 1][];
        for (var i = 0; i <= a.Length; i++)
        {
            edits[i] = new int[b.Length + 1];
            edits[i][0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                edits[i][j] = i == 0 ? j
                    : Math.Min(Math.Min(edits[i - 1][j] + 1, edits[i][j - 1] + 1), edits[i - 1][j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    edits[i][j] = Math.Min(edits[i][j], edits[i - 2][j - 2] + 1);
                }
            }
        }

        return edits[a.Length][b.Length];
    }

    private void Add(FindingKind kind, PlannedOperation operation, string path, string? suggested = null) =>
        _findings.Add(new PlanFinding(kind, path) { OperationIndex = _index, StepIndex = operation.StepIndex, SuggestedPath = suggested });

    private void AddRuntime(FindingKind kind, int index, string path, string? suggested = null) =>
        _findings.Add(new PlanFinding(kind, path) { RuntimeIndex = index, SuggestedPath = suggested });

    private readonly record struct PathState(Origin Origin, FileReference? Content);
}
