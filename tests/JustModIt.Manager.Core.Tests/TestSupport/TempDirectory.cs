using System.Diagnostics;
using System.Text;

namespace JustModIt.Manager.Core.Tests.TestSupport;

internal sealed class TempDirectory : IDisposable
{
    private readonly List<string> _links = [];

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

    /// <summary>
    /// Creates a directory link, or skips the test when it cannot: a junction on Windows, which needs no privileges unlike
    /// a symbolic link, and a symbolic link elsewhere.
    /// </summary>
    /// <param name="relativePath">Where the link goes, relative to the temporary folder.</param>
    /// <param name="target">The folder the link points to.</param>
    public string CreateDirectoryLink(string relativePath, string target)
    {
        var link = Combine(relativePath);
        if (OperatingSystem.IsWindows())
        {
            CreateJunctionOrSkip(link, target);
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }

        _links.Add(link);
        return link;
    }

    public string[] Files() =>
        [.. Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories).Select(file => System.IO.Path.GetRelativePath(Path, file))];

    public void Dispose()
    {
        try
        {
            // A recursive delete fails on junctions without admin rights, so the links go first.
            foreach (var link in Enumerable.Reverse(_links))
            {
                Directory.Delete(link);
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void CreateJunctionOrSkip(string link, string target)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", link, target },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            Assert.Skip("Junctions cannot be created on this machine.");
        }

        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Assert.Skip("Junctions cannot be created on this machine.");
        }
    }
}
