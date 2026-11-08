using System.Text;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;

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

    public static readonly ProjectVersion Version = new(new ProjectGame(new GameKey(StoreId.Steam, "1000010"), "Lantern Isle"));

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

    /// <summary>A project of one part, with the title and version a manifest package needs.</summary>
    /// <param name="part">The part; none gives an untouched one.</param>
    public static CreatorProject Project(ProjectPart? part = null) => new()
    {
        Parts = [part ?? new()],
        Metadata = new PackageMetadata { Title = "Example package", Version = "1.0" },
    };

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

    /// <summary>Lists the game folder and compares the author's folder with it, as the window does.</summary>
    /// <param name="installs">The installs the app keeps records of; none when null.</param>
    /// <param name="progress">Receives the comparison's progress.</param>
    /// <param name="cancellationToken">Cancels it; the test's when null.</param>
    /// <param name="options">The app's folder names; the workspace's when null.</param>
    /// <param name="layout">Where the project's folders lie on this game; as listed when null.</param>
    /// <param name="skipped">Folders of the author's that hold other packages' files; none when null.</param>
    /// <param name="authorDirectory">The author's folder; the workspace's when null.</param>
    /// <param name="other">Another version's game that shares the author's folder; none when null.</param>
    public async Task<FolderComparison> CompareAsync(IReadOnlyList<ExistingInstall>? installs = null,
        IProgress<CompareProgress>? progress = null, CancellationToken? cancellationToken = null, CreatorOptions? options = null,
        FolderLayout? layout = null, IReadOnlyList<string>? skipped = null, string? authorDirectory = null, Workspace? other = null)
    {
        var token = cancellationToken ?? TestContext.Current.CancellationToken;
        var comparer = new FolderComparer(options ?? Options);
        var listed = await comparer.ListGameAsync(GameDirectory, installs ?? [], token);
        return await comparer.CompareAsync(new ComparisonRequest(layout is { } rules ? listed.InLayout(rules) : listed, authorDirectory ?? AuthorDirectory, skipped ?? [])
        {
            OtherVersions = other is null ? [] : [await comparer.ListGameAsync(other.GameDirectory, [], token)],
        }, progress, token);
    }

    /// <summary>Plans a project's base on <see cref="Version"/> over the folders as they are now.</summary>
    /// <param name="project">The project.</param>
    /// <param name="options">The app's limits and rules; the workspace's when null.</param>
    public async Task<CreatorReview> ReviewAsync(CreatorProject project, CreatorOptions? options = null) =>
        CreatorPlanner.Plan(await CompareAsync(), project, project.Parts[0], Version, options ?? Options);

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
