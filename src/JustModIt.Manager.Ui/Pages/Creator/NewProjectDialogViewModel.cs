using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>Why a detected game cannot be chosen for a package.</summary>
public enum GameBlock
{
    None,

    /// <summary>A package of this app is installed on it, whose files would count as the game's.</summary>
    Installed,

    /// <summary>A package of the creator's is installed on it for a test.</summary>
    TestInstalled,

    /// <summary>The store or a job is working on it, or it is not installed and idle.</summary>
    NotReady,
}

/// <summary>A detected game in the creator's list.</summary>
/// <param name="entry">The game as the agent reported it.</param>
public sealed partial class GameOption(DetectedGameEntry entry) : ObservableObject
{
    public DetectedGameEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    public string Store => Entry.Key.Store.Value;

    public string StoreName => GameWording.StoreName(Entry.Key.Store);

    public string Folder => Entry.Game.InstallDirectory;

    public string Cover => Entry.Cover;

    public bool HasCover => Cover.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectable), nameof(IsBlockedByInstall), nameof(IsNotReady))]
    public partial GameBlock Block { get; set; }

    /// <summary>What blocks the game, e.g. the install on it.</summary>
    [ObservableProperty]
    public partial string BlockText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public bool IsSelectable => Block == GameBlock.None;

    public bool IsBlockedByInstall => Block is GameBlock.Installed or GameBlock.TestInstalled;

    public bool IsNotReady => Block == GameBlock.NotReady;
}

/// <summary>Two games side by side, so a virtualising list lays the grid out row by row.</summary>
/// <param name="Left">The first game.</param>
/// <param name="Right">The second game; null on a last row of one.</param>
public sealed record GameOptionRow(GameOption Left, GameOption? Right);

/// <summary>Where the list of detected games stands.</summary>
public enum GameListState
{
    /// <summary>The agent has not answered yet, or has reported no game before the stores' first look.</summary>
    Loading,

    /// <summary>The agent gave no answer.</summary>
    Failed,

    /// <summary>The stores looked and reported no game.</summary>
    NoGames,
    Listed,
}

/// <summary>What the author chose for a new project.</summary>
/// <param name="Game">The detected game the project is made for.</param>
public sealed record NewProjectAnswer(DetectedGameEntry Game);

/// <summary>
/// The dialog that gives a project its game: the detected games, from the library's last scan, of which one may not be chosen
/// while a package is installed on it or it is not ready.
/// </summary>
public sealed partial class NewProjectDialogViewModel : DialogViewModel<NewProjectAnswer?>, IDisposable
{
    private readonly UiServices _services;
    private readonly IReadOnlyCollection<GameKey>? _versions;
    private List<GameOption> _options = [];
    private DateTimeOffset? _scannedAt;

    /// <summary>Opens the dialog, which reads the detected games at once.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="folder">The author's folder; empty for a project without one.</param>
    /// <param name="versions">The versions of a project that takes another, which the list leaves out; null for a new project.</param>
    public NewProjectDialogViewModel(UiServices services, string folder, IReadOnlyCollection<GameKey>? versions = null) : base(null)
    {
        _services = services;
        Folder = folder;
        _versions = versions;
        (Title, CreateText) = versions is null ? (Strings.Creator_NewTitle, Strings.Creator_Create) : (Strings.Creator_AddGameVersion, Strings.Creator_AddGameVersion);
        _services.Agent.PropertyChanged += OnAgentChanged;
        _ = ReadAsync();
    }

    public string Folder { get; }

    public string Title { get; }

    /// <summary>The label of the way on: to create a project, or to add the game to one.</summary>
    public string CreateText { get; }

    public bool HasFolder => Folder.Length > 0;

