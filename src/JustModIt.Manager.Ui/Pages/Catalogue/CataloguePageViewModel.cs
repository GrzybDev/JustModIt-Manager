using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Catalogue;

/// <summary>What the catalogue shows below its filters.</summary>
public enum CatalogueContent
{
    /// <summary>The first answer has not come yet; skeleton cards stand in.</summary>
    Loading,

    /// <summary>The site was never read and nothing was saved, so there is nothing to browse.</summary>
    Unavailable,
    Games,

    /// <summary>The catalogue holds no game at all.</summary>
    Empty,

    /// <summary>Nothing has the name searched for, whatever the filters.</summary>
    NoResults,

    /// <summary>Something has the name, or the catalogue has games, but not with the filters chosen.</summary>
    FilteredOut,
}

/// <summary>What the head of the page says about how fresh the catalogue is.</summary>
public enum CatalogueFreshness
{
    /// <summary>A banner says it instead.</summary>
    None,
    Loading,
    Refreshing,
    Updated,
}

/// <summary>
/// The catalogue: every game the site lists, searched, filtered and ordered as the site's games directory does it, a page
/// of cards at a time as the grid scrolls. Everything comes from the agent; the catalogue never installs anything, so a
/// game on this computer leads to its page among the player's games.
/// </summary>
public sealed partial class CataloguePageViewModel : PageViewModel, IDisposable
{
    /// <summary>How many games one answer brings; the grid asks for more as it scrolls.</summary>
    public const int PageSize = 60;

    private const int SkeletonCards = 10;

    // A word is asked for once, not with every letter typed.
    private static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(200);

    private readonly UiServices _services;
    private readonly CardGrid _grid = new();
    private readonly Dictionary<Guid, CatalogueCardViewModel> _cards = [];
    private readonly Dictionary<(CatalogueFilter, Guid), FilterOption> _options = [];
    private readonly List<CatalogueGameView> _games = [];
    private readonly DispatcherTimer _typing;
    private readonly AllStatusesPill _allStatuses = new();
    private readonly StatusesHelp _statusesHelp = new();
    private CatalogueFiltersResult? _filters;
    private CataloguePageResult? _page;
    private int _version;
    private bool _loadingMore;
    private bool _holding;
    private bool _includeAdult;
    private bool _unanswered;
    private (DateTimeOffset? Catalogue, DateTimeOffset? Library, DateTimeOffset? Adult)? _asked;

    /// <summary>Creates the page, which follows the agent's state from then on.</summary>
    /// <param name="services">The window's services.</param>
    public CataloguePageViewModel(UiServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _typing = new DispatcherTimer { Interval = TypingPause };
        _typing.Tick += (_, _) =>
        {
            _typing.Stop();
            Requery(keep: false);
        };
        Skeletons = [.. Enumerable.Range(0, SkeletonCards)];
        MoreGroups =
        [
            new FilterGroup(CatalogueFilter.Type, Strings.Catalogue_Type),
            new FilterGroup(CatalogueFilter.Store, Strings.Catalogue_Store),
            new FilterGroup(CatalogueFilter.Platform, Strings.Catalogue_Platform),
            new FilterGroup(CatalogueFilter.Genre, Strings.Catalogue_Genre),
        ];
        _services.Agent.PropertyChanged += OnAgentChanged;
        Refresh();
    }

