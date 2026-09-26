using System.IO.Enumeration;
using System.Text;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Platform;

public sealed class PhysicalFileSystem : IFileSystem
{
    public static PhysicalFileSystem Instance { get; } = new();

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public FileEntry? GetEntry(string path)
    {
        var info = new FileInfo(PathComparison.Normalize(path));
        var attributes = info.Attributes;
        // FileSystemInfo reports -1 for a path where nothing exists.
        if ((int)attributes == -1)
        {
            return null;
        }

        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        return new FileEntry(info.FullName, attributes, isDirectory ? 0 : info.Length, info.LastWriteTimeUtc);
    }

    public IEnumerable<FileEntry> Enumerate(string directory, string searchPattern, EnumerationOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(searchPattern);
        ArgumentNullException.ThrowIfNull(options);
        return new FileSystemEnumerable<FileEntry>(Path.GetFullPath(directory),
            (ref entry) => new FileEntry(entry.ToFullPath(), entry.Attributes, entry.IsDirectory ? 0 : entry.Length,
                entry.LastWriteTimeUtc),
            options)
        {
            ShouldIncludePredicate = (ref entry) => EnumerationMatching.IsMatch(searchPattern, entry.FileName, options),
            ShouldRecursePredicate = (ref entry) => (entry.Attributes & FileAttributes.ReparsePoint) == 0,
        };
    }

    public Stream OpenRead(string path, FileShare share) => SharedFileRead.Open(path, share);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        SharedFileRead.ReadAllBytesAsync(path, Array.MaxLength, cancellationToken);

    public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(SharedFileRead.Open(path, SharedFileRead.Sharing), Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public string GetCanonicalPath(string path) => PathCanonicalizer.Canonicalize(path);
}
