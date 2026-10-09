using System.Text;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>A file tree in memory; paths follow the rules of the platform the tests run on.</summary>
internal sealed class InMemoryFileSystem : IFileSystem
{
    private static readonly DateTimeOffset DefaultTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, Node> _nodes = new(PathComparison.Comparer);
    private readonly Dictionary<string, string> _links = new(PathComparison.Comparer);

    public string AddFile(string path, string content, DateTimeOffset? lastWriteTimeUtc = null,
        FileAttributes attributes = FileAttributes.Normal) =>
        AddFile(path, Encoding.UTF8.GetBytes(content), lastWriteTimeUtc, attributes);

    public string AddFile(string path, byte[] content, DateTimeOffset? lastWriteTimeUtc = null,
        FileAttributes attributes = FileAttributes.Normal)
    {
        var full = PathComparison.Normalize(path);
        AddDirectory(Path.GetDirectoryName(full)!);
        _nodes[full] = new Node(attributes == FileAttributes.Normal ? FileAttributes.Archive : attributes, content,
            lastWriteTimeUtc ?? DefaultTime);
        return full;
    }

    public string AddDirectory(string path, FileAttributes attributes = FileAttributes.Directory, DateTimeOffset? lastWriteTimeUtc = null)
    {
        var full = PathComparison.Normalize(path);
        for (var current = full; current is not null && !_nodes.ContainsKey(current); current = Path.GetDirectoryName(current))
        {
            _nodes[current] = new Node(current == full ? attributes | FileAttributes.Directory : FileAttributes.Directory, null,
                current == full ? lastWriteTimeUtc ?? DefaultTime : DefaultTime);
        }

        return full;
    }

    /// <summary>Adds a directory link, listed as a link and resolved to its target by canonicalisation.</summary>
    /// <param name="path">Where the link is.</param>
    /// <param name="target">The folder the link points to.</param>
    public string AddDirectoryLink(string path, string target)
    {
        var full = AddDirectory(path, FileAttributes.Directory | FileAttributes.ReparsePoint);
        _links[full] = PathComparison.Normalize(target);
        return full;
    }

    public void Remove(string path)
    {
        var full = PathComparison.Normalize(path);
        foreach (var key in _nodes.Keys.Where(key => PathComparison.IsWithin(full, key)).ToList())
        {
            _nodes.Remove(key);
        }
    }

    public bool FileExists(string path) => Find(path) is { IsDirectory: false };

    public bool DirectoryExists(string path) => Find(path) is { IsDirectory: true };

    public FileEntry? GetEntry(string path)
    {
        var full = PathComparison.Normalize(path);
        return _nodes.TryGetValue(full, out var node) ? node.ToEntry(full) : null;
    }

    public IEnumerable<FileEntry> Enumerate(string directory, string searchPattern, EnumerationOptions options)
    {
        var full = PathComparison.Normalize(directory);
        if (Find(full) is not { IsDirectory: true })
        {
            throw new DirectoryNotFoundException($"Could not find a part of the path '{full}'.");
        }

        return List(full, searchPattern, options, depth: 0);
    }

    public Stream OpenRead(string path, FileShare share) => new MemoryStream(ReadFile(path), writable: false);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(ReadFile(path).ToArray());

    public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(OpenRead(path, FileShare.Read), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public string GetCanonicalPath(string path)
    {
        var full = PathComparison.Normalize(path);
        foreach (var (link, target) in _links)
        {
            if (PathComparison.IsWithin(link, full))
            {
                return GetCanonicalPath(Path.Join(target, full.AsSpan(link.Length)));
            }
        }

        return full;
    }

    private IEnumerable<FileEntry> List(string directory, string searchPattern, EnumerationOptions options, int depth)
    {
        var children = _nodes
            .Where(pair => PathComparison.Comparer.Equals(Path.GetDirectoryName(pair.Key), directory))
            .Where(pair => (pair.Value.Attributes & options.AttributesToSkip) == 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();
        foreach (var (path, node) in children)
        {
            if (EnumerationMatching.IsMatch(searchPattern, Path.GetFileName(path), options))
            {
                yield return node.ToEntry(path);
            }

            var canRecurse = options.RecurseSubdirectories && depth < options.MaxRecursionDepth;
            if (node.IsDirectory && canRecurse && (node.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                foreach (var entry in List(path, searchPattern, options, depth + 1))
                {
                    yield return entry;
                }
            }
        }
    }

    private byte[] ReadFile(string path)
    {
        var full = PathComparison.Normalize(path);
        return Find(full) switch
        {
            { IsDirectory: false } node => node.Content!,
            { IsDirectory: true } => throw new UnauthorizedAccessException($"Access to the path '{full}' is denied."),
            null when Find(Path.GetDirectoryName(full)!) is null =>
                throw new DirectoryNotFoundException($"Could not find a part of the path '{full}'."),
            null => throw new FileNotFoundException($"Could not find file '{full}'.", full),
        };
    }

    private Node? Find(string path) => _nodes.GetValueOrDefault(PathComparison.Normalize(path));

    private sealed record Node(FileAttributes Attributes, byte[]? Content, DateTimeOffset LastWriteTimeUtc)
    {
        public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;

        public FileEntry ToEntry(string path) => new(path, Attributes, Content?.LongLength ?? 0, LastWriteTimeUtc);
    }
}
