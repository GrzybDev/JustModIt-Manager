using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>What the store integrations found, in short; <see cref="DetectionReportView"/> has the whole report.</summary>
public sealed record LibraryView
{
    /// <summary>The games are the ones saved at the last run until the first scan finishes.</summary>
    public bool IsCached { get; init; }

    public DateTimeOffset? ScannedAt { get; init; }

    public IReadOnlyList<StoreView> Stores { get; init; } = [];

    /// <summary>Folders the player added because detection did not find a game.</summary>
    public IReadOnlyList<LibraryFolderView> AddedFolders { get; init; } = [];

    /// <summary>Sums up what each store integration found, with the folders the player added.</summary>
    /// <param name="state">The engine's newest state.</param>
    /// <param name="settings">The settings that hold the added folders.</param>
    public static LibraryView From(AppState state, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);
        return new LibraryView
        {
            IsCached = state.IsLibraryCached,
            ScannedAt = state.LibraryScannedAt,
            Stores =
            [
                .. state.Stores.Select(store => new StoreView(store.Store.Value)
                {
                    IsFound = store.IsPresent,
                    ScanFailed = store.ScanFailed,
                    IsComplete = store.IsComplete,
                    LibraryCount = store.Libraries.Count,
                    OfflineLibraryCount = store.Libraries.Count(library => library.State == LibraryState.Offline),
                    HasProblems = !store.IsFullyRead,
                }),
            ],
            AddedFolders = [.. settings.LibraryFolders.Select(folder => new LibraryFolderView(folder.Store.Value, folder.Path))],
        };
    }
}

/// <summary>A store the app reads games from, as the last scan found it.</summary>
/// <param name="Store">The store's id, e.g. <c>steam</c>.</param>
public sealed record StoreView(string Store)
{
    /// <summary>The store's client, its data or a folder the player added for it exists.</summary>
    public bool IsFound { get; init; }

    public bool ScanFailed { get; init; }

    /// <summary>False when the store's list of libraries could not be read, or a library on it did not answer.</summary>
    public bool IsComplete { get; init; }

    public int LibraryCount { get; init; }

    public int OfflineLibraryCount { get; init; }

    /// <summary>Something could not be read or is offline, which may be why a game is missing.</summary>
    public bool HasProblems { get; init; }
}

public sealed record LibraryFolderView(string Store, string Path);

/// <summary>A project the site has for a game, by the address of its page.</summary>
/// <param name="ProjectId">The project, whose id stays when its slugs change.</param>
/// <param name="GameSlug">The game's slug in the site's addresses.</param>
/// <param name="TeamSlug">The team's slug in the site's addresses.</param>
public sealed record SiteProjectView(Guid ProjectId, string GameSlug, string TeamSlug);

/// <summary>A game a store reported, with the site's cover of it and the site's projects for it.</summary>
/// <param name="Key">The game's store and id there.</param>
/// <param name="Name">As the store names it.</param>
/// <param name="Game">What the store reports about it.</param>
public sealed record DetectedGameEntry(GameKey Key, string Name, DetectedGameView Game)
{
    /// <summary>The cover of the site's game matched to it, as an absolute address; empty when the site has none.</summary>
    public string Cover { get; init; } = "";

    /// <summary>The site's projects for the game, whatever their status.</summary>
    public IReadOnlyList<SiteProjectView> Projects { get; init; } = [];

    /// <summary>Maps a game of the library's snapshot.</summary>
    /// <param name="game">The game as its store reports it.</param>
    /// <param name="index">The site's projects matched to the detected games, which give the cover and the projects.</param>
    public static DetectedGameEntry From(InstalledGame game, InstallableIndex index)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(index);
        var projects = index.Find(game.Key);
        return new DetectedGameEntry(game.Key, game.Name, DetectedGameView.From(game))
        {
            Cover = projects.Select(entry => entry.Game.Cover).FirstOrDefault(cover => cover.Length > 0) ?? "",
            Projects = [.. projects.Select(entry => new SiteProjectView(entry.ProjectUuid, entry.Game.Slug, entry.Team.Slug))],
        };
    }
}

/// <summary>What detection found on this machine, store by store, for a player whose game is missing.</summary>
/// <param name="Stores">What each store integration found.</param>
/// <param name="ScannedAt">When the stores were last scanned; null before the first scan.</param>
public sealed record DetectionReportView(IReadOnlyList<StoreDetectionView> Stores, DateTimeOffset? ScannedAt = null)
{
    /// <summary>Maps the library's detection report, with the site's names and tips for its stores.</summary>
    /// <param name="report">The report of the last scan.</param>
    /// <param name="storefronts">The site's stores in its order; each of the app's stores takes the first of its kind.</param>
    public static DetectionReportView From(DetectionReport report, IReadOnlyList<StorefrontPlatform> storefronts)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(storefronts);
        return new DetectionReportView(
            [.. report.Stores.Select(store => StoreDetectionView.From(store, storefronts.FirstOrDefault(site => site.Store == store.Store)))],
            report.ScannedAt);
    }
}

public sealed record StoreDetectionView(string Store)
{
    /// <summary>The store's name on the site; empty when the site lists no store of this kind.</summary>
    public string Name { get; init; } = "";

    /// <inheritdoc cref="StorefrontPlatform.Launcher"/>
    public string Launcher { get; init; } = "";

    /// <summary>
    /// Markdown in the site's language: how players find the folder of a game they have from this store; empty when the
    /// site gives no tip.
    /// </summary>
    public string InstallHint { get; init; } = "";

    public bool IsFound { get; init; }

    public bool ScanFailed { get; init; }

    public bool IsComplete { get; init; }

    public int GameCount { get; init; }

    public IReadOnlyList<LibraryDetectionView> Libraries { get; init; } = [];

    /// <summary>Maps what detection found for one store.</summary>
    /// <param name="store">The store's part of the report.</param>
    /// <param name="site">The site's store of the same kind, if it has one.</param>
    public static StoreDetectionView From(StoreDetection store, StorefrontPlatform? site)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new StoreDetectionView(store.Store.Value)
        {
            Name = site?.Name ?? "",
            Launcher = site?.Launcher ?? "",
            InstallHint = site?.InstallHint ?? "",
            IsFound = store.IsFound,
            ScanFailed = store.ScanFailed,
            IsComplete = store.IsComplete,
            GameCount = store.GameCount,
            Libraries =
            [
                .. store.Libraries.Select(library => new LibraryDetectionView(library.Library.Id, library.Library.Source)
                {
                    Path = library.Library.Path,
                    State = library.Library.State,
                    IsAdded = library.Library.IsManual,
                    UnreadableEntries = library.Library.UnreadableEntries,
                    GameCount = library.GameCount,
                }),
            ],
        };
    }
}

/// <summary>A library a store integration found.</summary>
/// <param name="Id">The library's id, stable across scans.</param>
/// <param name="Source">Which kind of library this is, as the store integration names it, e.g. <c>steam-library</c>.</param>
public sealed record LibraryDetectionView(string Id, string Source)
{
    /// <summary>The folder or file to show; null for the registry.</summary>
    public string? Path { get; init; }

    public LibraryState State { get; init; }

    /// <summary>The player added it.</summary>
    public bool IsAdded { get; init; }

    public int UnreadableEntries { get; init; }

    public int GameCount { get; init; }
}
