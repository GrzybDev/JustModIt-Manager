using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Platform;

public sealed class InMemoryFileSystemTests : FileSystemContractTests
{
    private readonly InMemoryFileSystem _fileSystem = new();

    public InMemoryFileSystemTests() => _fileSystem.AddDirectory(Root);

    protected override IFileSystem FileSystem => _fileSystem;

    protected override string Root { get; } = Path.Combine(OperatingSystem.IsWindows() ? @"D:\" : "/mnt", "Library");

    protected override string WriteFile(string relativePath, byte[] content, DateTimeOffset lastWriteTimeUtc) =>
        _fileSystem.AddFile(Path.Combine(Root, relativePath), content, lastWriteTimeUtc);

    protected override string CreateDirectory(string relativePath, FileAttributes attributes = 0) =>
        _fileSystem.AddDirectory(Path.Combine(Root, relativePath), attributes | FileAttributes.Directory);

    protected override string CreateDirectoryLink(string relativePath, string target) =>
        _fileSystem.AddDirectoryLink(Path.Combine(Root, relativePath), target);
}
