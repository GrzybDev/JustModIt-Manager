using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Platform;

public sealed class PhysicalFileSystemTests : FileSystemContractTests, IDisposable
{
    private readonly TempDirectory _directory = new();

    protected override IFileSystem FileSystem => PhysicalFileSystem.Instance;

    protected override string Root => PathComparison.Normalize(_directory.Path);

    [Fact]
    public async Task A_file_its_owner_is_writing_can_be_read()
    {
        var path = _directory.Write("appmanifest_620.acf", "\"AppState\" {}");
        await using var owner = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        (await FileSystem.ReadAllTextAsync(path, 1024, TestContext.Current.CancellationToken)).Should().Be("\"AppState\" {}");
    }

    public void Dispose() => _directory.Dispose();

    protected override string WriteFile(string relativePath, byte[] content, DateTimeOffset lastWriteTimeUtc)
    {
        var path = _directory.Write(relativePath, content);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc.UtcDateTime);
        return path;
    }

    protected override string CreateDirectory(string relativePath, FileAttributes attributes = 0)
    {
        var path = _directory.CreateDirectory(relativePath);
        if (attributes != 0)
        {
            File.SetAttributes(path, File.GetAttributes(path) | attributes);
        }

        return path;
    }

    protected override string CreateDirectoryLink(string relativePath, string target) =>
        _directory.CreateDirectoryLink(relativePath, target);
}
