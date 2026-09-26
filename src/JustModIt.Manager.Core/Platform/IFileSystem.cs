namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// Read access to files, so the code that inspects stores and games can be tested without a disk, throwing for a missing
/// path as <see cref="System.IO"/> does and blocking on slow or network drives, so it stays off the UI thread.
/// </summary>
public interface IFileSystem
{
    /// <summary>Whether a file exists at the path.</summary>
    /// <param name="path">The file's path.</param>
    bool FileExists(string path);

    /// <summary>Whether a directory exists at the path.</summary>
    /// <param name="path">The directory's path.</param>
    bool DirectoryExists(string path);

    /// <summary>What is at <paramref name="path"/>, describing a link itself rather than its target; null when nothing is.</summary>
    /// <param name="path">The file's or directory's path.</param>
    FileEntry? GetEntry(string path);

    /// <summary>Lists a directory lazily; recursion never enters links, which can form cycles or lead outside it.</summary>
    /// <param name="directory">The directory to list.</param>
    /// <param name="searchPattern">A pattern with <c>*</c> and <c>?</c> that names must match.</param>
    /// <param name="options">Recursion, casing and the attributes to skip.</param>
    IEnumerable<FileEntry> Enumerate(string directory, string searchPattern, EnumerationOptions options);

    /// <summary>Opens a file for asynchronous reading while others hold it with <paramref name="share"/> access.</summary>
    /// <param name="path">The file's path.</param>
    /// <param name="share">The access other processes may keep or take meanwhile.</param>
    Stream OpenRead(string path, FileShare share);

    /// <summary>Reads a whole file even while its owner has it open for writing or replaces it.</summary>
    /// <param name="path">The file's path.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>Reads a whole file as UTF-8, or in the encoding its byte order mark names, which is not returned.</summary>
    /// <param name="path">The file's path.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken);

    /// <summary>The path with links, junctions and short names resolved (see <see cref="IO.PathCanonicalizer"/>).</summary>
    /// <param name="path">A path whose tail may not exist yet.</param>
    string GetCanonicalPath(string path);
}
