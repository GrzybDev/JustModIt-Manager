using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>Why a detected game cannot be chosen for a package.</summary>
public enum GameBlock
{
    None,

    /// <summary>A package of this app is installed on it, whose files would count as the game's.</summary>
    Installed,

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

    public bool IsBlockedByInstall => Block == GameBlock.Installed;

    public bool IsNotReady => Block == GameBlock.NotReady;
}

/// <summary>Two games side by side, so a virtualising list lays the grid out row by row.</summary>
/// <param name="Left">The first game.</param>
/// <param name="Right">The second game; null on a last row of one.</param>
public sealed record GameOptionRow(GameOption Left, GameOption? Right);

/// <summary>
/// The first step: the detected game the package is made for, from the library's last scan, which may not be chosen
/// while a package is installed on it or it is not ready.
/// </summary>
public sealed partial class GameStepViewModel : ObservableObject, ICreatorStepView
{
    private readonly CreatorPageViewModel _page;
    private readonly CreatorSession _session;
    private List<GameOption> _options = [];
    private DateTimeOffset? _scannedAt;
    private Task? _loading;

    /// <summary>Creates the step for a project.</summary>
    /// <param name="page">The creator.</param>
    /// <param name="session">The project.</param>
    public GameStepViewModel(CreatorPageViewModel page, CreatorSession session)
    {
        _page = page;
        _session = session;
        _page.Agent.PropertyChanged += OnAgentChanged;
    }

    public ObservableCollection<GameOptionRow> Rows { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGames), nameof(ShowsNoResults))]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    public partial string Count { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNoResults))]
    public partial bool HasResults { get; private set; }

    public bool ShowsGames => IsLoaded;

    public bool ShowsNoResults => IsLoaded && !HasResults;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    public partial GameOption? Selected { get; private set; }

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    public void OnShown()
    {
        if (!IsLoaded)
        {
            _ = LoadAsync();
        }
    }

    public Task<bool> CanLeaveAsync() => Task.FromResult(true);

    public void Dispose() => _page.Agent.PropertyChanged -= OnAgentChanged;

    /// <summary>Reads the detected games from the agent once; the reading under way serves every caller.</summary>
    /// <returns>Completes once the games are shown.</returns>
    public Task LoadAsync() => _loading ??= ReadAsync();

    private async Task ReadAsync()
    {
        _scannedAt = _page.Agent.State.Library.ScannedAt;
        var (_, answer) = await _page.AskAsync(new GetDetectedGames()).ConfigureAwait(true);
        if (answer is DetectedGames detected)
        {
            var chosen = Selected?.Entry.Key;
            _options = [.. detected.Games.Select(entry => new GameOption(entry) { IsSelected = entry.Key == chosen })];
            Judge();
            if (_page.Game is { } game)
            {
                _page.IsGameOnOtherStores = IsOnOtherStores(game);
            }

            IsLoaded = true;
            Filter();
        }
        else
        {
            _loading = null;
        }
    }

    /// <summary>The game of a key among the detected ones; null when no store reports it.</summary>
    /// <param name="key">The game's key.</param>
    public DetectedGameEntry? Find(GameKey key) => _options.FirstOrDefault(option => option.Entry.Key == key)?.Entry;

    /// <summary>Makes a game the project's, forgetting a comparison with another game.</summary>
    /// <param name="entry">A detected game.</param>
    public void Choose(DetectedGameEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var project = _session.Project;
        if (project.Game?.Key != entry.Key || project.GameFolder != entry.Game.InstallDirectory)
        {
            _ = _session.UseAsync(null);
            _session.Change(current => current with
            {
                Game = new ProjectGame(entry.Key, entry.Name),
                GameFolder = entry.Game.InstallDirectory,
            });
        }

        _page.IsGameOnOtherStores = IsOnOtherStores(entry);
        _page.Game = entry;
        _page.ClearProblem();
        foreach (var option in _options)
        {
            option.IsSelected = option.Entry.Key == entry.Key;
        }

        Selected = _options.FirstOrDefault(option => option.IsSelected);
    }

    [RelayCommand]
    private void Pick(GameOption option)
    {
        if (option.IsSelectable)
        {
            Choose(option.Entry);
        }
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue() => _page.GoTo(CreatorStep.Files);

    private bool CanContinue() => Selected is { IsSelectable: true };

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        IsChecking = true;
        await _page.AskAsync(new CheckNow()).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ShowUndetected() => _page.Services.Navigator.Navigate(new UndetectedGamesRoute(ShellSection.Creator));

    [RelayCommand]
    private void OpenGame(GameOption option) => _page.Services.Navigator.Navigate(new GameRoute(option.Entry.Key));

    partial void OnQueryChanged(string value) => Filter();

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IAgent.State))
        {
            return;
        }

        var state = _page.Agent.State;
        IsChecking = state.Activity.IsChecking;
        if (IsLoaded && state.Library.ScannedAt != _scannedAt)
        {
            _loading = ReadAsync();
        }
        else
        {
            Judge();
        }
    }

    // A package on the game, or a store or job at work on it, would make its files no baseline for the package.
    private void Judge()
    {
        var state = _page.Agent.State;
        foreach (var option in _options)
        {
            var game = option.Entry;
            var install = state.Games.SelectMany(view => view.Installs)
                .FirstOrDefault(install => Core.IO.PathComparison.Overlaps(install.InstallDirectory, game.Game.InstallDirectory));
            var busy = state.Jobs.Any(job => job.Game == game.Key)
                || game.Game.State != GameInstallState.Installed || game.Game.IsStale;
            (option.Block, option.BlockText) = install is not null
                ? (GameBlock.Installed, InstalledText(install))
                : busy
                    ? (GameBlock.NotReady, Text.Format(Strings.Creator_GameNotReady,
                        ("state", GameWording.GameState(game.Game.State, game.Game.Reason, game.Key.Store))))
                    : (GameBlock.None, "");
        }

        Selected = _options.FirstOrDefault(option => option.IsSelected);
        ContinueCommand.NotifyCanExecuteChanged();
    }

    // Stores spell a title differently now and then, e.g. with a colon, so titles are compared as the search folds them.
    private bool IsOnOtherStores(DetectedGameEntry entry) =>
        _options.Any(option => option.Entry.Key.Store != entry.Key.Store
            && string.Equals(SearchText.Fold(option.Name), SearchText.Fold(entry.Name), StringComparison.Ordinal));

    private static string InstalledText(InstallView install)
    {
        if (install.IsTest)
        {
            return Strings.Creator_GameTestInstalled;
        }

        var version = GameWording.ShortVersion(GameWording.Version(install));
        return Text.Format(Strings.Creator_GameInstalled, ("project", install.TeamName ?? install.ProjectName ?? install.PackageFileName),
            ("hasVersion", version.Length > 0 ? "true" : "false"), ("version", version));
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
