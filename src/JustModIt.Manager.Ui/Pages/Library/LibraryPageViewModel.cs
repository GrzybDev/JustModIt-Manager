using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>Which games the grid shows.</summary>
public enum LibraryFilter
{
    All,
    Installed,
    Available,
    Hidden,
}

/// <summary>The order of the grid.</summary>
public enum LibrarySort
{
    Name,
    Recent,
}

/// <summary>What the page shows below its head: the games, or why there are none to show.</summary>
public enum LibraryContent
{
    /// <summary>Not yet known which games have projects; skeleton cards stand in, never an empty state.</summary>
    Loading,
    Games,

    /// <summary>The stores and the site were read and no game has a project.</summary>
    Empty,

    /// <summary>The site was never reached, so the games found cannot be matched yet.</summary>
    FoundOffline,
    AllHidden,
    NoResults,
    FilteredOut,
}

/// <summary>
/// The player's games: what needs the player first (the to-do panel), then the other games as cards, with search,
/// filters and order. Everything comes from the agent's state; the page keeps one card and one row per thing, so a new
/// state only changes what changed.
/// </summary>
public sealed partial class LibraryPageViewModel : PageViewModel, IDisposable
{
    /// <summary>How many rows the to-do panel shows before it offers to show them all.</summary>
    public const int CollapsedTodoRows = 5;

    private const int SkeletonCards = 10;

    private readonly UiServices _services;
    private readonly Dictionary<GameKey, GameCardViewModel> _cards = [];
    private readonly Dictionary<string, TodoRowViewModel> _rows = [];
    private readonly HelpTileViewModel _help = new();
    private readonly CardGrid _grid = new();
    private List<TodoRowViewModel> _allRows = [];

    /// <summary>Creates the page, which follows the agent's state from then on.</summary>
    /// <param name="services">The window's services.</param>
    public LibraryPageViewModel(UiServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _services.Agent.PropertyChanged += OnAgentChanged;
        _services.Agent.JobsProgressed += OnJobsProgressed;
        Skeletons = [.. Enumerable.Range(0, SkeletonCards)];
        Refresh();
    }

