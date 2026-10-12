using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Controls.Markdown;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Undetected;

/// <summary>What one store integration found, beside the site's tip on adding a game to that store.</summary>
/// <param name="Store">The store's id, for its mark.</param>
/// <param name="Name">The store's or its launcher's name.</param>
/// <param name="Detail">How many libraries and games were found, or that the store was not.</param>
/// <param name="Warnings">What could not be read or is not connected, each in a sentence.</param>
/// <param name="Hint">The site's tip in Markdown; null when the site gives none.</param>
/// <param name="Client">The store's client to open; null when detection found none the window can open.</param>
public sealed record StoreSection(string Store, string Name, string Detail, IReadOnlyList<string> Warnings, MarkdownDocument? Hint,
    ClientLink? Client)
{
    public bool HasHint => Hint is not null;

    public string OpenText { get; } = Client is null ? "" : Text.Format(Strings.Undetected_OpenStore, ("store", Client.Name));
}

/// <summary>
/// The help for a game that is missing (the "can't see your game?" panel): what each store integration found, the site's
/// tips on adding a game in the store, and the one fix the stores' own files allow, adding the folder of a library or a
/// game that only those files can confirm.
/// </summary>
public sealed partial class UndetectedGamesViewModel : PageViewModel, IDisposable
{
    private readonly UiServices _services;
    private DateTimeOffset? _reportFor;
    private DetectionReportView? _report;
    private LibraryFolderAdded? _added;

    /// <summary>Creates the page, which follows the agent's scans from then on.</summary>
    /// <param name="services">The window's services.</param>
    public UndetectedGamesViewModel(UiServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _services.Agent.PropertyChanged += OnAgentChanged;
        ShowScan();
    }

    public override string Title => Strings.Shell_PageUndetected;

    public string FoundHeading { get; } = Strings.Undetected_Found.ToUpper(Text.Culture);

    public string HowToHeading { get; } = Strings.Undetected_HowTo.ToUpper(Text.Culture);

    public ObservableCollection<StoreSection> Stores { get; } = [];

    /// <summary>The report is being read for the first time.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; } = true;

    [ObservableProperty]
    public partial string ScannedText { get; private set; } = "";

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    /// <summary>A picked folder is being checked by the stores.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLibraryCommand), nameof(AddGameCommand))]
    public partial bool IsAdding { get; private set; }

