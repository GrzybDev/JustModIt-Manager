using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Creator;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>
/// Invented folders for the package creator in the system's temporary folder: a game the stores report and an author's
/// folder laid out like it, removed when the test ends.
/// </summary>
public sealed class CreatorWorkspace : IDisposable
{
    public static readonly GameKey Lantern = new(StoreId.Steam, "5100");
    public static readonly GameKey Tidewater = new(StoreId.Steam, "5200");
    public static readonly GameKey Ashfall = new(StoreId.Gog, "5300");

    public CreatorWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "ExampleApp.CreatorTests", Guid.NewGuid().ToString("N"));
        GameFolder = Path.Combine(Root, "Games", "Lantern Isle");
        AuthorFolder = Path.Combine(Root, "Work", "Lantern Isle Files");
        Game("bin/lantern.exe", "lantern executable");
        Game("data/strings.bin", "strings of the original game");
        Game("data/config.xml", "<config language=\"en\" />");
        Author("data/strings.bin", "strings changed by the author, a little longer");
        Author("data/extra/credits.txt", "credits of the author's work");
    }

    public string Root { get; }

    public string GameFolder { get; }

    public string AuthorFolder { get; }

    /// <summary>The site's projects for Lantern Isle, as the agent's catalogue matched them.</summary>
    public IReadOnlyList<SiteProjectView> LanternProjects { get; set; } = [];

    /// <summary>The games the stores report: one ready, one with a package on it, one its store updates.</summary>
    public IReadOnlyList<DetectedGameEntry> Detected =>
    [
        Entry(Ashfall, "Ashfall Chronicles", Path.Combine(Root, "Games", "Ashfall"), GameInstallState.Updating),
        Entry(Lantern, "Lantern Isle", GameFolder, GameInstallState.Installed) with { Projects = LanternProjects },
        Entry(Tidewater, "Tidewater", Path.Combine(Root, "Games", "Tidewater"), GameInstallState.Installed),
    ];

    public string Game(string path, string content) => Write(GameFolder, path, content);

    /// <summary>Lantern Isle as another store reports it, in a folder of its own holding the same files.</summary>
    public DetectedGameEntry CopyOfLantern(GameKey key)
    {
        var folder = Path.Combine(Root, "Games", "Lantern Isle " + key.Store.Value);
        foreach (var file in Directory.EnumerateFiles(GameFolder, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(folder, Path.GetRelativePath(GameFolder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        return Entry(key, "Lantern Isle", folder, GameInstallState.Installed);
    }

    public string Author(string path, string content) => Write(AuthorFolder, path, content);

    /// <summary>Another folder of the author's beside the first, e.g. an add-on's, laid out like the game.</summary>
    public string Folder(string name, params (string Path, string Content)[] files)
    {
        var folder = Path.Combine(Root, "Work", name);
        foreach (var (path, content) in files)
        {
            Write(folder, path, content);
        }

        return folder;
    }

    /// <summary>Puts the creator's page, with an optional file system, in place of the window's own.</summary>
    /// <param name="fileSystem">Reads the folders; null for the real file system.</param>
    public static Action<UiComposition> Compose(IFileSystem? fileSystem = null) => ui =>
    {
        CreatorPageViewModel? creator = null;
        ui.Pages.Register<CreatorRoute>((_, services) => creator ??= new CreatorPageViewModel(services, fileSystem));
        ui.Pages.Register<CreatorProjectRoute>((_, services) => (creator ??= new CreatorPageViewModel(services, fileSystem)).OpenPage!);
    };

    /// <summary>
    /// Makes a project from a folder the author picked, on a game the dialog recognised or the author chose from its list, and opens
    /// it; of the game's copies it takes only the first, as an author who turned the others off.
    /// </summary>
    /// <param name="ui">The window, on the library.</param>
    /// <param name="folder">The author's folder.</param>
    /// <param name="game">The game's name among the detected ones.</param>
    /// <returns>The project's page, once it knows its game; its comparison has started.</returns>
    public static async Task<ProjectPageViewModel> NewProjectAsync(UiHarness ui, string folder, string game)
    {
        ui.Pickers.Folder = folder;
        ((CreatorPageViewModel)ui.Shell.Navigator.Current!).NewProjectCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is NewProjectDialogViewModel { HasShown: true } or NewProjectDialogViewModel { IsListed: true });
        var dialog = (NewProjectDialogViewModel)ui.Shell.Dialogs.Current!;
        if (dialog.Shown?.Name != game)
        {
            dialog.ShowListCommand.Execute(null);
            await UiHarness.WaitUntilAsync(() => dialog.IsListed);
            dialog.PickCommand.Execute(dialog.Rows.SelectMany(row => new[] { row.Left, row.Right }).First(option => option?.Name == game));
        }

        foreach (var copy in dialog.VersionTiles.Skip(1))
        {
            dialog.ToggleVersionCommand.Execute(copy);
        }

        dialog.CreateCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null && ui.Shell.Navigator.Current is ProjectPageViewModel { Game: not null });
        return (ProjectPageViewModel)ui.Shell.Navigator.Current!;
    }

    /// <summary>Answers the window's question for the detected games, and every other request with nothing.</summary>
    public Func<Message, Message?> Answer => request => request is GetDetectedGames ? new DetectedGames(Detected) : null;

    // A package just built may still be open for a moment, e.g. in a virus scanner, and a folder left behind harms no test.
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TestContext.Current.AddWarning($"The temporary folder {Root} was left behind: {exception.Message}");
        }
    }

    private static DetectedGameEntry Entry(GameKey key, string name, string folder, GameInstallState state) =>
        new(key, name, new DetectedGameView { InstallDirectory = folder, LibraryId = "library", State = state, BuildId = "18001", CanLaunch = true });

    private static string Write(string folder, string path, string content)
    {
        var full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }
}

