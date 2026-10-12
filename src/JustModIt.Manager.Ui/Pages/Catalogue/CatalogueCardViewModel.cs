using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Catalogue;

/// <summary>
/// A game's card in the catalogue: its cover, the status of its best project, its team or teams, when its project last
/// moved, and a bar that leads to the game's page in the player's games when it is on this computer. The page keeps one
/// card per game, so a new answer never replays its entrance.
/// </summary>
/// <param name="id">The game's id on the site.</param>
public sealed partial class CatalogueCardViewModel(Guid id) : ObservableObject
{
    public Guid Id { get; } = id;

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    /// <summary>The player's search, which marks what it matched in the name and the team.</summary>
    [ObservableProperty]
    public partial string Query { get; private set; } = "";

    [ObservableProperty]
    public partial string Cover { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsAdult { get; private set; }

    [ObservableProperty]
    public partial bool IsCoverBlurred { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    public partial string StatusColor { get; private set; } = "";

    /// <summary>The team's name, or how many projects several teams made.</summary>
    [ObservableProperty]
    public partial string TeamName { get; private set; } = "";

    [ObservableProperty]
    public partial string BadgeName { get; private set; } = "";

    [ObservableProperty]
    public partial string BadgeAvatar { get; private set; } = "";

    [ObservableProperty]
    public partial bool HasManyTeams { get; private set; }

    [ObservableProperty]
    public partial string OtherBadgeName { get; private set; } = "";

    [ObservableProperty]
    public partial string OtherBadgeAvatar { get; private set; } = "";

    /// <summary>When the project came out, was announced or last changed; empty while it shows progress instead.</summary>
    [ObservableProperty]
    public partial string Moved { get; private set; } = "";

    [ObservableProperty]
    public partial bool ShowsProgress { get; private set; }

    /// <summary>From 0 to 100.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = "";

    /// <summary>The game is on this computer.</summary>
    [ObservableProperty]
    public partial bool IsDetected { get; private set; }

    /// <summary>The game of the player's games that the bar opens; null while the game is not among them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpensGame), nameof(IsDetectedOnly))]
    public partial GameKey? PlayerGame { get; private set; }

    /// <summary>The game is on this computer but waits for a project to come out, which the bar says.</summary>
    [ObservableProperty]
    public partial bool AwaitsProject { get; private set; }

    [ObservableProperty]
    public partial string AccessibleName { get; private set; } = "";

    [ObservableProperty]
    public partial string BarName { get; private set; } = "";

    /// <summary>Where the card leads: the project's page, or the game's comparison of several.</summary>
    public Route Route { get; private set; } = new CatalogueRoute();

    public bool HasStatus => Status.Length > 0;

    public bool OpensGame => IsDetected && PlayerGame is not null;

    public bool IsDetectedOnly => IsDetected && PlayerGame is null;

    /// <summary>Shows the game as the catalogue's newest answer has it.</summary>
    /// <param name="game">The game with the projects its card shows.</param>
    /// <param name="query">The player's search.</param>
    /// <param name="state">The agent's state, which tells the player's games, the blur setting and the games unveiled.</param>
    /// <param name="clock">The clock ages are told against.</param>
    public void Update(CatalogueGameView game, string query, AppStateView state, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(clock);
        var projects = Best(game.Projects);
        var best = projects.FirstOrDefault();
        Name = game.Name;
        Query = query;
        Cover = game.Cover;
        IsAdult = game.IsAdult;
        IsCoverBlurred = AdultContent.IsVeiled(state, game.IsAdult, [game.Id]);
        Status = best?.Status?.Name ?? "";
        StatusColor = best?.Status?.Color ?? "";
        HasManyTeams = projects.Count > 1;
        BadgeName = best?.Team.Name ?? "";
        BadgeAvatar = best?.Team.Avatar ?? "";
        OtherBadgeName = HasManyTeams ? projects[1].Team.Name : "";
        OtherBadgeAvatar = HasManyTeams ? projects[1].Team.Avatar : "";
        TeamName = HasManyTeams ? Text.Format(Strings.Catalogue_CardProjects, ("count", projects.Count)) : BadgeName;

        var progress = best is { Status.ShowsProgress: true, Progress: { } percent } ? percent : (int?)null;
        ShowsProgress = progress is not null;
        Progress = Math.Clamp(progress ?? 0, 0, 100);
        ProgressText = progress is { } shown ? Formats.Percent(shown) : "";
        Moved = progress is null ? MovedText(game, best, clock) : "";

        IsDetected = game.Detected.Count > 0;
        PlayerGame = game.Detected.Cast<GameKey?>().FirstOrDefault(key => state.Games.Any(mine => mine.Key == key));
        // A project whose work the game's makers ship releases nothing to wait for.
        AwaitsProject = IsDetected && PlayerGame is null && game.LatestReleaseAt is null && best?.Status is not { IsCompleted: true, HoldsReleasesBack: true };

        var details = new List<string>();
        if (game.IsAdult)
        {
            details.Add(IsCoverBlurred ? Strings.Catalogue_CardAdultBlurred : Strings.Library_AdultTip);
        }

        details.Add(Status);
        details.Add(ProgressText);
        details.Add(Text.Format(Strings.Catalogue_CardTeam, ("team", BadgeName)));
        AccessibleName = HasManyTeams
            ? Text.Format(Strings.Catalogue_CardCompare, ("game", Name), ("count", projects.Count))
            : Text.Format(Strings.Catalogue_CardName, ("game", Name), ("details", string.Join(", ", details.Where(part => part.Length > 0))));
        BarName = Text.Format(Strings.Catalogue_BarName, ("game", Name), ("library", Strings.Shell_Library));
        Route = HasManyTeams || best is null
            ? new CatalogueGameRoute(game.Slug)
            : new CatalogueProjectRoute(game.Slug, best.Team.Slug, best.Slug);
    }

    // The status the card shows is the best option's: a completed project first, then the one furthest along.
    private static List<CatalogueProjectView> Best(IEnumerable<CatalogueProjectView> projects) =>
        [.. projects.OrderByDescending(project => project.Status?.IsCompleted == true).ThenByDescending(project => project.Progress ?? -1)];

    /// <summary>
    /// When a project without a release was announced, while its status holds releases back before work, or finished, once
    /// the game's makers ship its work, and otherwise when it last changed, as the site's cards tell it.
    /// </summary>
    /// <param name="project">The project; null says nothing.</param>
    /// <param name="clock">The clock ages are told against.</param>
    internal static string Announced(CatalogueProjectView? project, TimeProvider clock) => project switch
    {
        { Status: { HoldsReleasesBack: true, IsCompleted: false }, FirstPublishedAt: { } announced } =>
            Text.Format(Strings.Catalogue_Announced, ("age", Formats.CalendarAge(announced, clock))),
        { Status: { HoldsReleasesBack: true, IsCompleted: true }, CompletedAt: { } completed } =>
            Text.Format(Strings.Catalogue_Completed, ("age", Formats.CalendarAge(completed, clock))),
        { UpdatedAt: { } changed } => Text.Format(Strings.Catalogue_Changed, ("age", Formats.CalendarAge(changed, clock))),
        _ => "",
    };

    private static string MovedText(CatalogueGameView game, CatalogueProjectView? project, TimeProvider clock) =>
        game.LatestReleaseAt is { } released
            ? Text.Format(Strings.Catalogue_Released, ("age", Formats.CalendarAge(released, clock)))
            : Announced(project, clock);
}
