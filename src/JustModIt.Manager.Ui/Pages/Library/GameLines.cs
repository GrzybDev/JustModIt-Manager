using Avalonia.Media;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>What a status line about a game says: its icon and tone, a label and what follows it.</summary>
/// <param name="Icon">The icon before the line.</param>
/// <param name="Tone">The colour of the icon and the label.</param>
/// <param name="Label">The state in a few words.</param>
/// <param name="Detail">More about it; null for nothing more.</param>
public sealed record LineContent(Geometry Icon, Tone Tone, string Label, string? Detail = null)
{
    /// <summary>Words after the label in its colour, such as the versions of an update.</summary>
    public string? LabelExtra { get; init; }

    /// <summary>A closing part in the warning colour.</summary>
    public string? Warning { get; init; }

    /// <summary>Work is under way, so the icon turns.</summary>
    public bool IsBusy { get; init; }

    /// <summary>A job's share done from 0 to 1, or null while it cannot tell; only for <see cref="IsBusy"/> lines.</summary>
    public double? Fraction { get; init; }
}

/// <summary>The status lines of the player's games: the one a card shows, and the one of a job under way.</summary>
internal static class GameLines
{
    /// <summary>The line of a job: queued, waiting and for what, or its stage and how far it is.</summary>
    public static LineContent Job(JobView job, StoreId store) => job.Stage switch
    {
        JobStage.Queued => new LineContent(Icons.Clock, Tone.Accent, Strings.Library_JobQueued),
        JobStage.Waiting => job.WaitReason switch
        {
            JobWaitReason.GameRunning => new LineContent(Icons.Hourglass, Tone.Accent, Strings.Library_JobWaitingForGame,
                Text.Format(Strings.Library_JobThen, ("kind", job.Kind.ToString()))),
            JobWaitReason.StoreBusy or JobWaitReason.QuietPeriod => new LineContent(Icons.Hourglass, Tone.Accent,
                Text.Format(Strings.Library_JobWaitingForStore, ("store", GameWording.StoreName(store))),
                Text.Format(Strings.Library_JobThen, ("kind", job.Kind.ToString()))),
            JobWaitReason.Recovery => new LineContent(Icons.Hourglass, Tone.Accent, Strings.Library_JobWaitingForRecovery),
            _ => new LineContent(Icons.Clock, Tone.Accent, Strings.Library_JobQueued),
        },
        _ => new LineContent(Icons.LoaderCircle, Tone.Accent, GameWording.Percent(job) is { } percent
            ? Text.Format(Strings.Library_JobProgress, ("stage", GameWording.Stage(job)), ("percent", percent))
            : Text.Format(Strings.Library_JobUnknownProgress, ("stage", GameWording.Stage(job))))
        {
            IsBusy = true,
            Fraction = job.Fraction,
        },
    };

    /// <summary>The line at the foot of a game's card: its job, or what it is when nothing runs.</summary>
    public static LineContent Card(GameView game)
    {
        if (game.Job is { } job)
        {
            return Job(job, game.Key.Store);
        }

        if (game.IsHidden)
        {
            return new LineContent(Icons.EyeOff, Tone.Neutral, Strings.Library_CardHidden);
        }

        var install = game.Installs.Count > 0 ? game.Installs[0] : null;
        return game.Status.State switch
        {
            MyGameState.Installed when install is not null => Installed(install),
            MyGameState.UpdateAvailable when install is not null => new LineContent(Icons.CircleArrowUp, Tone.Info, Strings.Library_StateUpdate)
            {
                LabelExtra = UpdateVersions(install),
            },
            MyGameState.NeedsRepair => new LineContent(Icons.Wrench, Tone.Warning, Strings.Library_StateRepair),
            MyGameState.GameNotReady => new LineContent(Icons.Clock, Tone.Neutral, Strings.Library_CardNotReady, NotReady(game)),
            MyGameState.Orphaned => Orphaned(game.Status),
            MyGameState.BackupsUnavailable => new LineContent(Icons.TriangleAlert, Tone.Warning,
                game.Installs.Any(each => each.Status.State == MyGameState.BackupsUnavailable && each.MissingOriginals.Count > 0)
                    ? Strings.Library_StateOriginalsMissing
                    : Strings.Library_StateBackups),
            MyGameState.TestInstalled => new LineContent(Icons.Package, Tone.Neutral, Strings.Library_CardTest),
            MyGameState.Busy => new LineContent(Icons.Clock, Tone.Accent, Strings.Library_JobQueued),
            _ when game.Project is null && game.Projects.Count > 1 => new LineContent(Icons.Users, Tone.Neutral,
                Text.Format(Strings.Library_StateChoice, ("count", game.Projects.Count))),
            _ => new LineContent(Icons.Download, Tone.Neutral, Strings.Library_CardReady),
        };
    }

    /// <summary>What the store does with a game that is not ready, from the game's status or what the store reports.</summary>
    public static string NotReady(GameView game) =>
        game.Status.GameState is { } state
            ? GameWording.GameState(state, game.Status.StateReason ?? GameStateReason.None, game.Key.Store)
            : game.Detected is { } detected
                ? GameWording.GameState(detected.State, detected.Reason, game.Key.Store)
                : "";

    /// <summary>The versions an update goes between, e.g. "v1 → v2".</summary>
    public static string UpdateVersions(InstallView install)
    {
        var from = GameWording.ShortVersion(GameWording.Version(install));
        var to = GameWording.ShortVersion(GameWording.Version(install.Project?.Release));
        return from.Length > 0 && to.Length > 0 ? Text.Format(Strings.Library_UpdateVersions, ("from", from), ("to", to)) : to;
    }

    private static LineContent Installed(InstallView install)
    {
        var version = GameWording.ShortVersion(GameWording.Version(install));
        return new LineContent(Icons.Check, Tone.Success, version.Length > 0
            ? Text.Format(Strings.Library_CardInstalledVersion, ("version", version))
            : Strings.Library_CardInstalled);
    }

    private static LineContent Orphaned(GameStatusView status) => status.Orphan switch
    {
        OrphanReason.GameNotDetected => new LineContent(Icons.TriangleAlert, Tone.Warning, Strings.Library_StateGone),
        OrphanReason.GameMoved => new LineContent(Icons.FolderInput, Tone.Warning, Strings.Library_StateMoved),
        _ => new LineContent(Icons.Unlink, Tone.Neutral, Strings.Library_StateNotInCatalogue),
    };
}
