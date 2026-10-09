using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>A game in the player's list, with everything its card and page show.</summary>
public sealed record GameView
{
    public required GameKey Key { get; init; }

    public required string Name { get; init; }

    public required GameStatusView Status { get; init; }

    /// <summary>What its store reports; null when no store lists the game any more.</summary>
    public DetectedGameView? Detected { get; init; }

    /// <summary>The projects the player may choose for a new install: completed, with a package that fits the game.</summary>
    public IReadOnlyList<ProjectOfferView> Projects { get; init; } = [];

    /// <summary>
    /// The installed project, also once it is no longer on offer; otherwise the chosen or only one on offer. Null while
    /// several teams offer one and the player has not chosen.
    /// </summary>
    public ProjectOfferView? Project { get; init; }

    public InstallView? Installed { get; init; }

    public GamePreferences Preferences { get; init; } = GamePreferences.Default;

    public JobView? Job { get; init; }

    /// <summary>The store already offers the game in the app's target language, so the app never changes it on its own.</summary>
    public bool OffersTargetLanguage { get; init; }

    public FailureView? LastFailure { get; init; }

    /// <summary>What runs from the game's folder, when the app happens to know.</summary>
    public IReadOnlyList<string> RunningProcesses { get; init; } = [];

    public bool IsHidden { get; init; }

    public bool IsAdult { get; init; }

    /// <inheritdoc cref="MyGame.NeedsAttention"/>
    public bool NeedsAttention { get; init; }

    /// <summary>Maps a game of the player's list.</summary>
    /// <param name="game">The game as the engine's state holds it.</param>
    public static GameView From(MyGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return new GameView
        {
            Key = game.Key,
            Name = game.Name,
            Status = GameStatusView.From(game.Status),
            Detected = game.Game is { } detected ? DetectedGameView.From(detected) : null,
            Projects = [.. game.Projects.Select(ProjectOfferView.From)],
            Project = game.Project is { } project ? ProjectOfferView.From(project) : null,
            Installed = game.Installed is { } record ? InstallView.From(record) : null,
            Preferences = game.Preferences,
            Job = game.Job is { } job ? JobView.From(job) : null,
            OffersTargetLanguage = game.OffersTargetLanguage,
            LastFailure = game.LastFailure is { } failure ? FailureView.From(failure) : null,
            RunningProcesses = [.. game.RunningProcesses],
            IsHidden = game.IsHidden,
            IsAdult = game.IsAdult,
            NeedsAttention = game.NeedsAttention,
        };
    }
}

/// <summary>What a game in the player's list is, and why, for the app to say in its own words.</summary>
/// <param name="State">The status; the other properties give its reasons.</param>
public sealed record GameStatusView(MyGameState State)
{
    public GameInstallState? GameState { get; init; }

    public GameStateReason? StateReason { get; init; }

    public RepairReason? Repair { get; init; }

    public OrphanReason? Orphan { get; init; }

    public UpdateState? Update { get; init; }

    public UpdateKind? UpdateKind { get; init; }

    /// <summary>The variant an update installs.</summary>
    public string? UpdateVariant { get; init; }

    /// <summary>The variant the player chose is gone, so the update installs the recommended one.</summary>
    public bool UpdateVariantFellBack { get; init; }

    /// <summary>The folder that should hold the originals, while it is unavailable.</summary>
    public string? BackupDirectory { get; init; }

    /// <summary>Maps a status with its reasons.</summary>
    /// <param name="status">The status of a game or of an install on it.</param>
    public static GameStatusView From(MyGameStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new GameStatusView(status.State)
        {
            GameState = status.GameState,
            StateReason = status.StateReason,
            Repair = status.Repair,
            Orphan = status.Orphan,
            Update = status.Update?.State,
            UpdateKind = status.Update?.Kind,
            UpdateVariant = status.Update?.Variant?.Name,
            UpdateVariantFellBack = status.Update?.VariantFellBack ?? false,
            BackupDirectory = status.BackupDirectory,
        };
    }
}

/// <summary>A game as its store reports it.</summary>
public sealed record DetectedGameView
{
    public required string InstallDirectory { get; init; }

    public required string LibraryId { get; init; }

    public string? BuildId { get; init; }

    public GameInstallState State { get; init; }

    public GameStateReason Reason { get; init; }

    public OperatingSystemKind Platform { get; init; }

    /// <summary>Kept from an earlier scan because the store's records could not be read this time.</summary>
    public bool IsStale { get; init; }

    public bool CanLaunch { get; init; }

    /// <summary>Maps a game as its store reports it.</summary>
    /// <param name="game">The game from the library's snapshot.</param>
    public static DetectedGameView From(InstalledGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return new DetectedGameView
        {
            InstallDirectory = game.InstallDirectory,
            LibraryId = game.LibraryId,
            BuildId = game.BuildId,
            State = game.State,
            Reason = game.Reason,
            Platform = game.Platform,
            IsStale = game.IsStale,
            CanLaunch = game.Launch is not null,
        };
    }
}
