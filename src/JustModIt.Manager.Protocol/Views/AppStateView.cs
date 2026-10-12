using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>
/// Everything the window shows, sent whole with every change: the game list is small, and a whole snapshot cannot leave
/// the window half up to date.
/// </summary>
public sealed record AppStateView
{
    /// <summary>Grows with every snapshot the agent sends.</summary>
    public long Version { get; init; }

    /// <summary>The installs, the saved library and the saved catalogue have been read.</summary>
    public bool IsLoaded { get; init; }

    /// <summary>Sorted by name, hidden games included.</summary>
    public IReadOnlyList<GameView> Games { get; init; } = [];

    public IReadOnlyList<JobView> Jobs { get; init; } = [];

    public SettingsView Settings { get; init; } = SettingsView.From(AppSettings.Default);

    /// <summary>
    /// Until when the player counts as an adult; adult games and their pages ask for the player's age again after it.
    /// </summary>
    public DateTimeOffset? AdultContentConfirmedUntil { get; init; }

    /// <inheritdoc cref="AppSettings.RevealedAdultGames"/>
    public IReadOnlyList<Guid> RevealedAdultGames { get; init; } = [];

    public LibraryView Library { get; init; } = new();

    public CatalogueView Catalogue { get; init; } = new();

    public ActivityView Activity { get; init; } = new();

    public AppUpdateView Update { get; init; } = new();

    /// <summary>Operations a crash interrupted that could not be finished or undone yet.</summary>
    public IReadOnlyList<RecoveredOperation> PendingRecoveries { get; init; } = [];

    /// <inheritdoc cref="AppState.PendingBackupMoves"/>
    public int PendingBackupMoves { get; init; }

    /// <summary>Makes the snapshot of the engine's state and the agent's own status.</summary>
    /// <param name="version">The snapshot's number among those the agent sent.</param>
    /// <param name="state">The engine's newest state.</param>
    /// <param name="settings">The current settings.</param>
    /// <param name="site">What the site says about itself.</param>
    /// <param name="activity">What the agent is busy with beyond the jobs.</param>
    /// <param name="update">How the app's own update stands.</param>
    public static AppStateView From(long version, AppState state, AppSettings settings, SiteConfiguration site, ActivityView activity,
        AppUpdateView update)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(update);
        return new AppStateView
        {
            Version = version,
            IsLoaded = state.IsLoaded,
            Games = [.. state.Games.Select(GameView.From)],
            Jobs = [.. state.Jobs.Select(JobView.From)],
            Settings = SettingsView.From(settings),
            AdultContentConfirmedUntil = settings.AdultContentConfirmedUntil,
            RevealedAdultGames = [.. settings.RevealedAdultGames],
            Library = LibraryView.From(state, settings),
            Catalogue = new CatalogueView
            {
                State = state.CatalogueState,
                FetchedAt = state.CatalogueFetchedAt,
                Failure = state.CatalogueFailure,
                SiteName = site.Name,
                IsUnderMaintenance = site.IsUnderMaintenance,
                ReleaseFiles = site.ReleaseFiles,
            },
            Activity = activity,
            Update = update,
            PendingRecoveries = state.PendingRecoveries,
            PendingBackupMoves = state.PendingBackupMoves,
        };
    }
}

/// <summary>How fresh the catalogue is, and what the site says about itself.</summary>
public sealed record CatalogueView
{
    public CatalogueState State { get; init; }

    /// <summary>When the catalogue was last read from the site; null before the first time.</summary>
    public DateTimeOffset? FetchedAt { get; init; }

    /// <summary>Why the last refresh failed; null after a successful one.</summary>
    public CatalogueErrorReason? Failure { get; init; }

    /// <summary>The site's own name; empty until it was read.</summary>
    public string SiteName { get; init; } = "";

    public bool IsUnderMaintenance { get; init; }

    /// <summary>What the site takes as release files, which the package creator checks a package against.</summary>
    public ReleaseFileRules ReleaseFiles { get; init; } = new();
}

/// <summary>What the agent is busy with beyond the jobs.</summary>
public sealed record ActivityView
{
    /// <summary>A check the player asked for runs: the stores are scanned and the catalogue is read again.</summary>
    public bool IsChecking { get; init; }

    /// <summary>The app exits once the running jobs are done, and starts no new ones.</summary>
    public bool IsExitPending { get; init; }
}

public enum AppUpdateState
{
    /// <summary>This copy cannot update itself, e.g. a development build or one without an update feed.</summary>
    Unavailable,
    Idle,
    Checking,
    UpToDate,

    /// <summary>A newer version exists; the player decides when to download it, since automatic updates are off.</summary>
    Available,
    Downloading,

    /// <summary>Downloaded; it is applied when nothing needs the app, or when the player restarts it.</summary>
    Ready,
    Failed,
}

/// <summary>The app's own updates.</summary>
public sealed record AppUpdateView
{
    public AppUpdateState State { get; init; }

    public string CurrentVersion { get; init; } = "";

    /// <summary>The version found, being downloaded or ready.</summary>
    public string? Version { get; init; }

    /// <summary>From 0 to 100 while downloading.</summary>
    public int? Progress { get; init; }

    public DateTimeOffset? CheckedAt { get; init; }
}
