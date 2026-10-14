using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Packages;

/// <summary>A game file that software the package carries, such as a proxy library, rewrites or creates while the game runs.</summary>
/// <param name="Path">The game-relative file, under the rules of operation paths.</param>
/// <param name="Originals">
/// The content its store ships at the path, in any build or store version the package supports; null when the package
/// names none, as for a file the software creates.
/// </param>
public sealed record RuntimeFile(string Path, IReadOnlyList<FileContent>? Originals = null)
{
    public const int MaxPerPackage = 64;

    public const int MaxOriginals = 8;

    /// <summary>Whether the content is one the package names as its store's.</summary>
    /// <param name="size">The content's size in bytes.</param>
    /// <param name="sha256">The content's SHA-256 in hex.</param>
    public bool IsOriginal(long size, string sha256) =>
        Originals?.Any(original => original.Size == size && FileHash.Matches(original.Sha256, sha256)) == true;
}

/// <summary>A file's content as its size and hash tell it.</summary>
/// <param name="Size">The size in bytes.</param>
/// <param name="Sha256">The SHA-256 in lower-case hex.</param>
public sealed record FileContent(long Size, string Sha256);
