using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>
/// Finds Steam games through the client's own records: its library lists, the app manifests in each library, the
/// <c>downloading</c> folder and the per-app registry values.
/// </summary>
public sealed partial class SteamLibraryProvider : ILibraryProvider
{
    /// <summary>The <see cref="LibraryLocation.Source"/> of a Steam library folder.</summary>
    public const string LibrarySource = "steam-library";

    private const long MaxManifestLength = 16 * 1024 * 1024;

    // Manifests and the library list change with every install, update and library change; downloads show up in
    // "downloading" before the manifest says anything.
    private static readonly string[] SteamAppsEntries = [SteamPaths.ManifestPattern, SteamPaths.Downloading, SteamPaths.LibraryFoldersFile];

    private static readonly string[] SteamGameMarkers = ["steam_api.dll", "steam_api64.dll", "steam_appid.txt"];

    private static readonly EnumerationOptions ManifestSearch = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        MatchType = MatchType.Simple,
    };

    private readonly IFileSystem _fileSystem;
    private readonly IRegistry _registry;
    private readonly LibraryEnvironment _environment;
    private readonly SteamRootLocator _roots;
    private readonly ILogger _logger;

    public SteamLibraryProvider(IFileSystem fileSystem, IRegistry registry, LibraryEnvironment environment,
        ILogger<SteamLibraryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _registry = registry;
        _environment = environment;
        _roots = new SteamRootLocator(fileSystem, registry, environment);
        _logger = logger;
    }

    public StoreId Store => StoreId.Steam;

    public async Task<StoreScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var roots = _roots.Locate();
        var (libraries, listsComplete) = await LocateLibrariesAsync(roots, context.ManualFolders, cancellationToken).ConfigureAwait(false);
        var scan = await ScanLibrariesAsync(libraries, cancellationToken).ConfigureAwait(false);
        _logger.ScanCompleted(scan.Games.Count, libraries.Count, scan.Libraries.Count(library => library.State != LibraryState.Online));
        return scan with
        {
            IsPresent = roots.Count > 0 || libraries.Any(library => library.IsManual),
            IsComplete = listsComplete,
            Watch = PlanWatches(roots, libraries),
        };
    }

    public async Task<InstalledGame?> ProbeAsync(InstalledGame game, LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        var library = DescribeLibrary(game.LibraryId, isMounted: null, isManual: false);
        var probed = (await ScanLibrariesAsync([library], cancellationToken).ConfigureAwait(false)).Describe(game);

        // Steam may have moved the game to another library since the last scan.
        return probed ?? (await ScanAsync(context, cancellationToken).ConfigureAwait(false)).Describe(game);
    }

    public Task<LibraryFolderEvidence> InspectFolderAsync(string path, CancellationToken cancellationToken)
    {
        var picked = _fileSystem.GetCanonicalPath(path);

        // A picked steamapps folder, or a game folder inside one, stands for the library above it.
        var candidate = picked;
        for (var level = 0; candidate is not null && level <= 3; level++, candidate = Path.GetDirectoryName(candidate))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SteamPaths.FindSteamApps(_fileSystem, candidate) is { } steamApps && HasManifest(steamApps))
            {
                return Task.FromResult(LibraryFolderEvidence.Accepted(candidate));
            }
        }

        var isSteamGame = SteamGameMarkers.Any(marker => _fileSystem.FileExists(Path.Join(picked, marker)));
        return Task.FromResult(isSteamGame ? LibraryFolderEvidence.Rejected(LibraryFolderRejection.GameOutsideLibrary) : LibraryFolderEvidence.None);
    }

    private async Task<(IReadOnlyList<SteamLibrary> Libraries, bool IsComplete)> LocateLibrariesAsync(IReadOnlyList<string> roots,
        IReadOnlyList<string> manualFolders, CancellationToken cancellationToken)
    {
        var libraries = new List<SteamLibrary>();
        var seen = new HashSet<string>(PathComparison.Comparer);
        void Add(string path, bool? isMounted, bool isManual)
        {
            if (!Path.IsPathFullyQualified(path) || path.Contains('\0', StringComparison.Ordinal))
            {
                _logger.LibraryPathInvalid();
                return;
            }

            var library = DescribeLibrary(path, isMounted, isManual);
            if (seen.Add(library.Path))
            {
                libraries.Add(library);
            }
        }

        // Roots come first and are always online: another root's stale list may call them unmounted.
        foreach (var root in roots)
        {
            Add(root, isMounted: null, isManual: false);
        }

        foreach (var folder in manualFolders)
        {
            Add(folder, isMounted: null, isManual: true);
        }

        var complete = true;
        foreach (var listOwner in roots.Concat(manualFolders))
        {
            var (folders, readable) = await ReadLibraryListsAsync(listOwner, cancellationToken).ConfigureAwait(false);
            complete &= readable;
            foreach (var folder in folders)
            {
                Add(folder.Path, folder.IsMounted, isManual: false);
            }
        }

        return (libraries, complete);
    }

    private SteamLibrary DescribeLibrary(string path, bool? isMounted, bool isManual)
    {
        var canonical = _fileSystem.GetCanonicalPath(path);
        var steamApps = SteamPaths.FindSteamApps(_fileSystem, canonical);
        return new SteamLibrary(canonical, steamApps ?? Path.Join(canonical, SteamPaths.SteamApps),
            isMounted != false && steamApps is not null, isManual);
    }

    // IsReadable: at least one copy of each existing list could be read.
    private async Task<(List<SteamLibraryFolder> Folders, bool IsReadable)> ReadLibraryListsAsync(string root,
        CancellationToken cancellationToken)
    {
        // The current client keeps the same list in two places; older ones only wrote the steamapps copy.
        string[] files =
        [
            Path.Join(SteamPaths.FindSteamApps(_fileSystem, root) ?? Path.Join(root, SteamPaths.SteamApps), SteamPaths.LibraryFoldersFile),
            Path.Join(root, SteamPaths.Config, SteamPaths.LibraryFoldersFile),
        ];

        var folders = new List<SteamLibraryFolder>();
        var existing = 0;
        var parsed = 0;
        foreach (var file in files.Where(_fileSystem.FileExists))
        {
            existing++;
            try
            {
                folders.AddRange(SteamLibraryFolders.Parse(await VdfParser.ParseFileAsync(_fileSystem, file, cancellationToken)
                    .ConfigureAwait(false)));
                parsed++;
            }
            catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
            {
                _logger.LibraryListUnreadable(SteamLog.Describe(exception));
            }
        }

        return (folders, existing == 0 || parsed > 0);
    }

    private async Task<StoreScan> ScanLibrariesAsync(IReadOnlyList<SteamLibrary> libraries, CancellationToken cancellationToken)
    {
        var locations = new List<LibraryLocation>();
        var installations = new List<SteamInstallation>();
        var unreadable = new List<UnreadableGame>();
        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = library.IsOnline ? await ScanLibraryAsync(library, cancellationToken).ConfigureAwait(false) : null;
            locations.Add(new LibraryLocation(library.Path, LibrarySource, library.Path,
                scan is null ? LibraryState.Offline : LibraryState.Online, library.IsManual, scan?.Unreadable.Count ?? 0));
            if (scan is not null)
            {
                installations.AddRange(scan.Installations);
                unreadable.AddRange(scan.Unreadable);
            }
        }

        return new StoreScan
        {
            Store = Store,
            Libraries = new ValueList<LibraryLocation>([.. locations]),
            Games = new ValueList<InstalledGame>([.. OnePerInstallDirectory(installations)]),
            UnreadableGames = new ValueList<UnreadableGame>([.. unreadable]),
        };
    }

    // Null when the library cannot be listed, which is treated as offline.
    private async Task<LibraryScan?> ScanLibraryAsync(SteamLibrary library, CancellationToken cancellationToken)
    {
        List<FileEntry> files;
        try
        {
            // Steam writes temporary siblings (such as *.acf.tmp) before renaming them, so only exact names count.
            files = [.. _fileSystem.Enumerate(library.SteamAppsDirectory, SteamPaths.ManifestPattern, ManifestSearch)
                .Where(file => !file.IsDirectory && ManifestFileName().IsMatch(file.Name))];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger.LibraryUnreadable(SteamLog.Describe(exception));
            return null;
        }

        var downloads = SteamDownloads.ActiveAppIds(_fileSystem, library.SteamAppsDirectory);
        var installations = new List<SteamInstallation>();
        var unreadable = new List<UnreadableGame>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!uint.TryParse(ManifestFileName().Match(file.Name).Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var appId) || appId == 0)
            {
                continue;
            }

            SteamAppManifest manifest;
            try
            {
                manifest = await ReadManifestAsync(file, appId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
            {
                // Usually a manifest caught while Steam rewrites it, which means Steam is working on the app right now.
                _logger.AppSkipped(appId, SteamLog.Describe(exception));
                unreadable.Add(new UnreadableGame(KeyOf(appId), library.Path, _fileSystem.GetEntry(file.FullPath)?.LastWriteTimeUtc));
                continue;
            }

            if (Evaluate(library, manifest, file.LastWriteTimeUtc, downloads) is { } installation)
            {
                installations.Add(installation);
            }
        }

        return new LibraryScan(installations, unreadable);
    }

    private async Task<SteamAppManifest> ReadManifestAsync(FileEntry file, uint appId, CancellationToken cancellationToken)
    {
        if (file.Length > MaxManifestLength)
        {
            throw new FormatException($"The manifest of app {appId} is too large.");
        }

        var manifest = SteamAppManifest.Parse(await VdfParser.ParseFileAsync(_fileSystem, file.FullPath, cancellationToken).ConfigureAwait(false));

        // A copied or renamed file would otherwise lend its content to another app.
        return manifest.AppId == appId
            ? manifest
            : throw new FormatException($"The manifest describes app {manifest.AppId}, not the {appId} in its file name.");
    }

    private SteamInstallation? Evaluate(SteamLibrary library, SteamAppManifest manifest, DateTimeOffset lastWriteTime,
        IReadOnlySet<uint> downloads)
    {
        if (SteamToolFilter.IsTool(manifest))
        {
            return null;
        }

        var common = Path.Join(library.SteamAppsDirectory, SteamPaths.Common);
        var folder = SteamPaths.FindChildDirectory(_fileSystem, common, manifest.InstallDir);

        // Soundtracks are apps with manifests too, but Steam puts their files under music instead of common.
        if (folder is null
            && SteamPaths.FindChildDirectory(_fileSystem, Path.Join(library.SteamAppsDirectory, SteamPaths.Music), manifest.InstallDir) is not null)
        {
            return null;
        }

        var status = SteamStateResolver.Resolve(manifest, folder is not null,
            new SteamLiveSignals(downloads.Contains(manifest.AppId), IsRegistryUpdating(manifest.AppId)));
        if (status.GameState is not { } resolved)
        {
            return null;
        }

        if (status.Status != SteamAppStatus.Installed)
        {
            _logger.AppStatus(manifest.AppId, status.Describe());
        }

        // The manifest's write time rather than its LastUpdated field: Steam rewrites the file throughout a job, so a
        // recent write tells the caller to wait for a quiet period before touching the files.
        var game = new InstalledGame
        {
            Key = KeyOf(manifest.AppId),
            Name = manifest.Name ?? manifest.AppId.ToString(CultureInfo.InvariantCulture),
            InstallDirectory = _fileSystem.GetCanonicalPath(folder ?? Path.Join(common, manifest.InstallDir)),
            LibraryId = library.Path,
            BuildId = manifest.BuildId,
            State = resolved.State,
            Reason = resolved.Reason,
            StoreActivityAt = lastWriteTime,
            Platform = _environment.Platform.AsOperatingSystem(),
            Languages = LanguageNames.FromName(manifest.Language) is { } language ? [language] : [],
        };
        return new SteamInstallation(game, manifest);
    }

    // Set before the manifest changes, as soon as a download or update starts.
    private bool IsRegistryUpdating(uint appId) =>
        _environment.Platform == AppPlatform.Windows
        && _registry.ReadKey(RegistryHive.CurrentUser, RegistryView.Default,
                $@"{SteamRootLocator.SteamKey}\Apps\{appId.ToString(CultureInfo.InvariantCulture)}")
            ?.GetValue("Updating")?.AsNumber() is > 0;

    private WatchPlan PlanWatches(IReadOnlyList<string> roots, IReadOnlyList<SteamLibrary> libraries)
    {
        var folders = new List<WatchedFolder>();
        foreach (var library in libraries)
        {
            if (!library.IsOnline)
            {
                // A drive plugged back in, or a folder restored, shows up here.
                if (WatchedFolder.Toward(_fileSystem, library.SteamAppsDirectory) is { } toward)
                {
                    folders.Add(toward);
                }

                continue;
            }

            folders.Add(new WatchedFolder(library.SteamAppsDirectory, SteamAppsEntries));
            var downloading = Path.Join(library.SteamAppsDirectory, SteamPaths.Downloading);
            if (_fileSystem.DirectoryExists(downloading))
            {
                folders.Add(new WatchedFolder(downloading, []));
            }
        }

        // The current client keeps a second copy of its library list in config. Without any root, the places Steam may
        // be installed to are watched, so an install made while the app runs is found.
        var rootFiles = roots.Count > 0
            ? roots.Select(root => Path.Join(root, SteamPaths.Config, SteamPaths.LibraryFoldersFile))
            : _roots.Candidates().Select(candidate => Path.Join(candidate, SteamPaths.SteamApps));
        folders.AddRange(rootFiles.Select(file => WatchedFolder.Toward(_fileSystem, file)).OfType<WatchedFolder>());

        List<WatchedRegistryKey> keys = _environment.Platform == AppPlatform.Windows
            ?
            [
                // SteamPath moves with Steam; the Apps values change when downloads start.
                new(RegistryHive.CurrentUser, RegistryView.Default, SteamRootLocator.SteamKey, IncludeSubkeys: false),
                new(RegistryHive.CurrentUser, RegistryView.Default, $@"{SteamRootLocator.SteamKey}\Apps", IncludeSubkeys: true),
            ]
            : [];
        return new WatchPlan(new ValueList<WatchedFolder>([.. folders.Distinct()]), new ValueList<WatchedRegistryKey>([.. keys]));
    }

    private bool HasManifest(string steamApps)
    {
        try
        {
            return _fileSystem.Enumerate(steamApps, SteamPaths.ManifestPattern, ManifestSearch)
                .Any(file => !file.IsDirectory && ManifestFileName().IsMatch(file.Name));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static GameKey KeyOf(uint appId) => new(StoreId.Steam, appId.ToString(CultureInfo.InvariantCulture));

    // Several apps can resolve to one folder: a shared installdir (a game and its playtest or episodes), or links between
    // libraries. Projects are installed per folder, so exactly one app may claim it: the one whose files Steam considers
    // complete.
    private static IEnumerable<InstalledGame> OnePerInstallDirectory(IEnumerable<SteamInstallation> installations) =>
        installations
            .GroupBy(installation => installation.Game.InstallDirectory, PathComparison.Comparer)
            .Select(ClaimFolder);

    private static InstalledGame ClaimFolder(IEnumerable<SteamInstallation> sharing)
    {
        var owner = sharing
            .OrderBy(installation => Rank(installation.Game.State))
            .ThenByDescending(installation => installation.Manifest.SizeOnDisk)
            .ThenBy(installation => installation.Manifest.AppId)
            .First();

        // Steam writes the other apps' files, shared content included, into the same folder, so their jobs and manifest
        // writes hold the folder back as well.
        var game = owner.Game with { StoreActivityAt = sharing.Max(installation => installation.Game.StoreActivityAt) };
        return game.State == GameInstallState.Installed && sharing.Any(installation => installation.Game.State is GameInstallState.Updating
            or GameInstallState.Installing or GameInstallState.Validating or GameInstallState.Running or GameInstallState.Unknown)
            ? game with { State = GameInstallState.Updating, Reason = GameStateReason.SharedFolderBusy }
            : game;
    }

    private static int Rank(GameInstallState state) => state switch
    {
        GameInstallState.Installed => 0,
        GameInstallState.Broken or GameInstallState.Offline => 2,
        _ => 1,
    };

    [GeneratedRegex("^appmanifest_([0-9]{1,10})\\.acf$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ManifestFileName();

    private sealed record SteamLibrary(string Path, string SteamAppsDirectory, bool IsOnline, bool IsManual);

    private sealed record SteamInstallation(InstalledGame Game, SteamAppManifest Manifest);

    private sealed record LibraryScan(List<SteamInstallation> Installations, List<UnreadableGame> Unreadable);
}
