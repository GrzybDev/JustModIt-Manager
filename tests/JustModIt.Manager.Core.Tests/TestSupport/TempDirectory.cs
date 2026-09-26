using System.Text;

namespace JustModIt.Manager.Core.Tests.TestSupport;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(string relativePath) => System.IO.Path.Combine(Path, relativePath);

    public string Write(string relativePath, byte[] content)
    {
        var full = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public string Write(string relativePath, string content) => Write(relativePath, Encoding.UTF8.GetBytes(content));

    public string CreateDirectory(string relativePath) => Directory.CreateDirectory(Combine(relativePath)).FullName;

    public string[] Files() =>
        [.. Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories).Select(file => System.IO.Path.GetRelativePath(Path, file))];

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
