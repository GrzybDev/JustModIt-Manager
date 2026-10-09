using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library.Heroic;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Library.Epic;

/// <summary>
/// Finds Epic Games Store games through the Epic Games Launcher and Legendary, on its own or inside Heroic, knowing a
/// game by its base game's catalog namespace, since both list one game when Legendary imports the launcher's installs.
/// </summary>
public sealed class EpicLibraryProvider : ILibraryProvider
{
    private readonly IFileSystem _fileSystem;
    private readonly InstallRecordScanner<EpicInstallRecord> _scanner;

    /// <summary>Creates a provider that reads the given launchers.</summary>
    /// <param name="fileSystem">Reads the launchers' files and the games' folders.</param>
    /// <param name="sources">Launchers, in order of precedence; folders the player added are always read.</param>
    /// <param name="platform">The system the app runs on, taken as the system of games in folders the player added.</param>
    /// <param name="logger">Logs sources and records that were skipped.</param>
    internal EpicLibraryProvider(IFileSystem fileSystem, IEnumerable<IInstallSource<EpicInstallRecord>> sources, AppPlatform platform,
        ILogger<EpicLibraryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _scanner = new InstallRecordScanner<EpicInstallRecord>(Store, [.. sources, new EpicFolderSource(fileSystem, platform)], logger);
    }

    public StoreId Store => StoreId.Epic;

    /// <summary>
    /// Creates the provider for the launchers of this machine: the Epic Games Launcher on Windows, then every Heroic,
    /// then Legendary itself.
    /// </summary>
    /// <param name="fileSystem">Reads the launchers' files and the games' folders.</param>
    /// <param name="registry">Tells where the Epic Games Launcher keeps its data.</param>
    /// <param name="environment">The user's folders the launchers are found relative to.</param>
    /// <param name="logger">Logs sources and records that were skipped.</param>
    public static EpicLibraryProvider Create(IFileSystem fileSystem, IRegistry registry, LibraryEnvironment environment,
        ILogger<EpicLibraryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        List<IInstallSource<EpicInstallRecord>> sources = environment is { Platform: AppPlatform.Windows, ProgramData: { } programData }
            ? [new EpicLauncherSource(fileSystem, registry, programData)]
            : [];
        sources.AddRange(HeroicLauncher.ConfigDirectories(environment).Select(directory => LegendarySource.InHeroic(fileSystem, directory)));
        sources.Add(new LegendarySource(fileSystem, LegendarySource.StandaloneDirectory(environment)));
        return new EpicLibraryProvider(fileSystem, sources, environment.Platform, logger);
    }

    public Task<StoreScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken) =>
        _scanner.ScanAsync(context, EvaluateFolderAsync, cancellationToken);

    public async Task<InstalledGame?> ProbeAsync(InstalledGame game, LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        // A game's records may be in any launcher, and its DLC's records tell whether its folder is written; there are few.
        return (await ScanAsync(context, cancellationToken).ConfigureAwait(false)).Describe(game);
    }

    public async Task<LibraryFolderEvidence> InspectFolderAsync(string path, CancellationToken cancellationToken)
    {
        var folder = _fileSystem.GetCanonicalPath(path);
        return (await EpicGameFolder.ReadManifestsAsync(_fileSystem, folder, cancellationToken).ConfigureAwait(false)).Count > 0
            ? LibraryFolderEvidence.Accepted(folder)
            : LibraryFolderEvidence.None;
    }

    private Task<IReadOnlyList<RecordFinding>> EvaluateFolderAsync(EpicInstallRecord record, CancellationToken cancellationToken)
    {
        var directory = _fileSystem.GetCanonicalPath(record.InstallPath);
        var (state, reason) = !_fileSystem.DirectoryExists(directory) ? InstallFolder.MissingState(_fileSystem, directory)
            : record.Activity is { } activity ? (activity, GameStateReason.StoreBusy)
            : EpicGameFolder.Activity(_fileSystem, directory) ?? (GameInstallState.Installed, GameStateReason.None);
        var game = new InstalledGame
        {
            Key = new GameKey(Store, record.GameId),
            Name = record.Name ?? record.AppName,
            InstallDirectory = directory,
            LibraryId = record.LibraryId,
            BuildId = record.BuildId,
            State = state,
            Reason = reason,
            StoreActivityAt = new[] { record.ActivityAt, EpicGameFolder.LastWrittenAt(_fileSystem, directory) }.Max(),
            Platform = record.Platform,
            Launch = record.Launch,
        };
        return Task.FromResult<IReadOnlyList<RecordFinding>>([new RecordFinding(record.GameId, game)]);
    }
}
