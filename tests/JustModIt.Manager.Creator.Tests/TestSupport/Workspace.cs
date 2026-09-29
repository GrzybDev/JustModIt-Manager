using System.Text;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Creator.Comparison;

namespace JustModIt.Manager.Creator.Tests.TestSupport;

/// <summary>A game folder, an author's folder and a folder for packages, side by side in a temporary folder.</summary>
internal sealed class Workspace : IDisposable
{
    public const string HiddenFolderName = ".example-app";

    public const string Generator = "ExampleApp 1.2.3";

    public const string InstanceName = "ExampleApp";

    public static readonly CreatorOptions Options = new()
    {
        HiddenFolderName = HiddenFolderName,
        InstanceName = InstanceName,
        Generator = Generator,
        PackageExtension = ".examplemod",
        ProjectExtension = ".exampleproj",
    };

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
        IProgress<CompareProgress>? progress = null, CancellationToken? cancellationToken = null, CreatorOptions? options = null) =>
        new FolderComparer(options ?? Options).CompareAsync(new ComparisonRequest(GameDirectory, AuthorDirectory, installs ?? []),
            progress, cancellationToken ?? TestContext.Current.CancellationToken);

    /// <summary>Marks a folder in the game as an app's, as that app's owner file would.</summary>
    /// <param name="path">The folder, relative to the game.</param>
    /// <param name="app">The app's instance name, as its owner file gives it.</param>
    public string MarkGameFolder(string path, string app) =>
        Game(Path.Combine(path, AppFolders.OwnerFileName), $$"""{"version":1,"app":"{{app}}"}""");

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