    public override string Title => Strings.Shell_Library;

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsAll), nameof(ShowsInstalled), nameof(ShowsAvailable), nameof(ShowsHidden))]
    public partial LibraryFilter Filter { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortsByName), nameof(SortsByRecent))]
    public partial LibrarySort Sort { get; set; }

    [ObservableProperty]
    public partial bool IsTodoExpanded { get; set; }

    /// <summary>The to-do rows shown: the first few, or all of them once expanded.</summary>
    public ObservableCollection<TodoRowViewModel> TodoRows { get; } = [];

    public ObservableCollection<CardRow> Rows => _grid.Rows;

    /// <summary>Stand-ins for the cards while loading.</summary>
    public IReadOnlyList<int> Skeletons { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGames), nameof(IsLoading), nameof(ShowsEmptyState), nameof(ShowsFilterBar), nameof(ShowsHelpRow))]
    public partial LibraryContent Content { get; private set; }

    [ObservableProperty]
    public partial bool ShowsTodo { get; private set; }

    [ObservableProperty]
    public partial bool ShowsAllCurrent { get; private set; }

    /// <summary>The panel holds more rows than it shows collapsed.</summary>
    [ObservableProperty]
    public partial bool ShowsTodoToggle { get; private set; }

    [ObservableProperty]
    public partial string TodoToggleText { get; private set; } = "";

    [ObservableProperty]
    public partial bool ShowsBatch { get; private set; }

    [ObservableProperty]
    public partial string BatchText { get; private set; } = "";

    [ObservableProperty]
    public partial int BatchCount { get; private set; }

    [ObservableProperty]
    public partial Geometry BatchIcon { get; private set; } = Icons.CheckCheck;

    /// <summary>The games the batch action sees to, one per line.</summary>
    [ObservableProperty]
    public partial string BatchTip { get; private set; } = "";

    [ObservableProperty]
    public partial string BatchName { get; private set; } = "";

    [ObservableProperty]
    public partial int AllCount { get; private set; }

    [ObservableProperty]
    public partial int InstalledCount { get; private set; }

    [ObservableProperty]
    public partial int AvailableCount { get; private set; }

    [ObservableProperty]
    public partial int HiddenCount { get; private set; }

    /// <summary>The hidden games' filter is offered while a game is hidden, or while it is the one chosen.</summary>
    [ObservableProperty]
    public partial bool ShowsHiddenFilter { get; private set; }

    [ObservableProperty]
    public partial bool ShowsSearch { get; private set; }

    [ObservableProperty]
    public partial bool IsOffline { get; private set; }

    [ObservableProperty]
    public partial string OfflineTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string OfflineMessage { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    [ObservableProperty]
    public partial string EmptyTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string EmptyMessage { get; private set; } = "";

    [ObservableProperty]
    public partial Geometry EmptyIcon { get; private set; } = Icons.Gamepad;

    /// <summary>The empty state's one way on; null for none.</summary>
    [ObservableProperty]
    public partial string? EmptyAction { get; private set; }

    public bool ShowsGames => Content == LibraryContent.Games;

    public bool IsLoading => Content == LibraryContent.Loading;

    public bool ShowsEmptyState => Content is not (LibraryContent.Games or LibraryContent.Loading);

    /// <summary>The filters stay while a search or a filter leaves nothing, so the way back is where it was.</summary>
    public bool ShowsFilterBar => Content is LibraryContent.Games or LibraryContent.FilteredOut or LibraryContent.AllHidden;

    /// <summary>Under an empty state the help leads to what detection found, since a missing game is the likely cause.</summary>
    public bool ShowsHelpRow => Content is LibraryContent.Empty or LibraryContent.NoResults or LibraryContent.FilteredOut;

    public bool ShowsAll
    {
        get => Filter == LibraryFilter.All;
        set => Pick(value, LibraryFilter.All);
    }

    public bool ShowsInstalled
    {
        get => Filter == LibraryFilter.Installed;
        set => Pick(value, LibraryFilter.Installed);
    }

    public bool ShowsAvailable
    {
        get => Filter == LibraryFilter.Available;
        set => Pick(value, LibraryFilter.Available);
    }

    public bool ShowsHidden
    {
        get => Filter == LibraryFilter.Hidden;
        set => Pick(value, LibraryFilter.Hidden);
    }

    public bool SortsByName
    {
        get => Sort == LibrarySort.Name;
        set
        {
            if (value)
            {
                Sort = LibrarySort.Name;
            }
        }
    }

    public bool SortsByRecent
    {
        get => Sort == LibrarySort.Recent;
        set
        {
            if (value)
            {
                Sort = LibrarySort.Recent;
            }
        }
    }

    /// <summary>How many cards fit in a row of the grid; the view sets it from its width.</summary>
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

    /// <summary>The grid's cards in its order, then the help tile.</summary>
    public IReadOnlyList<object> GridItems => _grid.Items;

    /// <summary>The grid the view lays out and moves the keyboard through.</summary>
    public CardGrid Grid => _grid;

    public override void OnShown() => Refresh();

    public void Dispose()
    {
        _services.Agent.PropertyChanged -= OnAgentChanged;
        _services.Agent.JobsProgressed -= OnJobsProgressed;
    }

    partial void OnQueryChanged(string value) => Refresh();

    partial void OnFilterChanged(LibraryFilter value) => Refresh();

    partial void OnSortChanged(LibrarySort value) => Refresh();

    partial void OnIsTodoExpandedChanged(bool value) => Refresh();

    [RelayCommand]
    private async Task CheckNowAsync() => await AgentRequests.SendAsync(_services, new CheckNow()).ConfigureAwait(true);

    [RelayCommand]
    private void OpenGame(GameKey game) => _services.Navigator.Navigate(new GameRoute(game));

    [RelayCommand]
    private void OpenUndetected() => _services.Navigator.Navigate(new UndetectedGamesRoute());

    [RelayCommand]
    private void ClearSearch() => Query = "";

    [RelayCommand]
    private void ShowEverything()
    {
        Query = "";
        Filter = LibraryFilter.All;
    }

    [RelayCommand]
    private void ToggleTodo() => IsTodoExpanded = !IsTodoExpanded;

    [RelayCommand]
    private void RunEmptyAction()
    {
        if (Content == LibraryContent.NoResults)
        {
            ClearSearch();
        }
        else
        {
            ShowEverything();
        }
    }

    [RelayCommand]
    private async Task ActAsync(TodoRowViewModel row)
    {
        if (row.Request is not { } request)
        {
            OpenGame(row.Game);
            return;
        }

        if (row.IsDanger && !await _services.Dialogs.ConfirmAsync(new ConfirmDialogViewModel(Strings.GamePage_UninstallTitle,
                Text.Format(Strings.GamePage_UninstallMessage, ("game", row.Name)), Strings.GamePage_Uninstall, Icons.Trash, DialogTone.Danger)
            {
                ConfirmIcon = Icons.Trash,
            }).ConfigureAwait(true))
        {
            return;
        }

        await AgentRequests.SendAsync(_services, request).ConfigureAwait(true);
    }

    // One request per game, in the panel's order; the counted rows include those the collapsed panel hides.
    [RelayCommand]
    private async Task RunBatchAsync()
    {
        foreach (var row in BatchRows().ToList())
        {
            if (!await AgentRequests.SendAsync(_services, row.Request!).ConfigureAwait(true))
            {
                return;
            }
        }
    }

    private IEnumerable<TodoRowViewModel> BatchRows() =>
        _allRows.Where(row => row.Kind is TodoKind.Repair or TodoKind.Update && !row.WaitsForConnection && row.Request is not null);

    private void Pick(bool value, LibraryFilter filter)
    {
        if (value)
        {
            Filter = filter;
        }
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IAgent.State) or nameof(IAgent.HasState))
        {
            Refresh();
        }
    }

    private void OnJobsProgressed(object? sender, IReadOnlyList<JobView> jobs)
    {
        foreach (var job in jobs)
        {
            if (_cards.TryGetValue(job.Game, out var card))
            {
                card.Apply(job, job.Game.Store);
            }
        }
    }

    private void Refresh()
    {
        var state = _services.Agent.State;
        IsChecking = state.Activity.IsChecking;
        ShowOffline(state);

        var visible = state.Games.Where(game => !game.IsHidden).ToList();
        var hidden = state.Games.Where(game => game.IsHidden).ToList();
        foreach (var game in state.Games)
        {
            if (!_cards.TryGetValue(game.Key, out var card))
            {
                card = new GameCardViewModel(game.Key);
                _cards[game.Key] = card;
            }

            card.Update(game, state);
        }

        foreach (var gone in _cards.Keys.Except(state.Games.Select(game => game.Key)).ToList())
        {
            _cards.Remove(gone);
        }

        var query = SearchText.Fold(Query);
        bool Matches(string text) => query.Length == 0 || SearchText.Fold(text).Contains(query, StringComparison.Ordinal);

        _allRows = TodoRowViewModel.Build(state, _services.Clock, _rows);
        _rows.Clear();
        foreach (var row in _allRows)
        {
            _rows[row.Key] = row;
        }

        var matchingRows = _allRows.Where(row => Matches(_cards[row.Game].SearchText)).ToList();
        var inPanel = _allRows.Select(row => row.Game).ToHashSet();
        var rest = visible.Where(game => !inPanel.Contains(game.Key)).Select(game => (Game: game, Card: _cards[game.Key])).ToList();
        AllCount = rest.Count(entry => Matches(entry.Card.SearchText));
        InstalledCount = rest.Count(entry => entry.Game.Installs.Count > 0 && Matches(entry.Card.SearchText));
        AvailableCount = rest.Count(entry => entry.Game.Installs.Count == 0 && Matches(entry.Card.SearchText));
        HiddenCount = hidden.Count(game => Matches(_cards[game.Key].SearchText));
        ShowsHiddenFilter = hidden.Count > 0 || Filter == LibraryFilter.Hidden;
        if (Filter == LibraryFilter.Hidden && hidden.Count == 0 && Query.Length == 0)
        {
            Filter = LibraryFilter.All;
            return;
        }

        var shown = (Filter switch
        {
            LibraryFilter.Installed => rest.Where(entry => entry.Game.Installs.Count > 0),
            LibraryFilter.Available => rest.Where(entry => entry.Game.Installs.Count == 0),
            LibraryFilter.Hidden => hidden.Select(game => (Game: game, Card: _cards[game.Key])),
            _ => rest,
        }).Where(entry => Matches(entry.Card.SearchText)).Select(entry => entry.Card);
        var ordered = Sort == LibrarySort.Recent
            ? shown.OrderByDescending(card => card.ReleasedAt ?? DateTimeOffset.MinValue).ThenBy(card => card.Name, Text.Comparer)
            : shown.OrderBy(card => card.Name, Text.Comparer);
        List<object> gridItems = [.. ordered];

        var known = state.IsLoaded && state.Library.ScannedAt is not null && (state.Catalogue.FetchedAt is not null || state.Catalogue.Failure is not null);
        Content = !_services.Agent.HasState || (state.Games.Count == 0 && !known) ? LibraryContent.Loading
            : state.Games.Count == 0 ? (state.Catalogue.FetchedAt is null ? LibraryContent.FoundOffline : LibraryContent.Empty)
            : matchingRows.Count == 0 && gridItems.Count == 0 && query.Length > 0 && (Filter == LibraryFilter.All || AllCount + HiddenCount == 0)
                ? LibraryContent.NoResults
            : visible.Count == 0 && Filter != LibraryFilter.Hidden ? LibraryContent.AllHidden
            : gridItems.Count == 0 && (Filter != LibraryFilter.All || query.Length > 0) ? LibraryContent.FilteredOut
            : LibraryContent.Games;
        if (Content == LibraryContent.Games && Filter == LibraryFilter.All)
        {
            gridItems.Add(_help);
        }

        ShowEmptyState(Query.Trim());
        ShowsSearch = state.Games.Count > 0 || Content == LibraryContent.Loading;

        var panel = Content is LibraryContent.Games or LibraryContent.FilteredOut ? matchingRows : [];
        ShowsTodo = panel.Count > 0;
        ShowsAllCurrent = Content is LibraryContent.Games && _allRows.Count == 0 && query.Length == 0 && Filter == LibraryFilter.All;
        ShowsTodoToggle = panel.Count > CollapsedTodoRows;
        TodoToggleText = IsTodoExpanded ? Strings.Library_ShowLess : Text.Format(Strings.Library_ShowAllCount, ("count", panel.Count));
        CardGrid.Sync(TodoRows, IsTodoExpanded ? panel : [.. panel.Take(CollapsedTodoRows)]);
        ShowBatch();
        _grid.Show(gridItems);
    }

    private void ShowOffline(AppStateView state)
    {
        IsOffline = state.Catalogue.Failure is not null && state.Games.Count > 0;
        OfflineTitle = state.Catalogue.Failure == CatalogueErrorReason.Maintenance ? Strings.Library_MaintenanceTitle : Strings.Library_OfflineTitle;
        OfflineMessage = state.Catalogue.FetchedAt is { } fetched
            ? Text.Format(Strings.Library_OfflineMessage, ("time", Formats.RelativeTime(fetched, _services.Clock)))
            : Strings.Library_OfflineNever;
    }

    private void ShowEmptyState(string query)
    {
        EmptyAction = null;
        switch (Content)
        {
            case LibraryContent.Empty:
                (EmptyIcon, EmptyTitle, EmptyMessage) = (Icons.Gamepad, Strings.Library_EmptyTitle, Strings.Library_EmptyMessage);
                break;
            case LibraryContent.FoundOffline:
                (EmptyIcon, EmptyTitle, EmptyMessage) = (Icons.Gamepad, Strings.Library_FoundOfflineTitle, Strings.Library_FoundOfflineMessage);
                break;
            case LibraryContent.AllHidden:
                (EmptyIcon, EmptyTitle, EmptyMessage) = (Icons.EyeOff, Strings.Library_AllHiddenTitle, Strings.Library_AllHiddenMessage);
                break;
            case LibraryContent.NoResults:
                (EmptyIcon, EmptyTitle, EmptyMessage) = (Icons.Search, Strings.Library_NoResultsTitle,
                    Text.Format(Strings.Library_NoResults, ("query", query)));
                EmptyAction = Strings.Library_ClearSearch;
                break;
            case LibraryContent.FilteredOut:
                (EmptyIcon, EmptyTitle, EmptyMessage) = (Icons.Funnel, Strings.Library_FilteredOutTitle,
                    Text.Format(Strings.Library_FilteredOut, ("filter", Filter.ToString()), ("query", query),
                        ("hasQuery", query.Length > 0 ? "true" : "false")));
                EmptyAction = Strings.Library_ShowAll;
                break;
        }
    }

    private void ShowBatch()
    {
        var batch = BatchRows().ToList();
        var waiting = _allRows.Where(row => row.WaitsForConnection).ToList();
        var repairs = batch.Any(row => row.Kind == TodoKind.Repair);
        var updates = batch.Any(row => row.Kind == TodoKind.Update);
        ShowsBatch = batch.Count > 0 && ShowsTodo;
        BatchCount = batch.Count;
        (BatchText, BatchIcon) = (repairs, updates) switch
        {
            (true, true) => (Strings.Library_BatchRepairUpdate, Icons.CheckCheck),
            (true, false) => (Strings.Library_BatchRepair, Icons.Wrench),
            _ => (Strings.Library_BatchUpdate, Icons.CircleArrowUp),
        };
        BatchTip = string.Join(Environment.NewLine, batch.Concat(waiting).Select(row => row.BatchLine));
        BatchName = Text.Format(Strings.Library_BatchName, ("action", BatchText), ("games", string.Join(", ", batch.Select(row => row.Name))));
    }
}
