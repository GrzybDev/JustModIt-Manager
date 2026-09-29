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

    /// <summary>False when the store's list of libraries could not be read.</summary>
    public bool IsComplete { get; init; }

    public int LibraryCount { get; init; }

    public int OfflineLibraryCount { get; init; }

    /// <summary>Something could not be read or is offline, which may be why a game is missing.</summary>
    public bool HasProblems { get; init; }
}

public sealed record LibraryFolderView(string Store, string Path);

/// <summary>What detection found on this machine, store by store, for a player whose game is missing.</summary>
/// <param name="Stores">What each store integration found.</param>
/// <param name="ScannedAt">When the stores were last scanned; null before the first scan.</param>
public sealed record DetectionReportView(IReadOnlyList<StoreDetectionView> Stores, DateTimeOffset? ScannedAt = null)
{
    public static DetectionReportView From(DetectionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new DetectionReportView(
            [
                .. report.Stores.Select(store => new StoreDetectionView(store.Store.Value)
                {
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
                }),
            ],
            report.ScannedAt);
    }
}

public sealed record StoreDetectionView(string Store)
{
    public bool IsFound { get; init; }

    public bool ScanFailed { get; init; }

    public bool IsComplete { get; init; }

    public int GameCount { get; init; }

    public IReadOnlyList<LibraryDetectionView> Libraries { get; init; } = [];
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