    public override string Title => Strings.Shell_Catalogue;

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllStatuses))]
    public partial Guid? Status { get; set; }

    [ObservableProperty]
    public partial Guid? Team { get; set; }

    [ObservableProperty]
    public partial Guid? Type { get; set; }

    [ObservableProperty]
    public partial Guid? Store { get; set; }

    [ObservableProperty]
    public partial Guid? Platform { get; set; }

    [ObservableProperty]
    public partial Guid? Genre { get; set; }

    /// <summary>Only the games found on this computer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersMine), nameof(ShowsWideMine), nameof(ShowsCompactMine))]
    public partial bool DetectedOnly { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortsByReleased), nameof(SortsByPopularity), nameof(SortsByName))]
    public partial CatalogueOrder Order { get; set; }

    /// <summary>Adult games too; turning it on asks for the player's age first.</summary>
    public bool IncludeAdult
    {
        get => _includeAdult;
        set
        {
            if (value && !AdultContent.IsConfirmed(_services))
            {
                // The pill stays off until the player answers; the binding reads it back once its own write is done.
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IncludeAdult)));
                _ = ConfirmAdultAsync();
                return;
            }

            if (SetProperty(ref _includeAdult, value))
            {
                Requery(keep: false);
            }
        }
    }

    public bool IsAllStatuses
    {
        get => Status is null;
        set
        {
            if (value)
            {
                Status = null;
            }
            else
            {
                // A click on the chosen pill for every status keeps it chosen; a status is dropped by choosing it again.
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsAllStatuses)));
            }
        }
    }

    public bool SortsByReleased
    {
        get => Order == CatalogueOrder.Released;
        set => Sort(value, CatalogueOrder.Released);
    }

    public bool SortsByPopularity
    {
        get => Order == CatalogueOrder.Popular;
        set => Sort(value, CatalogueOrder.Popular);
    }

    public bool SortsByName
    {
        get => Order == CatalogueOrder.Alphabetical;
        set => Sort(value, CatalogueOrder.Alphabetical);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGames), nameof(IsLoading), nameof(ShowsEmptyState), nameof(ShowsFilters), nameof(IsUnavailable),
        nameof(ShowsClearInCard), nameof(ArePillsLoading), nameof(OffersMine), nameof(ShowsWideMine), nameof(ShowsCompactMine))]
    public partial CatalogueContent Content { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsFreshness), nameof(IsBusy), nameof(ShowsRefresh))]
    public partial CatalogueFreshness Freshness { get; private set; }

    [ObservableProperty]
    public partial string FreshnessText { get; private set; } = "";

    [ObservableProperty]
    public partial bool ShowsBanner { get; private set; }

    [ObservableProperty]
    public partial string BannerTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string BannerText { get; private set; } = "";

    [ObservableProperty]
    public partial Geometry BannerIcon { get; private set; } = Icons.WifiOff;

    public ObservableCollection<FilterOption> StatusOptions { get; } = [];

    /// <summary>The statuses' row: the pill for all of them, each status that leaves a game, and the way to their meaning.</summary>
    public ObservableCollection<object> StatusPills { get; } = [];

    [ObservableProperty]
    public partial int AllCount { get; private set; }

    public ObservableCollection<FilterOption> TeamOptions { get; } = [];

    [ObservableProperty]
    public partial bool ShowsTeams { get; private set; }

    [ObservableProperty]
    public partial bool ShowsTeamLabel { get; private set; }

    /// <summary>The filters the site keeps behind "more filters": types, stores, platforms and genres.</summary>
    public IReadOnlyList<FilterGroup> MoreGroups { get; }

    /// <summary>The groups of more filters that could narrow the catalogue at all.</summary>
    public ObservableCollection<FilterGroup> ShownGroups { get; } = [];

    [ObservableProperty]
    public partial bool ShowsMore { get; private set; }

    [ObservableProperty]
    public partial bool AreMoreOpen { get; set; }

    [ObservableProperty]
    public partial string MoreText { get; private set; } = "";

    /// <summary>The catalogue has adult games, or they are shown, so the pill that shows them is offered.</summary>
    [ObservableProperty]
    public partial bool OffersAdult { get; private set; }

    [ObservableProperty]
    public partial int AdultCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersMine), nameof(ShowsWideMine), nameof(ShowsCompactMine))]
    public partial int DetectedCount { get; private set; }

    /// <summary>The first answer came, so the counts and pills are real.</summary>
    [ObservableProperty]
    public partial bool HasAnswer { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsClearInCard))]
    public partial bool HasFilters { get; private set; }

    [ObservableProperty]
    public partial string EmptyTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string EmptyMessage { get; private set; } = "";

    [ObservableProperty]
    public partial Geometry EmptyIcon { get; private set; } = Icons.Search;

    /// <summary>The empty state's one way on; null for none.</summary>
    [ObservableProperty]
    public partial string? EmptyAction { get; private set; }

    /// <summary>How many games the grid holds, for screen readers.</summary>
    [ObservableProperty]
    public partial string ResultsText { get; private set; } = "";

    /// <summary>The page is narrow, so "on this computer" moves beside the order and the search keeps its whole line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsWideMine), nameof(ShowsCompactMine))]
    public partial bool IsCompact { get; set; }

    /// <summary>The search box shows its clear button instead of its shortcut.</summary>
    [ObservableProperty]
    public partial bool HasQuery { get; private set; }

    public ObservableCollection<CardRow> Rows => _grid.Rows;

    /// <summary>The grid the view lays out and moves the keyboard through.</summary>
    public CardGrid Grid => _grid;

    /// <summary>Stand-ins for the cards while loading.</summary>
    public IReadOnlyList<int> Skeletons { get; }

    /// <summary>How many cards fit in a row; the view sets it from its width.</summary>
    public int Columns
    {
        get => _grid.Columns;
        set
        {
            if (_grid.SetColumns(value))
            {
                OnPropertyChanged();
            }
        }
    }

    public bool ShowsGames => Content == CatalogueContent.Games;

    public bool IsLoading => Content == CatalogueContent.Loading;

    public bool IsUnavailable => Content == CatalogueContent.Unavailable;

    public bool ShowsEmptyState => Content is CatalogueContent.Empty or CatalogueContent.NoResults or CatalogueContent.FilteredOut;

    public bool ShowsFilters => Content != CatalogueContent.Unavailable;

    /// <summary>The pills wait for the catalogue as the cards do.</summary>
    public bool ArePillsLoading => Content == CatalogueContent.Loading;

    /// <summary>"On this computer" is offered once the games found here are matched, and while one of them fits the rest.</summary>
    public bool OffersMine => !ArePillsLoading && (DetectedCount > 0 || DetectedOnly);

    public bool ShowsWideMine => OffersMine && !IsCompact;

    public bool ShowsCompactMine => OffersMine && IsCompact;

    /// <summary>"Clear filters" stands in the filters' card unless the empty state offers it below.</summary>
    public bool ShowsClearInCard => HasFilters && Content != CatalogueContent.FilteredOut;

    public bool ShowsFreshness => Freshness != CatalogueFreshness.None;

    public bool IsBusy => Freshness is CatalogueFreshness.Loading or CatalogueFreshness.Refreshing;

    public bool ShowsRefresh => Freshness == CatalogueFreshness.Updated;

    public override void OnShown() => Refresh();

    public void Dispose()
    {
        _typing.Stop();
        _services.Agent.PropertyChanged -= OnAgentChanged;
    }

    /// <summary>Asks for the next page of games once the grid nears its end.</summary>
    public void LoadMore()
    {
        if (_loadingMore || _page is null || _games.Count >= _page.Total)
        {
            return;
        }

        _loadingMore = true;
        _ = LoadAsync(_version, _games.Count, PageSize);
    }

    partial void OnQueryChanged(string value)
    {
        HasQuery = value.Length > 0;
        _typing.Stop();
        _typing.Start();
    }

    partial void OnStatusChanged(Guid? value) => Requery(keep: false);

    partial void OnTeamChanged(Guid? value) => Requery(keep: false);

    partial void OnTypeChanged(Guid? value) => Requery(keep: false);

    partial void OnStoreChanged(Guid? value) => Requery(keep: false);

    partial void OnPlatformChanged(Guid? value) => Requery(keep: false);

    partial void OnGenreChanged(Guid? value) => Requery(keep: false);

    partial void OnDetectedOnlyChanged(bool value) => Requery(keep: false);

    partial void OnOrderChanged(CatalogueOrder value) => Requery(keep: false);

    partial void OnAreMoreOpenChanged(bool value) => ShowMoreText();

    [RelayCommand]
    private async Task RefreshCatalogueAsync() => await AgentRequests.SendAsync(_services, new CheckNow()).ConfigureAwait(true);

    [RelayCommand]
    private void ClearSearch()
    {
        Query = "";
        _typing.Stop();
        Requery(keep: false);
    }

    // Clears status, team, the more filters and "on this computer" in one question, leaving the search as it is.
    [RelayCommand]
    private void ClearFilters()
    {
        _holding = true;
        (Status, Team, Type, Store, Platform, Genre, DetectedOnly) = (null, null, null, null, null, null, false);
        SetProperty(ref _includeAdult, false, nameof(IncludeAdult));
        _holding = false;
        Requery(keep: false);
    }

    [RelayCommand]
    private void RunEmptyAction()
    {
        if (Content == CatalogueContent.NoResults)
        {
            ClearSearch();
        }
        else
        {
            ClearFilters();
        }
    }

    [RelayCommand]
    private void ToggleMore() => AreMoreOpen = !AreMoreOpen;

    [RelayCommand]
    private void OpenStatuses()
    {
        if (PageLinks.Help(_services.Context.Site.SiteAddress, "project-statuses") is { } help)
        {
            _services.Links.Open(help);
        }
    }

    [RelayCommand]
    private void OpenCard(CatalogueCardViewModel card) => _services.Navigator.Navigate(card.Route);

    [RelayCommand]
    private void OpenPlayerGame(CatalogueCardViewModel card)
    {
        if (card.PlayerGame is { } game)
        {
            _services.Navigator.Navigate(new GameRoute(game));
        }
    }

    // The agent takes the confirmation before it answers, while the state that says so may come a little later.
    private async Task ConfirmAdultAsync()
    {
        if (await AdultContent.ConfirmAsync(_services).ConfigureAwait(true) && SetProperty(ref _includeAdult, true, nameof(IncludeAdult)))
        {
            Requery(keep: false);
        }
    }

    private void Sort(bool value, CatalogueOrder order)
    {
        if (value)
        {
            Order = order;
        }
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IAgent.State) or nameof(IAgent.HasState))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var state = _services.Agent.State;
        ShowFreshness(state);
        var asked = (state.Catalogue.FetchedAt, state.Library.ScannedAt, state.AdultContentConfirmedUntil);
        // What the agent left unanswered, as while it restarts, is asked again with the next state it sends.
        if ((_asked != asked || _unanswered) && _services.Agent.HasState)
        {
            var catalogueChanged = _asked?.Catalogue != asked.FetchedAt || _filters is null;
            _asked = asked;
            _unanswered = false;
            if (catalogueChanged)
            {
                _ = LoadFiltersAsync();
            }

            Requery(keep: true);
            return;
        }

        Compose();
    }

    private void ShowFreshness(AppStateView state)
    {
        var catalogue = state.Catalogue;
        // The site may say it is under maintenance while it still answers, which the banner says too.
        var problem = catalogue.Failure ?? (catalogue.IsUnderMaintenance ? CatalogueErrorReason.Maintenance : null);
        ShowsBanner = catalogue.FetchedAt is not null && problem is not null && catalogue.State != CatalogueState.Refreshing;
        if (ShowsBanner)
        {
            BannerTitle = Text.Format(Strings.Catalogue_BannerTitle, ("reason", problem.ToString()));
            BannerText = Text.Format(Strings.Catalogue_BannerText, ("time", Formats.RelativeTime(catalogue.FetchedAt!.Value, _services.Clock)));
            BannerIcon = problem is CatalogueErrorReason.Offline or CatalogueErrorReason.Timeout ? Icons.WifiOff : Icons.ServerOff;
        }

        Freshness = catalogue switch
        {
            { FetchedAt: null, Failure: not null } when catalogue.State != CatalogueState.Refreshing => CatalogueFreshness.None,
            { FetchedAt: null } => CatalogueFreshness.Loading,
            { State: CatalogueState.Refreshing } => CatalogueFreshness.Refreshing,
            _ when ShowsBanner => CatalogueFreshness.None,
            _ => CatalogueFreshness.Updated,
        };
        FreshnessText = Freshness switch
        {
            CatalogueFreshness.Loading => Strings.Catalogue_Loading,
            CatalogueFreshness.Refreshing => Strings.Catalogue_Refreshing,
            CatalogueFreshness.Updated => Text.Format(Strings.Catalogue_Updated, ("time", Formats.RelativeTime(catalogue.FetchedAt!.Value, _services.Clock))),
            _ => "",
        };
    }

    private CatalogueQuery CurrentQuery() => new()
    {
        Text = Query.Trim(),
        StatusUuid = Status,
        TeamUuid = Team,
        TypeUuid = Type,
        StorefrontUuid = Store,
        PlatformUuid = Platform,
        GenreUuid = Genre,
        IncludeAdult = IncludeAdult,
        Order = Order,
    };

    // Asks again from the top; keeping asks for as many games as the grid holds, so a refresh does not cut it short.
    private void Requery(bool keep)
    {
        if (_holding || !_services.Agent.HasState)
        {
            return;
        }

        _loadingMore = false;
        _ = LoadAsync(++_version, 0, keep ? Math.Max(PageSize, _games.Count) : PageSize);
    }

    // Asks until it has as many games as it wanted, since the agent cuts a long slice short, and puts them in the grid only
    // then, so a refresh of a long grid neither shortens it nor moves its scroll.
    private async Task LoadAsync(int version, int offset, int count)
    {
        var request = new GetCataloguePage(CurrentQuery(), offset, count, DetectedOnly);
        var games = new List<CatalogueGameView>();
        CataloguePageResult? page = null;
        while (games.Count < count)
        {
            var (_, answer) = await AgentRequests.AskAsync(_services, request with { Offset = offset + games.Count, Count = count - games.Count })
                .ConfigureAwait(true);
            if (version != _version)
            {
                return;
            }

            if (answer is not CataloguePageResult slice)
            {
                (_loadingMore, _unanswered) = (false, true);
                return;
            }

            page = slice;
            games.AddRange(slice.Games);
            if (slice.Games.Count == 0 || offset + games.Count >= slice.Total)
            {
                break;
            }
        }

        _loadingMore = false;
        if (offset == 0)
        {
            _games.Clear();
        }

        _games.AddRange(games);
        _page = page;
        HasAnswer = true;
        Compose();
    }

    private async Task LoadFiltersAsync()
    {
        var (_, answer) = await AgentRequests.AskAsync(_services, new GetCatalogueFilters()).ConfigureAwait(true);
        if (answer is CatalogueFiltersResult filters)
        {
            _filters = filters;
            Compose();
        }
        else
        {
            _unanswered = true;
        }
    }

    private void Compose()
    {
        var state = _services.Agent.State;
        var query = Query.Trim();
        var page = _page;
        HasFilters = Status is not null || Team is not null || Type is not null || Store is not null || Platform is not null || Genre is not null
            || DetectedOnly || IncludeAdult;
        Content = state.Catalogue.FetchedAt is null && state.Catalogue.Failure is not null && state.Catalogue.State != CatalogueState.Refreshing
                ? CatalogueContent.Unavailable
            : page is null || state.Catalogue.FetchedAt is null ? CatalogueContent.Loading
            : page.Total > 0 ? CatalogueContent.Games
            : query.Length > 0 && page.Unfiltered == 0 ? CatalogueContent.NoResults
            : HasFilters || query.Length > 0 ? CatalogueContent.FilteredOut
            : CatalogueContent.Empty;
        ShowEmptyState(query, page);
        ShowPills(page);
        ShowCards(state, query);
        ResultsText = page is null ? "" : Text.Format(Strings.Catalogue_Results, ("count", page.Total));
    }

    private void ShowEmptyState(string query, CataloguePageResult? page)
    {
        EmptyAction = null;
        switch (Content)
        {
            case CatalogueContent.NoResults:
                (EmptyIcon, EmptyTitle, EmptyMessage) = (Icons.SearchX, Text.Format(Strings.Catalogue_NoResultsTitle, ("query", query)),
                    Strings.Catalogue_NoResultsText);
                EmptyAction = Strings.Library_ClearSearch;
                break;
            case CatalogueContent.FilteredOut:
                var hasQuery = query.Length > 0 ? "true" : "false";
                (EmptyIcon, EmptyTitle, EmptyMessage) = (Icons.Funnel,
                    Text.Format(Strings.Catalogue_FilteredOutTitle, ("query", query), ("hasQuery", hasQuery)),
                    Text.Format(Strings.Catalogue_FilteredOutText, ("count", page?.Unfiltered ?? 0), ("hasQuery", hasQuery)));
                EmptyAction = Strings.Catalogue_ClearFilters;
                break;
            case CatalogueContent.Empty:
                (EmptyIcon, EmptyTitle, EmptyMessage) = (Icons.Library, Strings.Catalogue_EmptyTitle, "");
                break;
        }
    }

    private void ShowPills(CataloguePageResult? page)
    {
        var facets = page?.Facets ?? new CatalogueFacets();
        var whole = _filters?.Facets ?? facets;
        var filters = _filters?.Filters ?? new CatalogueFiltersView();
        AllCount = facets.Statuses.Total;
        DetectedCount = page?.Detected ?? 0;
        AdultCount = facets.Adult;
        OffersAdult = whole.Adult > 0 || IncludeAdult;

        // Statuses keep the site's order; the other values go by how many games they leave, as the site lists them.
        CardGrid.Sync(StatusOptions, Options(CatalogueFilter.Status, filters.Statuses, facets.Statuses, Status, byCount: false));
        CardGrid.Sync(StatusPills, [_allStatuses, .. StatusOptions, _statusesHelp]);
        CardGrid.Sync(TeamOptions, Options(CatalogueFilter.Team, filters.Teams, facets.Teams, Team, byCount: true));
        ShowsTeams = TeamOptions.Count > 0 && !ArePillsLoading;
        ShowsTeamLabel = ShowsTeams || ArePillsLoading;
        var shown = new List<FilterGroup>();
        foreach (var group in MoreGroups)
        {
            var (values, facet, wholeFacet, selected) = group.Filter switch
            {
                CatalogueFilter.Type => (filters.Types, facets.Types, whole.Types, Type),
                CatalogueFilter.Store => (filters.Stores, facets.Stores, whole.Stores, Store),
                CatalogueFilter.Platform => (filters.Platforms, facets.Platforms, whole.Platforms, Platform),
                _ => (filters.Genres, facets.Genres, whole.Genres, Genre),
            };
            CardGrid.Sync(group.Options, Options(group.Filter, values, facet, selected, byCount: true));
            // As on the site, a group shows only while some value is on some games but not on all of them.
            var narrows = values.Any(value => wholeFacet.CountOf(value.Id) is var count && count > 0 && count < wholeFacet.Total);
            if (group.Options.Count > 0 && (narrows || selected is not null))
            {
                shown.Add(group);
            }
        }

        CardGrid.Sync(ShownGroups, shown);
        ShowsMore = (ShownGroups.Count > 0 || OffersAdult) && !ArePillsLoading;
        ShowMoreText();
    }

    private void ShowMoreText()
    {
        var chosen = new[] { Type, Store, Platform, Genre }.Count(value => value is not null) + (IncludeAdult ? 1 : 0);
        MoreText = AreMoreOpen ? Strings.Catalogue_FewerFilters
            : Text.Format(Strings.Catalogue_MoreFilters, ("count", chosen), ("hasCount", chosen > 0 ? "true" : "false"));
    }

    private List<FilterOption> Options(CatalogueFilter filter, IReadOnlyList<CatalogueOptionView> values, CatalogueFacet facet, Guid? selected,
        bool byCount)
    {
        var options = values
            .Select(value => (Value: value, Count: facet.CountOf(value.Id)))
            .Where(entry => entry.Count > 0 || entry.Value.Id == selected)
            .Select(entry =>
            {
                if (!_options.TryGetValue((filter, entry.Value.Id), out var option))
                {
                    option = new FilterOption(filter, entry.Value.Id, entry.Value.Name, OnPicked) { Color = entry.Value.Color, Avatar = entry.Value.Avatar };
                    _options[(filter, entry.Value.Id)] = option;
                }

                option.Show(entry.Count, entry.Value.Id == selected);
                return option;
            });
        return byCount ? [.. options.OrderByDescending(option => option.Count).ThenBy(option => option.Name, Text.Comparer)] : [.. options];
    }

    private void OnPicked(FilterOption option, bool picked)
    {
        var value = picked ? option.Id : (Guid?)null;
        switch (option.Filter)
        {
            case CatalogueFilter.Status:
                Status = value;
                break;
            case CatalogueFilter.Team:
                Team = value;
                break;
            case CatalogueFilter.Type:
                Type = value;
                break;
            case CatalogueFilter.Store:
                Store = value;
                break;
            case CatalogueFilter.Platform:
                Platform = value;
                break;
            case CatalogueFilter.Genre:
                Genre = value;
                break;
        }
    }

    private void ShowCards(AppStateView state, string query)
    {
        var shown = new List<object>(_games.Count);
        foreach (var game in _games)
        {
            if (!_cards.TryGetValue(game.Id, out var card))
            {
                card = new CatalogueCardViewModel(game.Id);
                _cards[game.Id] = card;
            }

            card.Update(game, query, state, _services.Clock);
            shown.Add(card);
        }

        foreach (var gone in _cards.Keys.Except(_games.Select(game => game.Id)).ToList())
        {
            _cards.Remove(gone);
        }

        _grid.Show(shown);
    }
}
