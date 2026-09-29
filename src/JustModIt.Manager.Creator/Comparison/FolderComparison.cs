using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Creator.Comparison;

public enum FileDifference
{
    /// <summary>The game has no such file.</summary>
    New,

    /// <summary>The author's version of a game file.</summary>
    Changed,

    /// <summary>The same content as the game's file.</summary>
    Identical,

    /// <summary>
    /// A game file that the author's folder lacks although it holds most of that folder's files, so the author has
    /// probably removed it.
    /// </summary>
    Missing,
}

public sealed record ComparedFile
{
    /// <summary>Game-relative, spelled like the game wherever the game has the file or its folders.</summary>
    public required string Path { get; init; }

    public required FileDifference Difference { get; init; }

    /// <summary>The author's file relative to the author's folder, as the author spells it; null when missing.</summary>
    public string? AuthorPath { get; init; }

    /// <summary>The author's file as listed; null when missing.</summary>
    public FileStamp? Author { get; init; }

    /// <summary>The game's file at <see cref="Path"/>; null when the game has none.</summary>
    public GameFile? Game { get; init; }

    /// <summary>
    /// A game file with the same content as the author's file, which a package must not carry; searched only for new
    /// files and for files whose size differs from the game's, from 1 KiB up.
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

/// <summary>The folders a comparison sets against each other.</summary>
/// <param name="GameDirectory">The game folder to compare against.</param>
/// <param name="AuthorDirectory">The author's folder, laid out like the game.</param>
/// <param name="Installs">Every install the app keeps a record of; one on the game refuses the comparison.</param>
public sealed record ComparisonRequest(string GameDirectory, string AuthorDirectory, IReadOnlyList<ExistingInstall> Installs);

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
    /// <param name="authorPath">Relative to the author's folder, in any letter case.</param>
    public ComparedFile? FindAuthorFile(string authorPath) => _byAuthorPath.GetValueOrDefault(authorPath);
}
