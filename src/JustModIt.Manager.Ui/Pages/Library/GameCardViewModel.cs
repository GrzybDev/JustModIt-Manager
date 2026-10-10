using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>
/// A game's card in the grid of the player's games: its cover, the site's status of its project, its team and store, and
/// one line about it. The page keeps one card per game and updates it, so a new state never replays its entrance.
/// </summary>
/// <param name="key">The game the card shows.</param>
public sealed partial class GameCardViewModel(GameKey key) : ObservableObject
{
    private JobView? _job;

    public GameKey Key { get; } = key;

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    /// <summary>The cover's address; empty shows the placeholder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    public partial string Cover { get; private set; } = "";

    /// <summary>An adult game's cover, veiled while the setting asks for it.</summary>
    [ObservableProperty]
    public partial bool IsCoverBlurred { get; private set; }

    [ObservableProperty]
    public partial bool IsAdult { get; private set; }

    [ObservableProperty]
    public partial string Store { get; private set; } = "";

    [ObservableProperty]
    public partial string StoreTip { get; private set; } = "";

    /// <summary>The team's name, or how many teams offer a project while the player has chosen none.</summary>
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
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    public partial string StatusColor { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsProgress), nameof(IsIndeterminate))]
    public partial LineContent Line { get; private set; } = new(Theme.Icons.Download, Controls.Tone.Neutral, "");

    /// <summary>From 0 to 100 while a job shows how far it is.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string AccessibleName { get; private set; } = "";

    public bool ShowsProgress => Line.IsBusy;

    /// <summary>The job cannot tell how far it is, so its bar moves without a share.</summary>
    public bool IsIndeterminate => Line.Fraction is null;

    public bool HasCover => Cover.Length > 0;

    public bool HasStatus => Status.Length > 0;

    /// <summary>When the newest release of the game's project came out, which orders the grid by the newest.</summary>
    public DateTimeOffset? ReleasedAt { get; private set; }

    /// <summary>The words the search looks in: the game's and the teams' names.</summary>
    public string SearchText { get; private set; } = "";

    /// <summary>Shows the game as the agent's newest state has it.</summary>
    /// <param name="game">The game.</param>
    /// <param name="blurAdult">Whether adult covers are veiled.</param>
    public void Update(GameView game, bool blurAdult)
    {
        ArgumentNullException.ThrowIfNull(game);
        var offer = game.Project ?? game.Installs.Select(install => install.Project).FirstOrDefault(project => project is not null)
            ?? (game.Projects.Count > 0 ? game.Projects[0] : null);
        Name = game.Name;
        Cover = offer?.Cover is { Length: > 0 } cover ? cover : game.Installs.Select(install => install.CoverUrl?.AbsoluteUri).FirstOrDefault(url => url is not null) ?? "";
        IsAdult = game.IsAdult;
        IsCoverBlurred = game.IsAdult && blurAdult;
        Store = game.Key.Store.Value;
        StoreTip = GameWording.DetectedIn(game.Key.Store);
        HasManyTeams = game.Project is null && game.Installs.Count == 0 && game.Projects.Count > 1;
        BadgeName = offer?.Team.Name ?? game.Installs.Select(install => install.TeamName).FirstOrDefault(name => name is not null) ?? "";
        BadgeAvatar = offer?.Team.Avatar ?? "";
        TeamName = HasManyTeams ? Text.Format(Strings.Library_CardTeams, ("count", game.Projects.Count)) : BadgeName;
        OtherBadgeName = HasManyTeams ? game.Projects[1].Team.Name : "";
        OtherBadgeAvatar = HasManyTeams ? game.Projects[1].Team.Avatar : "";
        Status = offer?.Status?.Name ?? "";
        StatusColor = offer?.Status?.Color ?? "";
        OnPropertyChanged(nameof(HasStatus));
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
    /// <param name="store">The game's store, which a wait may name.</param>
    public void Apply(JobView job, StoreId store)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (_job is not { } known || known.Id != job.Id || known.Sequence >= job.Sequence)
        {
            return;
        }

        _job = job;
        Show(GameLines.Job(job, store));
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
