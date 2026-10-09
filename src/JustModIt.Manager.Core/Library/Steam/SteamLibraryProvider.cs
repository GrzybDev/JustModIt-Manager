using System.Globalization;
using System.Security;
using System.Text.RegularExpressions;
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
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    /// <summary>Creates a provider that finds Steam wherever the client may be installed.</summary>
    /// <param name="fileSystem">Reads the client's files and the games' folders.</param>
    /// <param name="registry">Reads where Steam is and which apps it updates, on Windows.</param>
    /// <param name="environment">The user's folders Steam is found relative to.</param>
    /// <param name="time">Tells which downloads still count as active.</param>
    /// <param name="logger">Logs what could not be read.</param>
    public SteamLibraryProvider(IFileSystem fileSystem, IRegistry registry, LibraryEnvironment environment, TimeProvider time,
        ILogger<SteamLibraryProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _registry = registry;
        _environment = environment;
        _roots = new SteamRootLocator(fileSystem, registry, environment);
        _time = time;
        _logger = logger;
    }

    public StoreId Store => StoreId.Steam;

    public async Task<StoreScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var access = context.Access;
        var candidates = await Task.WhenAll(_roots.Candidates().Select(candidate => FindRootAsync(candidate, access, cancellationToken)))
            .ConfigureAwait(false);
        List<RootCandidate> roots =
            [.. candidates.Where(candidate => candidate.Root is not null).DistinctBy(candidate => candidate.Root, PathComparison.Comparer)];
        var (libraries, listsComplete) = await LocateLibrariesAsync([.. roots.Select(root => root.Root!)], context.ManualFolders, access,
            cancellationToken).ConfigureAwait(false);
        var reads = await Task.WhenAll(libraries.Select(library => ReadLibraryAsync(library, access, cancellationToken))).ConfigureAwait(false);
        var scan = Combine(reads);
        _logger.ScanCompleted(scan.Games.Count, libraries.Count, scan.Libraries.Count(library => library.State != LibraryState.Online));

        // The current client keeps a second copy of its library list in config. Without any root, the places Steam may be
        // installed to are watched, so an install made while the app runs is found.
        var rootWatches = roots.Count > 0 ? roots.Select(root => root.ListWatch) : candidates.Select(candidate => candidate.Watch);
        return scan with
        {
            // A candidate that did not answer may well be where Steam is installed.
            IsPresent = roots.Count > 0 || candidates.Any(candidate => !candidate.IsAnswered) || libraries.Any(library => library.IsManual),

            // A library that did not answer is known only by the path it is listed under, which may not be the one an
            // earlier scan reported it as.
            IsComplete = listsComplete && candidates.All(candidate => candidate.IsAnswered) && reads.All(read => read.IsAnswered),
            Watch = PlanWatches(reads, rootWatches.OfType<WatchedFolder>()),
        };
    }

    public async Task<InstalledGame?> ProbeAsync(InstalledGame game, LibraryScanContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(context);
        var library = await DescribeAsync(new LibraryCandidate(game.LibraryId, IsMounted: null, IsManual: false), context.Access,
            cancellationToken).ConfigureAwait(false);
        var probed = Combine([await ReadLibraryAsync(library, context.Access, cancellationToken).ConfigureAwait(false)]).Describe(game);

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

    private async Task<T> ReadAsync<T>(LibraryAccess access, string path, Func<CancellationToken, Task<T>> read, Func<T> unanswered,
        CancellationToken cancellationToken)
    {
        try
        {
            return await access.ReadAsync(path, read, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.FolderNotAnswering();
            return unanswered();
        }
    }

    private Task<RootCandidate> FindRootAsync(string candidate, LibraryAccess access, CancellationToken cancellationToken) =>
        ReadAsync(access, candidate, _ =>
            {
                var root = _roots.Probe(candidate);
                return Task.FromResult(new RootCandidate(root, IsAnswered: true,
                    WatchedFolder.Toward(_fileSystem, Path.Join(candidate, SteamPaths.SteamApps)),
                    root is null ? null : WatchedFolder.Toward(_fileSystem, Path.Join(root, SteamPaths.Config, SteamPaths.LibraryFoldersFile))));
            },
            () => new RootCandidate(Root: null, IsAnswered: false, Watch: null, ListWatch: null), cancellationToken);

    private async Task<(IReadOnlyList<SteamLibrary> Libraries, bool IsComplete)> LocateLibrariesAsync(IReadOnlyList<string> roots,
        IReadOnlyList<string> manualFolders, LibraryAccess access, CancellationToken cancellationToken)
    {
        var lists = await Task.WhenAll(roots.Concat(manualFolders)
            .Select(listOwner => ReadLibraryListsAsync(listOwner, access, cancellationToken))).ConfigureAwait(false);

        var candidates = new List<LibraryCandidate>();
        void Add(string path, bool? isMounted, bool isManual)
        {
            if (!Path.IsPathFullyQualified(path) || path.Contains('\0', StringComparison.Ordinal))
            {
                _logger.LibraryPathInvalid();
                return;
            }

            candidates.Add(new LibraryCandidate(path, isMounted, isManual));
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

        foreach (var folder in lists.SelectMany(list => list.Folders))
        {
            Add(folder.Path, folder.IsMounted, isManual: false);
        }

        var libraries = await Task.WhenAll(candidates
                .DistinctBy(candidate => PathComparison.Normalize(candidate.Path), PathComparison.Comparer)
                .Select(candidate => DescribeAsync(candidate, access, cancellationToken)))
            .ConfigureAwait(false);
        return ([.. libraries.DistinctBy(library => library.Path, PathComparison.Comparer)], lists.All(list => list.IsReadable));
    }

    private Task<SteamLibrary> DescribeAsync(LibraryCandidate candidate, LibraryAccess access, CancellationToken cancellationToken) =>
        ReadAsync(access, candidate.Path, _ =>
            {
                var canonical = _fileSystem.GetCanonicalPath(candidate.Path);
                var steamApps = SteamPaths.FindSteamApps(_fileSystem, canonical);
                return Task.FromResult(new SteamLibrary(canonical, steamApps ?? Path.Join(canonical, SteamPaths.SteamApps),
                    candidate.IsMounted != false && steamApps is not null, candidate.IsManual));
            },
            () =>
            {
                var path = PathComparison.Normalize(candidate.Path);
                return new SteamLibrary(path, Path.Join(path, SteamPaths.SteamApps), IsOnline: false, candidate.IsManual) { IsAnswered = false };
            },
            cancellationToken);

    private Task<(List<SteamLibraryFolder> Folders, bool IsReadable)> ReadLibraryListsAsync(string listOwner, LibraryAccess access,
        CancellationToken cancellationToken) =>
        ReadAsync(access, listOwner, token => ReadLibraryListsAsync(listOwner, token), () => ([], false), cancellationToken);

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

    private Task<LibraryRead> ReadLibraryAsync(SteamLibrary library, LibraryAccess access, CancellationToken cancellationToken) =>
        library.IsAnswered
            ? ReadAsync(access, library.Path, async token => new LibraryRead(library,
                    library.IsOnline ? await ScanLibraryAsync(library, token).ConfigureAwait(false) : null, PlanWatches(library)),
                () => LibraryRead.Unanswered(library), cancellationToken)
            : Task.FromResult(LibraryRead.Unanswered(library));

    private StoreScan Combine(IReadOnlyList<LibraryRead> reads) => new()
    {
        Store = Store,
        Libraries =
        [
            .. reads.Select(read => new LibraryLocation(read.Library.Path, LibrarySource, read.Library.Path,
                read.Scan is null ? LibraryState.Offline : LibraryState.Online, read.Library.IsManual, read.Scan?.Unreadable.Count ?? 0)),
        ],
        Games = [.. OnePerInstallDirectory(reads.SelectMany(read => read.Scan?.Installations ?? []))],
        UnreadableGames = [.. reads.SelectMany(read => read.Scan?.Unreadable ?? [])],
        RecheckAt = reads.Select(read => read.Scan?.RecheckAt).Min(),
    };

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

        var downloads = SteamDownloads.LastChanges(_fileSystem, library.SteamAppsDirectory);
        var now = _time.GetUtcNow();
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

            var download = SteamDownloads.Classify(downloads.TryGetValue(appId, out var changed) ? changed : null, now);
            if (Evaluate(library, manifest, file.LastWriteTimeUtc, download) is { } installation)
            {
                installations.Add(installation);
            }
        }

        var recheckAt = downloads.Values.Where(changed => SteamDownloads.Classify(changed, now) == SteamDownload.Active)
            .Select(changed => (DateTimeOffset?)(changed + SteamDownloads.ActiveWindow)).Min();
        return new LibraryScan(installations, unreadable, recheckAt);
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
        SteamDownload download)
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
            new SteamLiveSignals(download, IsRegistryUpdating(manifest.AppId)));
        if (status.GameState is not { } resolved)
        {
            return null;
        }

        if (resolved != (GameInstallState.Installed, GameStateReason.None))
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

    private List<WatchedFolder> PlanWatches(SteamLibrary library)
    {
        if (!library.IsOnline)
        {
            // A drive plugged back in, or a folder restored, shows up here.
            return WatchedFolder.Toward(_fileSystem, library.SteamAppsDirectory) is { } toward ? [toward] : [];
        }

        var downloading = Path.Join(library.SteamAppsDirectory, SteamPaths.Downloading);
        return _fileSystem.DirectoryExists(downloading)
            ? [new WatchedFolder(library.SteamAppsDirectory, SteamAppsEntries), new WatchedFolder(downloading, [])]
            : [new WatchedFolder(library.SteamAppsDirectory, SteamAppsEntries)];
    }

    private WatchPlan PlanWatches(IEnumerable<LibraryRead> reads, IEnumerable<WatchedFolder> rootWatches)
    {
        List<WatchedRegistryKey> keys = _environment.Platform == AppPlatform.Windows
            ?
            [
                // SteamPath moves with Steam; the Apps values change when downloads start.
                new(RegistryHive.CurrentUser, RegistryView.Default, SteamRootLocator.SteamKey, IncludeSubkeys: false),
                new(RegistryHive.CurrentUser, RegistryView.Default, $@"{SteamRootLocator.SteamKey}\Apps", IncludeSubkeys: true),
            ]
            : [];
        return new WatchPlan([.. reads.SelectMany(read => read.Watch).Concat(rootWatches).Distinct()], [.. keys]);
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

    /// <summary>A place Steam may be installed to.</summary>
    /// <param name="Root">The canonical root at the candidate, when it holds a <c>steamapps</c> folder.</param>
    /// <param name="IsAnswered">False when the candidate's drive did not answer in time.</param>
    /// <param name="Watch">The way toward the candidate's <c>steamapps</c>, watched while Steam is nowhere to be found.</param>
    /// <param name="ListWatch">The way toward the root's second copy of its library list.</param>
    private sealed record RootCandidate(string? Root, bool IsAnswered, WatchedFolder? Watch, WatchedFolder? ListWatch);

    private sealed record LibraryCandidate(string Path, bool? IsMounted, bool IsManual);

    private sealed record SteamLibrary(string Path, string SteamAppsDirectory, bool IsOnline, bool IsManual)
    {
        /// <summary>False when its drive did not answer in time, so its path is the one listed rather than the canonical one.</summary>
        public bool IsAnswered { get; init; } = true;
    }

    /// <summary>A library as a scan read it.</summary>
    /// <param name="Library">The library read.</param>
    /// <param name="Scan">Null when the library is offline, could not be listed or did not answer.</param>
    /// <param name="Watch">What to watch for changes to the library's records, or for the library coming back.</param>
    private sealed record LibraryRead(SteamLibrary Library, LibraryScan? Scan, IReadOnlyList<WatchedFolder> Watch)
    {
        public bool IsAnswered { get; init; } = true;

        public static LibraryRead Unanswered(SteamLibrary library) => new(library, Scan: null, Watch: []) { IsAnswered = false };
    }

    private sealed record SteamInstallation(InstalledGame Game, SteamAppManifest Manifest);

    /// <summary>What a library that could be listed holds.</summary>
    /// <param name="Installations">The apps found installed, each with its manifest.</param>
    /// <param name="Unreadable">The apps whose manifests could not be read.</param>
    /// <param name="RecheckAt">When the first download that counts as active turns into a leftover.</param>
    private sealed record LibraryScan(List<SteamInstallation> Installations, List<UnreadableGame> Unreadable, DateTimeOffset? RecheckAt);
}
