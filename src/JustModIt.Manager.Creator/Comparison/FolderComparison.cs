using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Creator.Comparison;

public enum FileDifference
{
    /// <summary>The game has no such file.</summary>
    New,

    /// <summary>The author's version of a game file.</summary>
    Changed,

    /// <summary>The same content as the game's file, or as another version's at this path, which is no work of the author's either.</summary>
    Identical,

    /// <summary>
    /// A game file that the author's folder lacks although it holds most of that folder's files, so the author has
    /// probably removed it.
    /// </summary>
    Missing,
}

public sealed record ComparedFile
{
    /// <summary>In the project's layout, spelled like the game wherever the game has the file or its folders.</summary>
    public required string Path { get; init; }

    public required FileDifference Difference { get; init; }

    /// <summary>The author's file relative to the author's folder, as the author spells it; null when missing.</summary>
    public string? AuthorPath { get; init; }

    /// <summary>The author's file as listed; null when missing.</summary>
    public FileStamp? Author { get; init; }

    /// <summary>The game's file at <see cref="Path"/>; null when the game has none.</summary>
    public GameFile? Game { get; init; }

    /// <summary>
    /// A game file, of this version or another, with the same content as the author's file, which a package must not carry;
    /// searched only for new files and for files whose size differs from the game's, from 1 KiB up.
    /// </summary>
    public string? CopyOf { get; init; }

    /// <summary>
    /// For a new file: the game file it most likely was before the author renamed it, e.g. an original library moved
    /// aside for a proxy; each game file is proposed for one new file at most.
    /// </summary>
    public string? RenameFrom { get; init; }

    /// <summary>
    /// For a new file at the top of the author's folder: the one game folder that holds a file of this name, where the
    /// author most likely meant to put it, since the game will not read it where it is.
    /// </summary>
    public string? ExpectedFolder { get; init; }
}

/// <summary>What a comparison sets against each other.</summary>
/// <param name="Game">The game as listed, in the layout the comparison's paths take.</param>
/// <param name="AuthorDirectory">The author's folder, laid out like the game or like the project.</param>
/// <param name="SkippedFolders">Folders within it that hold other packages' files, which the comparison leaves out.</param>
public sealed record ComparisonRequest(GameIndex Game, string AuthorDirectory, IReadOnlyList<string> SkippedFolders)
{
    /// <summary>
    /// The other versions of the game that share the author's folder, in the project's layout. The folder was made on one of
    /// them, so what it holds of their files is no work of the author's on this version either.
    /// </summary>
    public IReadOnlyList<GameIndex> OtherVersions { get; init; } = [];
}

public enum ComparisonStage
{
    ListingFiles,
    Comparing,
    FindingCopies,
}

/// <summary>How far a comparison has got.</summary>
/// <param name="Stage">What the comparison is doing.</param>
/// <param name="CurrentFile">The game path of the author's file being worked on; null while listing and at the end.</param>
/// <param name="FilesDone">The author's files compared so far.</param>
/// <param name="FileCount">The author's files in all.</param>
/// <param name="BytesDone">Bytes hashed so far.</param>
/// <param name="BytesTotal">Bytes to hash in all; 0 when nothing needs hashing.</param>
public readonly record struct CompareProgress(ComparisonStage Stage, string? CurrentFile, int FilesDone, int FileCount,
    long BytesDone, long BytesTotal);

/// <summary>The author's files set against the game, and the game as it was listed for that.</summary>
public sealed class FolderComparison
{
    private readonly Dictionary<string, ComparedFile> _byAuthorPath;
    private readonly Dictionary<string, ComparedFile> _byPath;

    /// <summary>Holds the result of a comparison.</summary>
    /// <param name="gameDirectory">The game folder, canonical.</param>
    /// <param name="authorDirectory">The author's folder, canonical.</param>
    /// <param name="files">The compared files, by path.</param>
    /// <param name="game">The game listing the comparison used.</param>
    internal FolderComparison(string gameDirectory, string authorDirectory, ValueList<ComparedFile> files, GameIndex game)
    {
        GameDirectory = gameDirectory;
        AuthorDirectory = authorDirectory;
        Files = files;
        Game = game;
        _byAuthorPath = files.Where(file => file.AuthorPath is not null)
            .DistinctBy(file => file.AuthorPath, PackagePath.Comparer)
            .ToDictionary(file => file.AuthorPath!, PackagePath.Comparer);
        _byPath = files.Where(file => file.AuthorPath is not null)
            .DistinctBy(file => file.Path, PackagePath.Comparer)
            .ToDictionary(file => file.Path, PackagePath.Comparer);
    }

    /// <summary>Canonical.</summary>
    public string GameDirectory { get; }

    /// <summary>Canonical.</summary>
    public string AuthorDirectory { get; }

    /// <summary>The author's files and the game files the author probably removed, by path.</summary>
    public ValueList<ComparedFile> Files { get; }

    public GameIndex Game { get; }

    /// <summary>The full path of an author's file.</summary>
    /// <param name="authorPath">Relative to the author's folder.</param>
    public string AuthorFullPath(string authorPath) => PackagePath.ToFullPath(AuthorDirectory, authorPath);

    /// <summary>The compared file of an author's file; null when the comparison has none.</summary>
    /// <param name="path">Relative to the author's folder or else a project path, in any letter case.</param>
    public ComparedFile? FindAuthorFile(string path) => _byAuthorPath.GetValueOrDefault(path) ?? _byPath.GetValueOrDefault(path);
}
