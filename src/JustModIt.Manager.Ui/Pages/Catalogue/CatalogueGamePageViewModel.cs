using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Catalogue;

/// <summary>What a game's page in the catalogue shows.</summary>
public enum CatalogueGameMode
{
    /// <summary>The game is still being looked up, or its age asked for.</summary>
    Loading,

    /// <summary>One project's page.</summary>
    Project,

    /// <summary>The projects several teams made for the game, side by side.</summary>
    Comparison,
}

/// <summary>A store version of the game a project is made for, in the page's line of facts.</summary>
/// <param name="Name">The store's name on the site.</param>
/// <param name="Store">The store's id in the app, for its mark; empty for one the app does not know.</param>
/// <param name="Address">The game's page in the store, which the name opens; null when it is no link.</param>
/// <param name="Separates">A comma follows it, as another store does.</param>
public sealed record StoreFact(string Name, string Store, Uri? Address, bool Separates)
{
    public bool IsLink => Address is not null;
}

/// <summary>
/// A game of the catalogue: one project's page, or, for a game several teams made a project for, their projects side by
/// side. The catalogue never installs anything: a project with a file leads to the game's page among the player's games
/// when they offer or hold it, to the game's store when the game is not on this computer, and any other project to the site.
/// </summary>
public sealed partial class CatalogueGamePageViewModel : PageViewModel, IProjectComparison, IDisposable
{
    private readonly UiServices _services;
    private readonly GoneProjects _gone;
    private readonly string _gameSlug;
    private readonly (string Team, string Project)? _wanted;
    private readonly Dictionary<Guid, ProjectDetails> _compared = [];
    private readonly CancellationTokenSource _closing = new();
    private CatalogueGameView? _game;
    private CatalogueProjectView? _project;
    private ProjectDetails? _details;
    private Uri? _primaryLink;
    private Uri? _storeLink;
    private GameKey? _playerGame;
    private bool _started;
    private bool _unanswered;