/// <summary>The real file system, with a drive's root that shows a folder of the test's, so an author's folder can lie at a drive's root.</summary>
/// <param name="root">The drive's root, e.g. <c>Q:\</c>.</param>
/// <param name="folder">The folder it shows.</param>
public sealed class DriveRootFileSystem(string root, string folder) : IFileSystem
{
    private readonly PhysicalFileSystem _inner = PhysicalFileSystem.Instance;

    public bool FileExists(string path) => _inner.FileExists(Real(path));

    public bool DirectoryExists(string path) => _inner.DirectoryExists(Real(path));

    public FileEntry? GetEntry(string path) => _inner.GetEntry(Real(path)) is { } entry ? entry with { FullPath = path } : null;

    public IEnumerable<FileEntry> Enumerate(string directory, string searchPattern, EnumerationOptions options) =>
        _inner.Enumerate(Real(directory), searchPattern, options).Select(entry => entry with { FullPath = Shown(entry.FullPath) });

    public Stream OpenRead(string path, FileShare share) => _inner.OpenRead(Real(path), share);

    public Task<byte[]> ReadAllBytesAsync(string path, long maxLength, CancellationToken cancellationToken) =>
        _inner.ReadAllBytesAsync(Real(path), maxLength, cancellationToken);

    public Task<string> ReadAllTextAsync(string path, long maxLength, CancellationToken cancellationToken) =>
        _inner.ReadAllTextAsync(Real(path), maxLength, cancellationToken);

    public string GetCanonicalPath(string path) => Shown(_inner.GetCanonicalPath(Real(path)));

    private string Real(string path) => path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? Path.Join(folder, path[root.Length..]) : path;

    private string Shown(string path) => path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) ? Path.Join(root, path[folder.Length..].TrimStart(Path.DirectorySeparatorChar)) : path;
}

/// <summary>
/// The real file system, except that reading a file of a folder waits until the test lets it, so a comparison can be
/// caught under way, and then fails with what the test sets, as a driver or an encoder might.
/// </summary>
/// <param name="folder">The folder whose files wait.</param>
public sealed class HeldFileSystem(string folder) : IFileSystem
{
    private readonly PhysicalFileSystem _inner = PhysicalFileSystem.Instance;

    public ManualResetEventSlim Reads { get; } = new();

    /// <summary>What reading a file of the folder throws once the test lets it; null reads the file.</summary>
    public Exception? Failure { get; set; }

    public bool FileExists(string path) => _inner.FileExists(path);

    public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

    public FileEntry? GetEntry(string path) => _inner.GetEntry(path);

    public IEnumerable<FileEntry> Enumerate(string directory, string searchPattern, EnumerationOptions options) =>
        _inner.Enumerate(directory, searchPattern, options);

    public Stream OpenRead(string path, FileShare share)
    {
        if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
        {
            Reads.Wait(UiHarness.TimeLimit);
            if (Failure is { } failure)
            {
                throw failure;
            }
        }

        return _inner.OpenRead(path, share);
    }

    public Task<byte[]> ReadAllBytesAsync(string path, long maxLength, CancellationToken cancellationToken) =>
        _inner.ReadAllBytesAsync(path, maxLength, cancellationToken);

    public Task<string> ReadAllTextAsync(string path, long maxLength, CancellationToken cancellationToken) =>
        _inner.ReadAllTextAsync(path, maxLength, cancellationToken);

    public string GetCanonicalPath(string path) => _inner.GetCanonicalPath(path);
}
