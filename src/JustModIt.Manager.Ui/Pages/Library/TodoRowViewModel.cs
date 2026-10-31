using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>What a row of the to-do panel asks of the player, in the panel's order: failures first.</summary>
public enum TodoKind
{
    Failure,
    Repair,
    Update,
    Choice,
    Gone,
    Moved,
    Backups,
}

/// <summary>A team that offers a project for a game whose project the player has not chosen.</summary>
/// <param name="Name">The team's name.</param>
/// <param name="Avatar">The team's avatar; empty for none.</param>
/// <param name="Status">The site's status of its project; empty for none.</param>
/// <param name="StatusColor">The site's colour for the status.</param>
public sealed record TeamOffer(string Name, string Avatar, string Status, string StatusColor)
{
    public bool HasStatus => Status.Length > 0;
}

/// <summary>A row of the to-do panel: a game or one of its installs, what it needs, and the action that sees to it.</summary>
/// <param name="key">What the row is about, stable across states, so an updated row keeps its place on screen.</param>
public sealed partial class TodoRowViewModel(string key) : ObservableObject
{
    public string Key { get; } = key;

    public GameKey Game { get; private set; }

    public TodoKind Kind { get; private set; }

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    [ObservableProperty]
    public partial string Cover { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsCoverBlurred { get; private set; }

    [ObservableProperty]
    public partial string Store { get; private set; } = "";

    [ObservableProperty]
    public partial string StoreTip { get; private set; } = "";

    [ObservableProperty]
    public partial LineContent Line { get; private set; } = new(Icons.Info, Tone.Neutral, "");

    /// <summary>The teams to choose among, for a row that asks for a choice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTeams))]
    public partial IReadOnlyList<TeamOffer> Teams { get; private set; } = [];

    public bool HasTeams => Teams.Count > 0;

    [ObservableProperty]
    public partial string ActionText { get; private set; } = "";

    [ObservableProperty]
    public partial Geometry? ActionIcon { get; private set; }

    /// <summary>The action named with its game, for screen readers.</summary>
    [ObservableProperty]
    public partial string ActionName { get; private set; } = "";

    /// <summary>The action removes something, so it is drawn as a quiet red one and asks first.</summary>
    [ObservableProperty]
    public partial bool IsDanger { get; private set; }

    /// <summary>The command the action sends; null for one that opens the game's page instead.</summary>
    public Message? Request { get; private set; }

    /// <summary>The row's part of the panel's batch action: its line in the batch's tooltip; null when it is not part of it.</summary>
    public string? BatchLine { get; private set; }

    /// <summary>The update waits for the connection, so it is left out of the batch action.</summary>
    public bool WaitsForConnection { get; private set; }

    /// <summary>The rows of the to-do panel for the agent's state, in the panel's order.</summary>
    /// <param name="state">The agent's newest state.</param>
    /// <param name="clock">The clock release dates are told against.</param>
    /// <param name="existing">The rows shown now, updated and reused by key.</param>
    public static List<TodoRowViewModel> Build(AppStateView state, TimeProvider clock, IReadOnlyDictionary<string, TodoRowViewModel> existing)
    {
        ArgumentNullException.ThrowIfNull(state);
        var offline = state.Catalogue.Failure is not null;
        var rows = new List<TodoRowViewModel>();
        foreach (var game in state.Games.Where(game => !game.IsHidden && game.Job is null))
        {
            TodoRowViewModel Row(string kind, Guid? install)
            {
                var key = $"{game.Key}|{install}|{kind}";
                var row = existing.TryGetValue(key, out var kept) ? kept : new TodoRowViewModel(key);
                row.Show(game, state);
                rows.Add(row);
                return row;
            }

            if (game.LastFailure is { } failure)
            {
                Row("failure", null).ShowFailure(game, null, failure);
            }

            foreach (var install in game.Installs)
            {
                if (install.LastFailure is { } installFailure)
                {
                    Row("failure", install.Id).ShowFailure(game, install, installFailure);
                    continue;
                }

                switch (install.Status)
                {
                    case { State: MyGameState.NeedsRepair }:
                        Row("repair", install.Id).ShowRepair(game, install);
                        break;
                    case { State: MyGameState.UpdateAvailable }:
                        Row("update", install.Id).ShowUpdate(game, install, clock, offline);
                        break;
                    case { State: MyGameState.Orphaned, Orphan: OrphanReason.GameNotDetected }:
                        Row("gone", install.Id).ShowOrphan(game, install, TodoKind.Gone);
                        break;
                    case { State: MyGameState.Orphaned, Orphan: OrphanReason.GameMoved }:
                        Row("moved", install.Id).ShowOrphan(game, install, TodoKind.Moved);
                        break;
                    case { State: MyGameState.BackupsUnavailable }:
                        Row("backups", install.Id).ShowBackups(game, install);
                        break;
                }
            }

            if (game.Installs.Count == 0 && game.LastFailure is null && game.Project is null && game.Projects.Count > 1)
            {
                Row("choice", null).ShowChoice(game);
            }
        }

        return [.. rows.OrderBy(row => row.Kind).ThenBy(row => row.Name, Text.Comparer)];
    }

    private void Show(GameView game, AppStateView state)
    {
        Game = game.Key;
        Name = game.Name;
        var offer = game.Project ?? game.Installs.Select(install => install.Project).FirstOrDefault(project => project is not null)
            ?? (game.Projects.Count > 0 ? game.Projects[0] : null);
        Cover = offer?.Cover is { Length: > 0 } cover ? cover : game.Installs.Select(install => install.CoverUrl?.AbsoluteUri).FirstOrDefault(url => url is not null) ?? "";
        IsCoverBlurred = AdultContent.IsVeiled(state, game.IsAdult, game.SiteIds);
        Store = game.Key.Store.Value;
        StoreTip = GameWording.DetectedIn(game.Key.Store);
        Teams = [];
        Request = null;
        BatchLine = null;
        WaitsForConnection = false;
        IsDanger = false;
    }

    private void ShowFailure(GameView game, InstallView? install, FailureView failure)
    {
        Kind = TodoKind.Failure;
        Line = new LineContent(Icons.CircleX, Tone.Danger, Text.Format(Strings.Library_StateFailed, ("kind", failure.Kind.ToString())),
            failure.LostPrevious ? Strings.Library_PreviousLost : GameWording.FailureReason(failure.Failure, game.Key.Store, game.Name));
        // Trying again is refused the same way until the player uninstalls and the store checks the game's files, which the
        // game's page leads through.
        Request = failure.Failure?.Reason == JobFailureReason.KeptFilesBlockReplacement ? null : (failure.Kind, install) switch
        {
            (JobKind.Update, { } failed) => new UpdateProject(failed.Id),
            (JobKind.Repair, { } failed) => new RepairProject(failed.Id),
            (JobKind.Uninstall, { } failed) => new UninstallProject(failed.Id),
            (JobKind.Install or JobKind.Update or JobKind.Repair, null) when game.Project is { } project =>
                new InstallProject(game.Key, project.ProjectId),
            _ => null,
        };
        if (Request is null)
        {
            Act(Strings.Library_Open, Icons.ArrowRight, Text.Format(Strings.Library_OpenName, ("game", game.Name)));
        }
        else
        {
            Act(Strings.Library_Retry, Icons.RotateCcw, Text.Format(Strings.Library_RetryName, ("game", game.Name)));
        }
    }

    private void ShowRepair(GameView game, InstallView install)
    {
        Kind = TodoKind.Repair;
        Line = new LineContent(Icons.Wrench, Tone.Warning, Strings.Library_StateRepair,
            Text.Format(Strings.Library_RepairReason, ("reason", install.Status.Repair?.ToString() ?? ""),
                ("store", GameWording.StoreName(game.Key.Store))));
        Request = new RepairProject(install.Id);
        BatchLine = Text.Format(Strings.Library_BatchTipRepair, ("game", game.Name));
        Act(Strings.Library_Repair, Icons.Wrench, Text.Format(Strings.Library_RepairName, ("game", game.Name)));
    }

    private void ShowUpdate(GameView game, InstallView install, TimeProvider clock, bool offline)
    {
        Kind = TodoKind.Update;
        var release = install.Project?.Release;
        var size = install.Status.UpdateSize;
        var detail = GameWording.Join([release is null ? "" : GameWording.Released(release, clock), size is { } bytes ? Formats.FileSize(bytes) : ""]);
        Line = new LineContent(Icons.CircleArrowUp, Tone.Info, Strings.Library_StateUpdate, detail)
        {
            LabelExtra = GameLines.UpdateVersions(install),
            Warning = offline ? Strings.Library_UpdateWaits : null,
        };
        Request = new UpdateProject(install.Id);
        WaitsForConnection = offline;
        var version = GameWording.ShortVersion(GameWording.Version(release));
        BatchLine = Text.Format(offline ? Strings.Library_BatchTipWaiting : Strings.Library_BatchTipUpdate, ("game", game.Name),
            ("version", version));
        Act(Strings.Library_Update, Icons.CircleArrowUp, Text.Format(Strings.Library_UpdateName, ("game", game.Name)));
    }

    private void ShowChoice(GameView game)
    {
        Kind = TodoKind.Choice;
        Line = new LineContent(Icons.Users, Tone.Neutral, Text.Format(Strings.Library_StateChoice, ("count", game.Projects.Count)));
        Teams = [.. game.Projects.Select(project => new TeamOffer(project.Team.Name, project.Team.Avatar, project.Status?.Name ?? "",
            project.Status?.Color ?? ""))];
        Act(Strings.Library_Compare, Icons.Columns, Text.Format(Strings.Library_CompareName, ("game", game.Name)));
    }

    private void ShowOrphan(GameView game, InstallView install, TodoKind kind)
    {
        Kind = kind;
        var store = GameWording.StoreName(game.Key.Store);
        Line = kind == TodoKind.Gone
            ? new LineContent(Icons.TriangleAlert, Tone.Warning, Strings.Library_StateGone, Text.Format(Strings.Library_GoneDetail, ("store", store)))
            : new LineContent(Icons.FolderInput, Tone.Warning, Strings.Library_StateMoved, Text.Format(Strings.Library_MovedDetail, ("store", store)));
        Request = new UninstallProject(install.Id);
        IsDanger = true;
        Act(Strings.Library_Uninstall, Icons.Trash, Text.Format(Strings.Library_UninstallName, ("game", game.Name)));
    }

    private void ShowBackups(GameView game, InstallView install)
    {
        Kind = TodoKind.Backups;
        Line = install.MissingOriginals.Count > 0
            ? new LineContent(Icons.TriangleAlert, Tone.Warning, Strings.Library_StateOriginalsMissing,
                Text.Format(Strings.Library_OriginalsMissingDetail, ("store", GameWording.StoreName(game.Key.Store)),
                    ("storeId", game.Key.Store.Value)))
            : new LineContent(Icons.TriangleAlert, Tone.Warning, Strings.Library_StateBackups,
                Text.Format(Strings.Library_BackupsDetail, ("folder", install.Status.BackupDirectory ?? install.BackupDirectory)));
        Act(Strings.Library_Open, Icons.ArrowRight, Text.Format(Strings.Library_OpenName, ("game", game.Name)));
    }

    private void Act(string text, Geometry icon, string name)
    {
        ActionText = text;
        ActionIcon = icon;
        ActionName = name;
    }
}
