using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Core.State;

/// <summary>Everything the window shows as of one moment, immutable so any thread may hold it.</summary>
public sealed record AppState
{
    /// <summary>Before the local data was read; the window shows that it is loading rather than an empty list.</summary>
    public static AppState Loading { get; } = new();

    /// <summary>Grows with every published state.</summary>
    public long Version { get; init; }

    /// <summary>The installs, the saved library and the saved catalogue have been read.</summary>
    public bool IsLoaded { get; init; }

    /// <summary>Sorted by name, hidden games included.</summary>
    public ValueList<MyGame> Games { get; init; }

    public ValueList<JobSnapshot> Jobs { get; init; }

    /// <summary>What each store integration found, for telling the player which stores are there.</summary>
    public ValueList<StoreStatus> Stores { get; init; }

    /// <summary>The games are the ones saved at the last run until the first scan finishes.</summary>
    public bool IsLibraryCached { get; init; }

    public DateTimeOffset? LibraryScannedAt { get; init; }

    public CatalogueState CatalogueState { get; init; }

    public DateTimeOffset? CatalogueFetchedAt { get; init; }

    /// <summary>Why the last catalogue refresh failed; null after a successful one.</summary>
    public CatalogueErrorReason? CatalogueFailure { get; init; }

    /// <summary>Operations a crash interrupted that could not be finished or undone yet; their games stay untouched.</summary>
    public ValueList<RecoveredOperation> PendingRecoveries { get; init; }

    /// <summary>The game in the player's list; null when it is not there.</summary>
    /// <param name="game">The game's store and id.</param>
    public MyGame? Find(GameKey game) => Games.FirstOrDefault(candidate => candidate.Key == game);
}