    /// <summary>What adding the last picked folder came to; null before a folder was picked.</summary>
    [ObservableProperty]
    public partial string? Result { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSuccess), nameof(IsWarning), nameof(ResultIcon))]
    public partial Tone ResultTone { get; private set; }

    public bool IsSuccess => ResultTone == Tone.Success;

    public bool IsWarning => ResultTone == Tone.Warning;

    public Geometry ResultIcon => ResultTone switch
    {
        Tone.Success => Icons.CircleCheck,
        Tone.Warning => Icons.TriangleAlert,
        _ => Icons.Info,
    };

    public override void OnShown()
    {
        _reportFor = null;
        ShowScan();
    }

    public void Dispose() => _services.Agent.PropertyChanged -= OnAgentChanged;

    [RelayCommand]
    private async Task CheckNowAsync() => await AgentRequests.SendAsync(_services, new CheckNow()).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddLibraryAsync() => AddAsync(Strings.Undetected_PickLibraryTitle);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private Task AddGameAsync() => AddAsync(Strings.Undetected_PickGameTitle);

    [RelayCommand]
    private void OpenLink(Uri? address)
    {
        if (address is not null)
        {
            _services.Links.Open(address);
        }
    }

    [RelayCommand]
    private void OpenClient(ClientLink? client)
    {
        if (client is not null)
        {
            _services.Links.Open(client);
        }
    }

    private bool CanAdd() => !IsAdding;

    private async Task AddAsync(string title)
    {
        if (await _services.Pickers.PickFolderAsync(title).ConfigureAwait(true) is not { } folder)
        {
            return;
        }

        IsAdding = true;
        try
        {
            var (sent, answer) = await AgentRequests.AskAsync(_services, new AddLibraryFolder(folder)).ConfigureAwait(true);
            if (sent && answer is LibraryFolderAdded added)
            {
                _added = added;
                ShowResult();
            }
        }
        finally
        {
            IsAdding = false;
        }
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IAgent.State))
        {
            ShowScan();
        }
    }

    private void ShowScan()
    {
        var state = _services.Agent.State;
        IsChecking = state.Activity.IsChecking;
        ScannedText = state.Library.ScannedAt is { } scannedAt
            ? Text.Format(Strings.Undetected_ScannedAt, ("time", Formats.RelativeTime(scannedAt, _services.Clock)))
            : Strings.Undetected_NotScanned;
        if (state.Library.ScannedAt != _reportFor || _report is null)
        {
            _reportFor = state.Library.ScannedAt;
            _ = LoadReportAsync();
        }
    }

    private async Task LoadReportAsync()
    {
        try
        {
            if (await _services.Agent.RequestAsync(new GetDetectionReport()).ConfigureAwait(true) is not DetectionReportResult result)
            {
                return;
            }

            var hints = await Task.Run(() => result.Report.Stores
                .Select(store => store.InstallHint.Length > 0 ? MarkdownDocument.Parse(store.InstallHint) : null).ToList()).ConfigureAwait(true);
            _report = result.Report;
            var games = _services.Agent.State.Games;
            Stores.Clear();
            for (var index = 0; index < _report.Stores.Count; index++)
            {
                Stores.Add(Section(_report.Stores[index], hints[index], games));
            }

            IsLoading = false;
            ShowResult();
        }
        // The page keeps what it showed; the next scan reads the report again.
        catch (Exception exception) when (exception is IOException or TimeoutException or RequestFailedException)
        {
            _reportFor = null;
        }
    }

    private static StoreSection Section(StoreDetectionView store, MarkdownDocument? hint, IReadOnlyList<GameView> games)
    {
        var known = StoreId.TryParse(store.Store, out var id);
        var name = store.Launcher is { Length: > 0 } launcher ? launcher
            : store.Name is { Length: > 0 } site ? site
            : known ? GameWording.StoreName(id) : store.Store;
        var offered = games.Count(game => game.Key.Store.Value == store.Store && game.Detected is not null);
        var detail = !store.IsFound ? Strings.Undetected_StoreMissing
            : store.ScanFailed ? Strings.Undetected_StoreUnreadable
            : string.Join(" ", new[]
            {
                Text.Format(Strings.Undetected_StoreFound, ("libraries", store.Libraries.Count), ("games", store.GameCount)),
                offered > 0 ? Text.Format(Strings.Undetected_StoreOffered, ("offered", offered)) : "",
            }.Where(part => part.Length > 0));
        var warnings = new List<string>();
        foreach (var library in store.Libraries)
        {
            var place = library.Path ?? name;
            if (library.State == LibraryState.Offline)
            {
                warnings.Add(Text.Format(Strings.Undetected_LibraryOffline, ("library", place)));
            }
            else if (library.State == LibraryState.Unreadable)
            {
                warnings.Add(Text.Format(Strings.Undetected_LibraryUnreadable, ("library", place)));
            }

            if (library.UnreadableEntries > 0)
            {
                warnings.Add(Text.Format(Strings.Undetected_UnreadableEntries, ("count", library.UnreadableEntries), ("library", place)));
            }
        }

        return new StoreSection(store.Store, name, detail, warnings, hint, PageLinks.StoreClient(store));
    }

    private void ShowResult()
    {
        if (_added is null)
        {
            Result = null;
            return;
        }

        if (_added.Rejection is { } rejection)
        {
            ResultTone = rejection is LibraryFolderRejection.AlreadyAdded or LibraryFolderRejection.AlreadyDetected ? Tone.Info : Tone.Warning;
            Result = Text.Format(Strings.Undetected_Rejection, ("rejection", rejection.ToString()), ("library", Strings.Shell_Library),
                ("settings", Strings.Shell_Settings));
            return;
        }

        var path = _added.Path ?? "";
        var library = _report?.Stores.Where(store => store.Store == _added.Store).SelectMany(store => store.Libraries)
            .FirstOrDefault(found => found.IsAdded && string.Equals(found.Path, path, StringComparison.OrdinalIgnoreCase));
        if (library is null)
        {
            (ResultTone, Result) = (Tone.Success, Text.Format(Strings.Undetected_Added, ("folder", path)));
            return;
        }

        var offered = _services.Agent.State.Games.Count(game => game.Detected?.LibraryId == library.Id);
        (ResultTone, Result) = offered > 0
            ? (Tone.Success, Text.Format(Strings.Undetected_AddedGames, ("folder", path), ("games", library.GameCount), ("offered", offered)))
            : (Tone.Info, Text.Format(Strings.Undetected_AddedNothingOffered, ("folder", path), ("games", library.GameCount)));
    }
}
