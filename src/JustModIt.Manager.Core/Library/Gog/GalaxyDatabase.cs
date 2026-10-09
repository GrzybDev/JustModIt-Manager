using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library.Gog;

/// <summary>What GOG Galaxy's databases say about the games it manages.</summary>
/// <param name="Installed">The games Galaxy installed, by product id and folder.</param>
/// <param name="Jobs">What Galaxy is doing with a product: installing, updating or verifying it.</param>
public sealed record GalaxyGames(IReadOnlyList<(string GameId, string Folder)> Installed, IReadOnlyDictionary<string, GameInstallState> Jobs)
{
    public static GalaxyGames None { get; } = new([], new Dictionary<string, GameInstallState>());

    /// <summary>Tells whether Galaxy installed <paramref name="gameId"/> into <paramref name="folder"/>.</summary>
    /// <param name="gameId">A GOG product id.</param>
    /// <param name="folder">The folder a record names for it.</param>
    public bool Manages(string gameId, string folder) =>
        Installed.Any(install => install.GameId == gameId && PathComparison.AreSame(install.Folder, folder));
}

/// <summary>
/// GOG Galaxy's own databases, read only, which alone tell which games Galaxy manages and works on, since Galaxy
/// installs games with the same registry records and files as GOG's offline installers.
/// </summary>
/// <param name="fileSystem">Tells whether the databases' folder exists.</param>
/// <param name="sqlite">Reads the databases.</param>
/// <param name="storageDirectory">Galaxy's storage folder, which holds the databases.</param>
public sealed class GalaxyDatabase(IFileSystem fileSystem, ISqliteReader sqlite, string storageDirectory)
{
    private const string LibraryFile = "galaxy-2.0.db";
    private const string JobsFile = "jobs.db";

    private const string InstalledQuery = "SELECT productId, installationPath FROM InstalledBaseProducts";

    // A job stays until it finishes, also while it waits in Galaxy's queue or is paused.
    private const string JobsQuery =
        "SELECT productId, 'install' FROM InstallProductJobs "
        + "UNION ALL SELECT productId, CASE WHEN isVerifying THEN 'verify' ELSE 'update' END FROM UpdateProductJobs";

    // Every reader writes the -shm file, the app's own reads included, so it is not watched.
    private static readonly string[] DatabaseFiles =
        [LibraryFile, $"{LibraryFile}-wal", $"{LibraryFile}-journal", JobsFile, $"{JobsFile}-wal", $"{JobsFile}-journal"];

    public string StorageDirectory => storageDirectory;

    /// <summary>Finds Galaxy's databases in the system's shared application data folder.</summary>
    /// <param name="fileSystem">Tells whether the databases' folder exists.</param>
    /// <param name="sqlite">Reads the databases.</param>
    /// <param name="environment">The user's folders, with the shared application data folder.</param>
    /// <returns>Null without the system's shared application data folder.</returns>
    public static GalaxyDatabase? Locate(IFileSystem fileSystem, ISqliteReader sqlite, LibraryEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return environment.ProgramData is { } programData
            ? new GalaxyDatabase(fileSystem, sqlite, Path.Join(programData, "GOG.com", "Galaxy", "storage"))
            : null;
    }

    /// <summary>The database files, so Galaxy adding, removing or working on a game is noticed.</summary>
    public WatchedFolder? Watch =>
        fileSystem.DirectoryExists(storageDirectory)
            ? new WatchedFolder(storageDirectory, DatabaseFiles)
            : WatchedFolder.Toward(fileSystem, storageDirectory);

    /// <summary>Reads which games Galaxy installed and what it is doing with them.</summary>
    /// <returns><see cref="GalaxyGames.None"/> when Galaxy has no database or SQLite is missing.</returns>
    /// <exception cref="IOException">A database stayed busy or could not be read.</exception>
    public GalaxyGames Read()
    {
        if (sqlite.Query(Path.Join(storageDirectory, LibraryFile), InstalledQuery) is not { } installed)
        {
            return GalaxyGames.None;
        }

        var jobs = sqlite.Query(Path.Join(storageDirectory, JobsFile), JobsQuery) ?? [];
        return new GalaxyGames(
            [.. installed.Where(row => GogJson.IsId(row[0]) && !string.IsNullOrWhiteSpace(row[1])).Select(row => (row[0]!, row[1]!))],
            jobs.Where(row => GogJson.IsId(row[0])).DistinctBy(row => row[0]).ToDictionary(row => row[0]!, row => row[1] switch
            {
                "install" => GameInstallState.Installing,
                "verify" => GameInstallState.Validating,
                _ => GameInstallState.Updating,
            }));
    }
}