    /// <summary>Creates the page of a game, or of one of its projects.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="gameSlug">The game's slug in the site's addresses.</param>
    /// <param name="project">The project's team and project slugs; null shows the game's projects side by side.</param>
    /// <param name="gone">The projects whose page the site no longer has.</param>
    public CatalogueGamePageViewModel(UiServices services, string gameSlug, (string Team, string Project)? project, GoneProjects gone)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(gone);
        _services = services;
        _gameSlug = gameSlug;
        _wanted = project;
        _gone = gone;
        Story = new ProjectStory(services) { ShowsGalleryTitle = false };
        _services.Agent.PropertyChanged += OnAgentChanged;
    }

    public override string Title => Name.Length > 0 ? Name : Strings.Shell_Catalogue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProject), nameof(IsComparison), nameof(IsLoading), nameof(ShowsStory), nameof(ShowsActions))]
    public partial CatalogueGameMode Mode { get; private set; }

    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    [ObservableProperty]
    public partial string Cover { get; private set; } = "";

    [ObservableProperty]
    public partial string CoverName { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsAdult { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReveal))]
    public partial bool IsCoverBlurred { get; private set; }

    [ObservableProperty]
    public partial BackdropRecipe? Backdrop { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; private set; } = "";

    [ObservableProperty]
    public partial string StatusColor { get; private set; } = "";

    [ObservableProperty]
    public partial string StatusTip { get; private set; } = "";

    /// <summary>The status opens its explanation, as on a project with a file; otherwise the page explains it in a box.</summary>
    [ObservableProperty]
    public partial bool StatusOpensExplanation { get; private set; }

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial string StatusDescription { get; private set; } = "";

    /// <summary>When an abandoned project without a file last changed, which its status box says; empty for nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusDate))]
    public partial string StatusDate { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTeam))]
    public partial string TeamName { get; private set; } = "";

    [ObservableProperty]
    public partial string TeamAvatar { get; private set; } = "";

    [ObservableProperty]
    public partial Uri? TeamPage { get; private set; }

    /// <summary>The teams of a game several teams made a project for, e.g. "2 projects: A and B".</summary>
    [ObservableProperty]
    public partial string TeamsLine { get; private set; } = "";

    [ObservableProperty]
    public partial string OtherTeamName { get; private set; } = "";

    [ObservableProperty]
    public partial string OtherTeamAvatar { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    public partial string Summary { get; private set; } = "";

    /// <summary>The page's one red button; null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrimary), nameof(LeadsWithSite), nameof(ShowsActions))]
    public partial string? Primary { get; private set; }

    /// <summary>The red button opens the game among the player's games rather than in a store.</summary>
    [ObservableProperty]
    public partial bool PrimaryOpensGame { get; private set; }

    /// <summary>The store the red button opens, for its mark.</summary>
    [ObservableProperty]
    public partial string PrimaryStore { get; private set; } = "";

    /// <summary>
    /// An outlined button to the game in its store, for a status whose work the game's makers ship themselves; null for
    /// none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStoreAction), nameof(LeadsWithSite))]
    public partial string? StoreAction { get; private set; }

    /// <summary>The store <see cref="StoreAction"/> opens, for its mark.</summary>
    [ObservableProperty]
    public partial string StoreActionStore { get; private set; } = "";

    /// <summary>The release and when it came out, or when the project was announced or last changed.</summary>
    [ObservableProperty]
    public partial string Released { get; private set; } = "";

    /// <summary>The project's work stopped, though its last version is there.</summary>
    [ObservableProperty]
    public partial bool IsStopped { get; private set; }

    public ObservableCollection<StoreFact> Stores { get; } = [];

    [ObservableProperty]
    public partial bool HasStores { get; private set; }

    /// <summary>The release and the stores stand beside the buttons; a status that releases nothing has none.</summary>
    [ObservableProperty]
    public partial bool ShowsFacts { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVariants))]
    public partial string Variants { get; private set; } = "";

    [ObservableProperty]
    public partial Uri? SitePage { get; private set; }

    /// <summary>The status's explanation or progress stands in the page, as where there is nothing to download.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStatusBoxAbove), nameof(ShowsStatusBoxBelow))]
    public partial bool ShowsStatusBox { get; private set; }

    /// <summary>The status's box follows the buttons, as it does beside a file; otherwise it stands where they would.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStatusBoxAbove), nameof(ShowsStatusBoxBelow))]
    public partial bool StatusBoxFollowsActions { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    public partial string ProgressKind { get; private set; } = "";

    /// <summary>From 0 to 100.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string ProgressPercent { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgressValue))]
    public partial string ProgressValue { get; private set; } = "";

    /// <summary>What the page says about the game on this computer; empty for nothing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    public partial string Note { get; private set; } = "";

    /// <summary>The game is not on this computer while the project has a file, which a box explains.</summary>
    [ObservableProperty]
    public partial bool ShowsAbsent { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsActions), nameof(ShowsStory))]
    public partial bool IsLoadingDetails { get; private set; }

    /// <summary>The project's page could not be read and was never saved, so the page shows what the catalogue knows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStory))]
    public partial bool IsOffline { get; private set; }

    /// <summary>The project's page has a banner, which the head leaves room for above it.</summary>
    [ObservableProperty]
    public partial bool HasBanner { get; private set; }

    /// <summary>The gallery, the game's story and the people behind the project.</summary>
    public ProjectStory Story { get; }

    public ObservableCollection<ProjectChoice> Choices { get; } = [];

    [ObservableProperty]
    public partial string ChoicesText { get; private set; } = "";

    /// <summary>The window is narrow, so the cover shrinks and the site's button goes under the facts.</summary>
    [ObservableProperty]
    public partial bool IsCompact { get; set; }

    public bool IsProject => Mode == CatalogueGameMode.Project;

    public bool IsComparison => Mode == CatalogueGameMode.Comparison;

    public bool IsLoading => Mode == CatalogueGameMode.Loading;

    public bool CanReveal => IsCoverBlurred;

    public bool HasStatus => Status.Length > 0;

    public bool HasStatusDate => StatusDate.Length > 0;

    public bool HasTeam => TeamName.Length > 0;

    public bool HasSummary => Summary.Length > 0;

    public bool HasPrimary => Primary is not null;

    public bool HasStoreAction => StoreAction is not null;

    /// <summary>Without another button the site's button leads the line.</summary>
    public bool LeadsWithSite => !HasPrimary && !HasStoreAction;

    public bool HasVariants => Variants.Length > 0;

    public bool HasProgress => ProgressKind.Length > 0 || ProgressPercent.Length > 0;

    public bool HasProgressValue => ProgressValue.Length > 0;

    public bool ShowsStatusBoxAbove => ShowsStatusBox && !StatusBoxFollowsActions;

    public bool ShowsStatusBoxBelow => ShowsStatusBox && StatusBoxFollowsActions;

    /// <summary>The actions wait for the project's page, which tells whether there is a file; a comparison has only its red button.</summary>
    public bool ShowsActions => !IsLoadingDetails && (IsProject || HasPrimary);

    /// <summary>The gallery, story and creators show once the project's page is there.</summary>
    public bool ShowsStory => IsProject && !IsLoadingDetails && Story.Details is not null;

    public bool HasNote => Note.Length > 0;

    ICommand IProjectComparison.ChooseProjectCommand => OpenChoiceCommand;

    ICommand IProjectComparison.OpenStatusesCommand => OpenStatusesCommand;

    // Looked up once shown, so a page that only passes on the way to another one asks nothing.
    public override void OnShown()
    {
        if (!_started || (_game is null && _unanswered))
        {
            _started = true;
            _ = LoadAsync();
        }
        else if (_game is { } game)
        {
            _ = ReturnAsync(game);
        }
    }

    public void Dispose()
    {
        _services.Agent.PropertyChanged -= OnAgentChanged;
        _closing.Cancel();
        _closing.Dispose();
    }

    [RelayCommand]
    private void ToggleStatus() => IsStatusOpen = !IsStatusOpen;

    [RelayCommand]
    private void Open(Uri? address)
    {
        if (address is not null)
        {
            _services.Links.Open(address);
        }
    }

    [RelayCommand]
    private void OpenStatuses() =>
        Open(PageLinks.Help(_services.Context.Site.SiteAddress,
            _details?.StatusSlug is { Length: > 0 } slug ? "status-" + slug : "project-statuses"));

    [RelayCommand]
    private void RunPrimary()
    {
        if (PrimaryOpensGame && _playerGame is { } game)
        {
            _services.Navigator.Navigate(new GameRoute(game));
        }
        else
        {
            Open(_primaryLink);
        }
    }

    [RelayCommand]
    private void RunStoreAction() => Open(_storeLink);

    [RelayCommand]
    private void OpenUndetected() => _services.Navigator.Navigate(new UndetectedGamesRoute(ShellSection.Catalogue));

    [RelayCommand]
    private async Task RevealAsync()
    {
        if (_game is { } game)
        {
            await AgentRequests.SendAsync(_services, new RevealAdultPictures(game.Id)).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        if (_project is { } project)
        {
            await LoadDetailsAsync(project).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void OpenChoice(ProjectChoice choice)
    {
        if (_game?.Projects.FirstOrDefault(project => project.Id == choice.ProjectId) is { } project)
        {
            _services.Navigator.Navigate(new CatalogueProjectRoute(_gameSlug, project.Team.Slug, project.Slug));
        }
    }

    // What the agent left unanswered, as while it restarts, is asked again with the next state it sends.
    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(IAgent.State))
        {
            return;
        }

        if (_unanswered && _services.Navigator.Current == this)
        {
            _ = _game is null ? LoadAsync()
                : _project is { } project ? LoadDetailsAsync(project)
                : LoadComparedAsync(_game);
            return;
        }

        Compose();
    }

    private async Task LoadAsync()
    {
        _unanswered = false;
        // The page is shown while its navigation is still under way, which a fallback must not cut into.
        await Task.Yield();
        var (_, answer) = await AgentRequests.AskAsync(_services, new GetCatalogueGame(_gameSlug)).ConfigureAwait(true);
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        // No answer is no broken link: the page waits for the agent rather than giving way.
        if (answer is not CatalogueGameResult { Game: var game })
        {
            _unanswered = true;
            return;
        }

        if (game is null)
        {
            Leave(new CatalogueRoute());
            return;
        }

        if (_wanted is { } wanted)
        {
            _project = game.Projects.FirstOrDefault(project => project.Team.Slug == wanted.Team && project.Slug == wanted.Project)
                ?? game.Projects.FirstOrDefault(project => project.Team.Slug == wanted.Team);
            if (_project is null)
            {
                Leave(new CatalogueGameRoute(_gameSlug));
                return;
            }
        }
        else if (LeaveComparison(game))
        {
            return;
        }

        if (game.IsAdult && !await AdultContent.ConfirmAsync(_services).ConfigureAwait(true))
        {
            LeaveBack();
            return;
        }

        _game = game;
        Mode = _project is null ? CatalogueGameMode.Comparison : CatalogueGameMode.Project;
        OnPropertyChanged(nameof(Title));
        Compose();
        await (_project is { } shown ? LoadDetailsAsync(shown) : LoadComparedAsync(game)).ConfigureAwait(true);
    }

    private async Task LoadDetailsAsync(CatalogueProjectView project)
    {
        _unanswered = false;
        IsOffline = false;
        IsLoadingDetails = _details is null;
        var (answer, details) = await ReadPageAsync(project).ConfigureAwait(true);
        if (_closing.IsCancellationRequested)
        {
            return;
        }

        IsLoadingDetails = false;
        if (answer is { IsGone: true, Page: null })
        {
            Leave(new CatalogueGameRoute(_gameSlug));
            return;
        }

        _unanswered = answer is null;
        _details = details ?? _details;
        Story.Details = _details;
        IsOffline = _details is null;
        OnPropertyChanged(nameof(ShowsStory));
        Compose();
    }

    private async Task LoadComparedAsync(CatalogueGameView game)
    {
        _unanswered = false;
        foreach (var project in Listed(game).Where(project => !_compared.ContainsKey(project.Id)))
        {
            var (answer, details) = await ReadPageAsync(project).ConfigureAwait(true);
            if (_closing.IsCancellationRequested || (answer is { IsGone: true, Page: null } && LeaveComparison(game)))
            {
                return;
            }

            _unanswered |= answer is null;
            if (details is not null)
            {
                _compared[project.Id] = details;
            }

            Compose();
        }
    }

    private async Task<(ProjectPageResult? Answer, ProjectDetails? Details)> ReadPageAsync(CatalogueProjectView project)
    {
        var (_, answer) = await AgentRequests.AskAsync(_services, new GetProjectPage(_gameSlug, project.Team.Slug, project.Id)).ConfigureAwait(true);
        var page = answer as ProjectPageResult;
        if (page is { IsGone: true, Page: null })
        {
            _gone.Add(project.Id);
        }

        return (page, page?.Page is { } json ? await Task.Run(() => ProjectDetails.Read(json)).ConfigureAwait(true) : null);
    }

    // Back on the page: a project found gone meanwhile may leave the comparison with one project or none.
    private async Task ReturnAsync(CatalogueGameView game)
    {
        await Task.Yield();
        if (_project is not null || !LeaveComparison(game))
        {
            Compose();
        }
    }

    // Projects the catalogue lists, abandoned ones aside, whose page the site still has.
    private List<CatalogueProjectView> Listed(CatalogueGameView game) =>
        [.. game.Projects.Where(project => project.Status?.IsAbandoned != true && !_gone.Contains(project.Id))];

    // A game sets projects side by side only while it has several: one opens that project's page, none the catalogue.
    private bool LeaveComparison(CatalogueGameView game)
    {
        var listed = Listed(game);
        if (listed.Count > 1)
        {
            return false;
        }

        Leave(listed.Count == 1 ? new CatalogueProjectRoute(_gameSlug, listed[0].Team.Slug, listed[0].Slug) : new CatalogueRoute());
        return true;
    }

    // Only the page shown gives way to another; one the player has left behind meanwhile stays as it is.
    private void Leave(Route route)
    {
        if (_services.Navigator.Current == this)
        {
            _services.Navigator.Replace(route);
        }
    }

    private void LeaveBack()
    {
        if (_services.Navigator.Current != this)
        {
            return;
        }

        if (_services.Navigator.CanGoBack)
        {
            _services.Navigator.GoBackCommand.Execute(null);
        }
        else
        {
            _services.Navigator.Navigate(new CatalogueRoute());
        }
    }

    // The player's copy of the game that offers or holds the most of these projects, with those it does: the player's games
    // install only the projects they offer, and update those already installed.
    private (GameKey Key, HashSet<Guid> Projects)? PlayerGame(IEnumerable<Guid> projects)
    {
        var wanted = projects.ToHashSet();
        (GameKey Key, HashSet<Guid> Projects)? best = null;
        foreach (var key in _game?.Detected ?? [])
        {
            if (_services.Agent.State.Games.FirstOrDefault(game => game.Key == key) is not { } mine)
            {
                continue;
            }

            var found = mine.Projects.Select(offer => offer.ProjectId).Concat(mine.Installs.Select(install => install.Project?.ProjectId).OfType<Guid>())
                .Where(wanted.Contains).ToHashSet();
            if (found.Count > (best?.Projects.Count ?? 0))
            {
                best = (key, found);
            }
        }

        return best;
    }

    private void Compose()
    {
        if (_game is not { } game)
        {
            return;
        }

        var state = _services.Agent.State;
        Name = game.Name;
        Cover = game.Cover;
        CoverName = Text.Format(Strings.GamePage_CoverName, ("game", game.Name));
        IsAdult = game.IsAdult;
        IsCoverBlurred = AdultContent.IsVeiled(state, game.IsAdult, [game.Id]);
        // A comparison speaks for several teams, so it shows no team's artwork or banner, only the glow of the game's cover.
        var backdrop = _details?.Backdrop
            ?? new BackdropRecipe(game.Id.ToString(), "", "", _compared.Values.FirstOrDefault()?.Backdrop.Tint ?? "", game.Cover);
        HasBanner = backdrop.Artwork.Length > 0 || backdrop.Banner.Length > 0;
        Backdrop = backdrop with { IsBlurred = IsCoverBlurred };
        if (_project is { } project)
        {
            ShowProject(game, project, state);
        }
        else
        {
            ShowComparison(game, state);
        }
    }

    private void ShowProject(CatalogueGameView game, CatalogueProjectView project, AppStateView state)
    {
        var details = _details;
        var hasFile = details?.HasFile == true;
        // A status whose work the game's makers ship themselves releases nothing: the store is where the player goes.
        var official = details?.ShipsWithGame == true;
        var abandoned = project.Status?.IsAbandoned == true;
        var player = _playerGame = PlayerGame([project.Id])?.Key;
        var detected = game.Detected.Count > 0;
        Status = project.Status?.Name ?? "";
        StatusColor = project.Status?.Color ?? "";
        StatusTip = Text.Format(Strings.GamePage_StatusTip, ("status", Status));
        StatusDescription = details?.StatusDescription ?? "";
        StatusOpensExplanation = hasFile;
        (TeamName, TeamAvatar) = (project.Team.Name, project.Team.Avatar);
        var site = _services.Context.Site.SiteAddress;
        TeamPage = PageLinks.TeamPage(site, project.Team.Slug);
        SitePage = PageLinks.ProjectPage(site, game.Slug, project.Team.Slug);
        Summary = details?.Summary is { Length: > 0 } summary ? summary : project.Summary;

        var stores = details?.Stores ?? [];
        var found = state.Library.Stores.Where(store => store.IsFound).Select(store => store.Store).ToHashSet();
        var storeLink = stores.Where(link => link.Address is not null).OrderByDescending(link => found.Contains(link.Store)).FirstOrDefault();
        _primaryLink = null;
        // A game found on this computer needs no store, and the player's games lead on only for a project they offer or hold.
        (Primary, PrimaryOpensGame, PrimaryStore) = (hasFile, player, detected, storeLink) switch
        {
            (true, not null, _, _) => (Text.Format(Strings.Catalogue_OpenInLibrary, ("library", Strings.Shell_Library)), true, ""),
            (true, null, false, { } link) => (Text.Format(Strings.Catalogue_SeeInStore, ("store", link.Name)), false, link.Store),
            _ => ((string?)null, false, ""),
        };
        if (Primary is not null && !PrimaryOpensGame)
        {
            _primaryLink = storeLink?.Address;
        }

        (StoreAction, StoreActionStore, _storeLink) = official ? OfficialStore(game, stores, storeLink) : (null, "", null);
        CardGrid.Sync(Stores, [.. stores.Select((link, index) =>
            new StoreFact(link.Name, link.Store, _primaryLink is not null && link == storeLink ? null : link.Address, index < stores.Count - 1))]);
        HasStores = Stores.Count > 0;
        ShowsFacts = !official;
        // An abandoned project without a file says when it last changed in its status box, not again among the facts.
        StatusDate = abandoned && !hasFile && (project.UpdatedAt ?? project.FirstPublishedAt) is { } changed
            ? Text.Format(Strings.Catalogue_LastChange, ("age", Formats.CalendarAge(changed, _services.Clock)),
                ("date", Formats.LongDate(changed, _services.Clock.LocalTimeZone)))
            : "";
        Released = StatusDate.Length > 0 ? ""
            : details?.Release is { } release
                ? GameWording.Join([Text.Format(Strings.GamePage_Version, ("version", GameWording.Version(release))), ReleasedText(release)])
            : CatalogueCardViewModel.Announced(project, _services.Clock);
        IsStopped = hasFile && abandoned;
        Variants = details is { Variants.Count: > 1 } ? Text.Format(Strings.Catalogue_Variants, ("variants", string.Join(" · ", details.Variants))) : "";

        var progress = details?.Progress;
        // Where there is a file the status explains itself in its bubble, unless the project shows how far it is.
        ShowsStatusBox = (StatusDescription.Length > 0 || progress is not null || StatusDate.Length > 0) && (!hasFile || progress is not null);
        StatusBoxFollowsActions = hasFile;
        ProgressKind = progress?.Kind ?? "";
        Progress = progress?.Percent ?? 0;
        ProgressPercent = progress is null ? "" : Formats.Percent(progress.Percent);
        ProgressValue = progress is { Done: { } done, Total: { } total }
            ? Text.Format(Strings.Catalogue_ProgressValue, ("done", Formats.Number(done)), ("total", Formats.Number(total)), ("unit", progress.Unit))
            : "";

        // Only the project's page tells that nothing is out yet, so without it the page promises nothing.
        Note = (hasFile, player, detected) switch
        {
            (_, not null, _) => Text.Format(Strings.Catalogue_HereInstalls, ("library", Strings.Shell_Library)),
            (false, _, true) when details is not null && !abandoned && !official => Text.Format(Strings.Catalogue_HereWaits, ("library", Strings.Shell_Library)),
            (_, _, true) => Strings.Catalogue_Here,
            _ => "",
        };
        ShowsAbsent = !detected && JoinsPlayersGames(project, details);
        (Story.GameName, Story.TeamName, Story.TeamAvatar, Story.TeamPage, Story.IsBlurred) = (game.Name, TeamName, TeamAvatar, TeamPage, IsCoverBlurred);
    }

    // Only a completed project with a file brings its game to the player's games once the game is installed.
    private static bool JoinsPlayersGames(CatalogueProjectView project, ProjectDetails? details) =>
        project.Status?.IsCompleted == true && details?.HasFile == true;

    // The game in its store's client where the window has a link for one on this computer, else its page in the store.
    private static (string? Label, string Store, Uri? Address) OfficialStore(CatalogueGameView game, IReadOnlyList<StoreLinkView> stores,
        StoreLinkView? storeLink)
    {
        foreach (var key in game.Detected)
        {
            if (PageLinks.StorePage(key) is { } client)
            {
                var name = stores.FirstOrDefault(link => link.Store == key.Store.Value)?.Name ?? GameWording.StoreName(key.Store);
                return (Text.Format(Strings.Catalogue_OpenInStore, ("store", name)), key.Store.Value, client);
            }
        }

        return storeLink is { Address: { } address }
            ? (Text.Format(Strings.Catalogue_SeeInStore, ("store", storeLink.Name)), storeLink.Store, address)
            : (null, "", null);
    }

    private void ShowComparison(CatalogueGameView game, AppStateView state)
    {
        var listed = Listed(game).OrderByDescending(project => project.Status?.IsCompleted == true).ThenByDescending(project => project.Progress ?? -1).ToList();
        var mine = PlayerGame(listed.Select(project => project.Id));
        _playerGame = mine?.Key;
        // As on a project's page, only a file makes the way to the player's games red, and only several leave a choice there.
        var files = mine?.Projects.Count(id => _compared.GetValueOrDefault(id)?.HasFile == true) ?? 0;
        var names = listed.Select(project => project.Team.Name).ToList();
        var teams = names.Count < 2
            ? string.Join("", names)
            : Text.Format(Strings.Catalogue_And, ("first", string.Join(", ", names.Take(names.Count - 1))), ("second", names[^1]));
        TeamsLine = Text.Format(Strings.Catalogue_TeamsLine, ("count", listed.Count), ("teams", teams));
        (TeamName, TeamAvatar) = listed.Count > 0 ? (listed[0].Team.Name, listed[0].Team.Avatar) : ("", "");
        (OtherTeamName, OtherTeamAvatar) = listed.Count > 1 ? (listed[1].Team.Name, listed[1].Team.Avatar) : ("", "");
        Summary = listed.Select(project => project.Summary).FirstOrDefault(summary => summary.Length > 0) ?? "";
        (Primary, PrimaryOpensGame, PrimaryStore) = files > 0
            ? (Text.Format(Strings.Catalogue_OpenInLibrary, ("library", Strings.Shell_Library)), true, "")
            : ((string?)null, false, "");
        var detected = game.Detected.Count > 0;
        Note = (mine, files) switch
        {
            (not null, > 1) => Text.Format(Strings.Catalogue_HereChoose, ("library", Strings.Shell_Library)),
            (not null, _) => Text.Format(Strings.Catalogue_HereInstalls, ("library", Strings.Shell_Library)),
            _ => detected ? Strings.Catalogue_Here : "",
        };
        ShowsAbsent = !detected && listed.Any(project => JoinsPlayersGames(project, _compared.GetValueOrDefault(project.Id)));

        // The stores every project is made for are said once above the columns.
        var shared = listed.Count > 0 && listed.All(project => _compared.ContainsKey(project.Id))
            ? listed.Select(project => _compared[project.Id].Stores.Select(store => store.Name)).Aggregate((first, next) => first.Intersect(next)).ToList()
            : [];
        ChoicesText = Text.Format(Strings.Catalogue_CompareText, ("hasStores", shared.Count > 0 ? "true" : "false"), ("stores", string.Join(", ", shared)));
        CardGrid.Sync(Choices, [.. listed.Select(Choice)]);
    }

    private ProjectChoice Choice(CatalogueProjectView project)
    {
        var details = _compared.GetValueOrDefault(project.Id);
        var version = GameWording.Version(details?.Release);
        var released = details?.Release is { } release ? GameWording.Released(release, _services.Clock) : "";
        var progress = details?.Progress;
        return new ProjectChoice(project.Id, project.Team.Name, project.Team.Avatar, project.Status?.Name ?? "", project.Status?.Color ?? "",
            version.Length > 0 ? Text.Format(Strings.GamePage_Version, ("version", version)) : "", released)
        {
            AccessibleName = Text.Format(Strings.GamePage_ChoiceName, ("team", project.Team.Name), ("status", project.Status?.Name ?? ""),
                ("version", version)),
            ProgressKind = progress?.Kind ?? "",
            Progress = progress?.Percent ?? 0,
            ProgressText = progress is null ? "" : Formats.Percent(progress.Percent),
        };
    }

    private string ReleasedText(ReleaseView release) =>
        release.Date is { } date
            ? Text.Format(release.DateKind == ReleaseDateKind.Released ? Strings.Catalogue_Released : Strings.Catalogue_Changed,
                ("age", Formats.CalendarAge(date, _services.Clock)))
            : "";
}
