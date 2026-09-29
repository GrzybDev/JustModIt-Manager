using System.Text;
using JustModIt.Manager.Creator.Comparison;

namespace JustModIt.Manager.Creator.Tests.TestSupport;

/// <summary>A game folder, an author's folder and a folder for packages, side by side in a temporary folder.</summary>
internal sealed class Workspace : IDisposable
{
    public const string HiddenFolderName = ".example-app";

    public static readonly CreatorOptions Options = new() { HiddenFolderName = HiddenFolderName };

    public Workspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(GameDirectory);
        Directory.CreateDirectory(AuthorDirectory);
        Directory.CreateDirectory(OutputDirectory);
    }

    public string Root { get; }

    public string GameDirectory => Path.Combine(Root, "game");

    public string AuthorDirectory => Path.Combine(Root, "author");

    public string OutputDirectory => Path.Combine(Root, "out");

    public static byte[] Bytes(int size, int seed)
    {
        var bytes = new byte[size];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    public string Game(string path, byte[] content) => Write(GameDirectory, path, content);

    public string Game(string path, string content) => Game(path, Text(content));

    public string Author(string path, byte[] content) => Write(AuthorDirectory, path, content);

    public string Author(string path, string content) => Author(path, Text(content));

    public string GamePath(string path) => Path.Combine(GameDirectory, path);

    public string AuthorFile(string path) => Path.Combine(AuthorDirectory, path);

    public string Output(string name) => Path.Combine(OutputDirectory, name);

    public Task<FolderComparison> CompareAsync(IReadOnlyList<ExistingInstall>? installs = null,
        IProgress<CompareProgress>? progress = null, CancellationToken? cancellationToken = null) =>
        new FolderComparer(Options).CompareAsync(new ComparisonRequest(GameDirectory, AuthorDirectory, installs ?? []),
            progress, cancellationToken ?? TestContext.Current.CancellationToken);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Write(string root, string path, byte[] content)
    {
        var full = Path.Combine(root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }
}
