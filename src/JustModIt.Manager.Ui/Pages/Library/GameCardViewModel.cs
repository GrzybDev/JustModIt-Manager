using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>What a game's card and the to-do rows about a game show of it; the store only where the game is the player's.</summary>
public abstract partial class GameItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; protected set; } = "";

    /// <summary>The cover's address; empty shows the placeholder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    public partial string Cover { get; protected set; } = "";

    /// <summary>An adult game's cover, veiled while the setting asks for it.</summary>
    [ObservableProperty]
    public partial bool IsCoverBlurred { get; protected set; }

    [ObservableProperty]
    public partial string Store { get; private set; } = "";

    [ObservableProperty]
    public partial string StoreTip { get; private set; } = "";

    public bool HasCover => Cover.Length > 0;

    /// <summary>Shows the name, cover and store of one of the player's games.</summary>
    /// <param name="game">The game.</param>
    /// <param name="state">The agent's state, which tells whether an adult cover is veiled.</param>
    /// <returns>The project that speaks for the game; null without one.</returns>
    protected ProjectOfferView? ShowGame(GameView game, AppStateView state)
    {
        var offer = game.Project ?? game.Installs.Select(install => install.Project).FirstOrDefault(project => project is not null)
            ?? (game.Projects.Count > 0 ? game.Projects[0] : null);
        Name = game.Name;
        Cover = offer?.Cover is { Length: > 0 } cover ? cover : game.Installs.Select(install => install.CoverUrl?.AbsoluteUri).FirstOrDefault(url => url is not null) ?? "";
        IsCoverBlurred = AdultContent.IsVeiled(state, game.IsAdult, game.SiteIds);
        Store = game.Key.Store.Value;
        StoreTip = Text.Format(Strings.Library_DetectedIn, ("store", GameWording.StoreName(game.Key.Store)));
        return offer;
    }
}

/// <summary>What the cards of both grids show of a game: its project's status and the teams behind it.</summary>
public abstract partial class GameCardBaseViewModel : GameItemViewModel
{
    [ObservableProperty]
    public partial bool IsAdult { get; protected set; }

    /// <summary>The team's name, or how many teams or projects there are to choose from.</summary>
    [ObservableProperty]
    public partial string TeamName { get; private set; } = "";

    /// <summary>The name of the team whose mark the card shows first.</summary>
    [ObservableProperty]
    public partial string BadgeName { get; private set; } = "";

    [ObservableProperty]
    public partial string BadgeAvatar { get; private set; } = "";

    /// <summary>Several teams offer a project and none is chosen, so the card shows two marks and their count.</summary>
    [ObservableProperty]
    public partial bool HasManyTeams { get; private set; }

    [ObservableProperty]
    public partial string OtherBadgeName { get; private set; } = "";

    [ObservableProperty]
    public partial string OtherBadgeAvatar { get; private set; } = "";

    /// <summary>The site's status of the project; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    public partial string StatusColor { get; private set; } = "";

    [ObservableProperty]
    public partial string AccessibleName { get; protected set; } = "";

    public bool HasStatus => Status.Length > 0;

    /// <summary>Shows the project's status and the teams' marks.</summary>
    /// <param name="status">The project's status on the site; null for none.</param>
    /// <param name="name">The first team's name.</param>
    /// <param name="avatar">The first team's avatar.</param>
    /// <param name="other">The second team's mark, while there are several to choose from; null for one team.</param>
    /// <param name="severalText">What the card says instead of the team's name while there are several.</param>
    protected void ShowTeams(ProjectStatusView? status, string name, string avatar, TeamView? other, string severalText)
    {
        Status = status?.Name ?? "";
        StatusColor = status?.Color ?? "";
        HasManyTeams = other is not null;
        BadgeName = name;
        BadgeAvatar = avatar;
        TeamName = other is null ? name : severalText;
        OtherBadgeName = other?.Name ?? "";
        OtherBadgeAvatar = other?.Avatar ?? "";
    }
}

/// <summary>
/// A game's card in the grid of the player's games: its cover, the site's status of its project, its team and store, and
/// one line about it. The page keeps one card per game and updates it, so a new state never replays its entrance.
/// </summary>
/// <param name="key">The game the card shows.</param>
public sealed partial class GameCardViewModel(GameKey key) : GameCardBaseViewModel
{
    private JobView? _job;

    public GameKey Key { get; } = key;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsProgress), nameof(IsIndeterminate))]
    public partial LineContent Line { get; private set; } = new(Theme.Icons.Download, Controls.Tone.Neutral, "");

    /// <summary>From 0 to 100 while a job shows how far it is.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    public bool ShowsProgress => Line.IsBusy;

    /// <summary>The job cannot tell how far it is, so its bar moves without a share.</summary>
    public bool IsIndeterminate => Line.Fraction is null;

    /// <summary>When the newest release of the game's project came out, which orders the grid by the newest.</summary>
    public DateTimeOffset? ReleasedAt { get; private set; }

    /// <summary>The words the search looks in: the game's and the teams' names.</summary>
    public string SearchText { get; private set; } = "";

    /// <summary>Shows the game as the agent's newest state has it.</summary>
    /// <param name="game">The game.</param>
    /// <param name="state">The agent's state, which tells whether an adult cover is veiled.</param>
    public void Update(GameView game, AppStateView state)
    {
        var offer = ShowGame(game, state);
        var teamName = offer?.Team.Name ?? game.Installs.Select(install => install.TeamName).FirstOrDefault(name => name is not null) ?? "";
        var otherTeam = game.Project is null && game.Installs.Count == 0 && game.Projects.Count > 1 ? game.Projects[1].Team : null;
        IsAdult = game.IsAdult;
        ShowTeams(offer?.Status, teamName, offer?.Team.Avatar ?? "", otherTeam,
            Text.Format(Strings.Library_CardTeams, ("count", game.Projects.Count)));
        IEnumerable<ProjectOfferView?> offers = [game.Project, .. game.Projects, .. game.Installs.Select(install => install.Project)];
        ReleasedAt = offers.Max(project => project?.Release?.Date);
        string[] words = [game.Name, .. game.Projects.Select(project => project.Team.Name), .. game.Installs.Select(install => install.TeamName ?? "")];
        SearchText = string.Join(' ', words);
        _job = game.Job;
        Show(GameLines.Card(game));
    }

    /// <summary>
    /// Moves the card's job on, touching only what the job changes; a report of another job, or an older one, changes nothing.
    /// </summary>
    /// <param name="job">A newer snapshot of the job the card shows.</param>
    public void Apply(JobView job)
    {
        if (_job is not { } known || known.Id != job.Id || known.Sequence >= job.Sequence)
        {
            return;
        }

        _job = job;
        Show(GameLines.Job(job));
    }

    private void Show(LineContent line)
    {
        Line = line;
        Progress = Math.Clamp(line.Fraction ?? 0, 0, 1) * 100;
        AccessibleName = Text.Format(Strings.Library_CardName, ("game", Name), ("state",
            string.Join(", ", new[] { line.Label, line.LabelExtra, line.Detail }.Where(part => !string.IsNullOrEmpty(part)))));
    }
}

/// <summary>The tile at the end of the grid that leads to what detection found, for a player whose game is missing.</summary>
public sealed class HelpTileViewModel;