    public ObservableCollection<GameOptionRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(HasFailed), nameof(HasNoGames), nameof(IsListed), nameof(ShowsSearch), nameof(ShowsNoResults))]
    public partial GameListState State { get; private set; }

    public bool IsLoading => State == GameListState.Loading;

    public bool HasFailed => State == GameListState.Failed;

    public bool HasNoGames => State == GameListState.NoGames;

    public bool IsListed => State == GameListState.Listed;

    public bool ShowsSearch => State is GameListState.Loading or GameListState.Listed;

    [ObservableProperty]
    public partial string Count { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNoResults))]
    public partial bool HasResults { get; private set; }

    /// <summary>The search leaves none of the games.</summary>
    public bool ShowsNoResults => IsListed && !HasResults;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial GameOption? Selected { get; private set; }

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    /// <summary>Tells whether a game can be what a package is compared with now.</summary>
    /// <param name="game">A detected game.</param>
    /// <param name="state">The agent's state.</param>
    /// <param name="notReady">The words for a game the store or a job works on, around the game's state.</param>
    /// <returns>What blocks the game, and the words for it.</returns>
    public static (GameBlock Block, string Text) Judge(DetectedGameEntry game, AppStateView state, string notReady)
    {
        // A package on the game, or a store or job at work on it, would make its files no baseline for the package.
        var install = state.Games.SelectMany(view => view.Installs)
            .FirstOrDefault(install => Core.IO.PathComparison.Overlaps(install.InstallDirectory, game.Game.InstallDirectory));
        if (install is { IsTest: true })
        {
            return (GameBlock.TestInstalled, Strings.Creator_GameTestInstalled);
        }

        if (install is not null)
        {
            var version = GameWording.ShortVersion(GameWording.Version(install));
            return (GameBlock.Installed, Text.Format(Strings.Creator_GameInstalled, ("project", install.TeamName ?? install.ProjectName ?? install.PackageFileName),
                ("hasVersion", version.Length > 0 ? "true" : "false"), ("version", version)));
        }

        return state.Jobs.Any(job => job.Game == game.Key) || game.Game.State != GameInstallState.Installed || game.Game.IsStale
            ? (GameBlock.NotReady, Text.Format(notReady, ("state", GameWording.GameState(game.Game.State, game.Game.Reason, game.Key.Store))))
            : (GameBlock.None, "");
    }

    public void Dispose() => _services.Agent.PropertyChanged -= OnAgentChanged;

    [RelayCommand]
    private async Task ReadAsync()
    {
        if (HasFailed)
        {
            State = GameListState.Loading;
        }

        _scannedAt = _services.Agent.State.Library.ScannedAt;
        if (await AgentRequests.AskQuietlyAsync(_services.Agent, new GetDetectedGames()).ConfigureAwait(true) is DetectedGames detected)
        {
            var chosen = Selected?.Entry.Key;
            _options = [.. detected.Games.Where(entry => _versions?.Contains(entry.Key) != true).Select(entry => new GameOption(entry) { IsSelected = entry.Key == chosen })];
            ShowJudged();
            State = _options.Count > 0 ? GameListState.Listed
                : _scannedAt is null ? GameListState.Loading
                : GameListState.NoGames;
            Filter();
        }
        else if (!IsListed)
        {
            State = GameListState.Failed;
        }
    }

    [RelayCommand]
    private void Pick(GameOption option)
    {
        if (option.IsSelectable)
        {
            foreach (var other in _options)
            {
                other.IsSelected = other == option;
            }

            Selected = option;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => Complete(new NewProjectAnswer(Selected!.Entry));

    private bool CanCreate() => Selected is { IsSelectable: true };

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        IsChecking = true;
        await AgentRequests.AskAsync(_services, new CheckNow()).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ShowUndetected()
    {
        Cancel();
        _services.Navigator.Navigate(new UndetectedGamesRoute(ShellSection.Creator));
    }

    [RelayCommand]
    private void OpenGame(GameOption option)
    {
        Cancel();
        _services.Navigator.Navigate(new GameRoute(option.Entry.Key));
    }

    partial void OnQueryChanged(string value) => Filter();

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IAgent.State))
        {
            return;
        }

        var state = _services.Agent.State;
        IsChecking = state.Activity.IsChecking;
        if (state.Library.ScannedAt != _scannedAt)
        {
            _ = ReadAsync();
        }
        else
        {
            ShowJudged();
        }
    }

    private void ShowJudged()
    {
        foreach (var option in _options)
        {
            (option.Block, option.BlockText) = Judge(option.Entry, _services.Agent.State, Strings.Creator_GameNotReady);
        }

        Selected = _options.FirstOrDefault(option => option.IsSelected);
        CreateCommand.NotifyCanExecuteChanged();
    }

    private void Filter()
    {
        var query = SearchText.Fold(Query);
        var shown = _options.Where(option => query.Length == 0 || SearchText.Fold(option.Name).Contains(query, StringComparison.Ordinal))
            .ToList();
        Rows.Clear();
        for (var i = 0; i < shown.Count; i += 2)
        {
            Rows.Add(new GameOptionRow(shown[i], i + 1 < shown.Count ? shown[i + 1] : null));
        }

        HasResults = shown.Count > 0;
        Count = Text.Format(Strings.Creator_GameCount, ("count", _options.Count));
    }
}
