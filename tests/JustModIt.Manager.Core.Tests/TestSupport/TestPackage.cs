using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>Builds manifest packages and plain .zip packages entry by entry, including broken ones.</summary>
internal sealed partial class TestPackage
{
    public static readonly Guid Id = Guid.Parse("6f0c9f8e-3b1a-4a55-9d0e-2f5b1c7a9e10");

    private readonly List<(string Name, byte[] Content, int Attributes)> _entries = [];
    private readonly List<PackageOperation> _operations = [];
    private readonly List<RuntimeFile> _runtimeFiles = [];
    private string? _manifest;
    private int _formatVersion = PackageManifest.CurrentFormatVersion;
    private IReadOnlyList<string>? _requires;
    private IReadOnlyList<PackageTarget> _targets = [];
    private IReadOnlyList<ThirdPartyNotice>? _notices;

    public TestPackage Copy(string path, string content) => Copy(path, Encoding.UTF8.GetBytes(content));

    public TestPackage Copy(string path, byte[] content)
    {
        var source = UniqueEntry(PackageLayout.FilesFolder + path);
        _entries.Add((source, content, 0));
        _operations.Add(new CopyOperation(path, source, content.Length, Sha256(content)));
        return this;
    }

    public TestPackage Rename(string from, string to)
    {
        _operations.Add(new RenameOperation(from, to));
        return this;
    }

    public TestPackage Delete(string path)
    {
        _operations.Add(new DeleteOperation(path));
        return this;
    }

    /// <summary>Declares a file the package's software changes while the game runs.</summary>
    /// <param name="path">The game-relative file.</param>
    /// <param name="originals">The contents its store ships, which the manifest names as its originals.</param>
    public TestPackage Declare(string path, params string[] originals)
    {
        _runtimeFiles.Add(new RuntimeFile(path, originals.Length > 0
            ? [.. originals.Select(original => Encoding.UTF8.GetBytes(original)).Select(bytes => new FileContent(bytes.Length, Sha256(bytes)))]
            : null));
        return this;
    }

    public TestPackage Operation(PackageOperation operation)
    {
        _operations.Add(operation);
        return this;
    }

    /// <summary>Limits the last operation to the games of some stores.</summary>
    /// <param name="stores">The stores it runs on.</param>
    public TestPackage Only(params StoreId[] stores)
    {
        _operations[^1] = _operations[^1] with { Only = stores };
        return this;
    }

    public TestPackage Entry(string name, string content, int externalAttributes = 0) =>
        Entry(name, Encoding.UTF8.GetBytes(content), externalAttributes);

    public TestPackage Entry(string name, byte[] content, int externalAttributes = 0)
    {
        _entries.Add((name, content, externalAttributes));
        return this;
    }

    public TestPackage FormatVersion(int version)
    {
        _formatVersion = version;
        return this;
    }

    public TestPackage Requires(params string[] capabilities)
    {
        _requires = capabilities;
        return this;
    }

    public TestPackage Targets(params PackageTarget[] targets)
    {
        _targets = targets;
        return this;
    }

    public TestPackage Notices(params ThirdPartyNotice[] notices)
    {
        _notices = notices;
        return this;
    }

    public TestPackage RawManifest(string json)
    {
        _manifest = json;
        return this;
    }

    public PackageManifest Manifest() => new()
    {
        FormatVersion = _formatVersion,
        Id = Id,
        Title = "Example package",
        Version = "1.0",
        Targets = _targets,
        Requires = _requires ?? Capabilities(),
        ThirdPartyNotices = _notices,
        RuntimeFiles = _runtimeFiles.Count > 0 ? [.. _runtimeFiles] : null,
        Operations = [.. _operations],
    };

    public string WriteManifestPackage(string path)
    {
        var manifest = _manifest ?? JsonSerializer.Serialize(Manifest(), PackageJsonContext.Default.PackageManifest);
        return Write(path, [(PackageLayout.ManifestEntry, Encoding.UTF8.GetBytes(manifest), 0), .. _entries]);
    }

    public string WriteZip(string path) => Write(path, _entries);

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    // A manifest may leave out what its operations need, but not what its other fields need.
    private List<string>? Capabilities()
    {
        var fields = new[]
        {
            (PackageCapabilities.TargetFolders, _targets.Any(target => target.Folders is not null)),
            (PackageCapabilities.TargetsOnly, _operations.Exists(operation => operation.Only is not null)),
            (PackageCapabilities.PatchAlternatives, _operations.Exists(operation => operation is PatchOperation { Alternatives: not null })),
        }.Where(field => field.Item2).Select(field => field.Item1).ToList();
        return _runtimeFiles.Count > 0 || fields.Count > 0
            ? [.. PackageCapabilities.Of(_operations, declaresFiles: _runtimeFiles.Count > 0), .. fields]
            : null;
    }

    private string UniqueEntry(string name)
    {
        var unique = name;
        for (var copy = 2; _entries.Exists(entry => PackagePath.Comparer.Equals(entry.Name, unique)); copy++)
        {
            unique = $"{name}.{copy}";
        }

        return unique;
    }

    private static string Write(string path, IEnumerable<(string Name, byte[] Content, int Attributes)> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content, attributes) in entries)
        {
            var entry = zip.CreateEntry(name);
            entry.ExternalAttributes = attributes;
            using var stream = entry.Open();
            stream.Write(content);
        }

        return path;
    }
}
