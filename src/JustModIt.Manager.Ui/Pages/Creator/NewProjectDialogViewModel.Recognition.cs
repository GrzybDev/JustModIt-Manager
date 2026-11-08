using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>An add-on of the site's project that a new project takes once it has a folder, dropped on its row or picked.</summary>
public sealed partial class PartOption : ObservableObject
{
    private readonly NewProjectDialogViewModel _dialog;

    /// <summary>Offers an add-on the site names.</summary>
    /// <param name="dialog">The dialog, which judges a folder given.</param>
    /// <param name="name">The add-on's name on the site.</param>
    /// <param name="from">Which release offers it.</param>
    internal PartOption(NewProjectDialogViewModel dialog, string name, string from)
    {
        _dialog = dialog;
        Name = name;
        DropText = GameWording.Join([from, Strings.Creator_FeatureFolder]);
    }

    public string Name { get; }

    /// <summary>Which release offers the add-on and that its folder can be dropped, before the way to pick one.</summary>
    public string DropText { get; }

    /// <summary>The picker's title, which screen readers read as the way to pick.</summary>
    public string PickName => Text.Format(Strings.Creator_PickFeatureName, ("name", Name));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolder))]
    public partial string Folder { get; internal set; } = "";

    public bool HasFolder => Folder.Length > 0;

    [RelayCommand]
    private void Drop(string? folder) => _dialog.TakeFolder(this, folder);
}

// The game a new project's files fit, recognised off the UI thread, with its versions on this computer and the add-ons of its
// project on the site.
public sealed partial class NewProjectDialogViewModel
{
    private readonly CancellationTokenSource _closing = new();
    private readonly HashSet<GameKey> _turnedOff = [];
    private readonly Dictionary<GameKey, bool> _otherLayout = [];
    private CancellationTokenSource? _layouts;
    private Recognition? _recognition;
    private int _recognitions;
    private int _candidates = -1;
    private bool _byAuthor;
    private GameKey? _shownKey;
    private List<GameOption> _copies = [];
    private ProjectHints? _hints;

    /// <summary>The game whose versions the project takes; null while its files are being recognised.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShown))]
    public partial GameOption? Shown { get; private set; }

    public bool HasShown => Shown is not null;

    /// <summary>The dialog shows the game a new project takes, or that it is being recognised.</summary>
    public bool ShowsGame => !ShowsList && State is GameListState.Loading or GameListState.Listed;

    /// <summary>The files fit no detected game, which the list says above the games.</summary>
    [ObservableProperty]
    public partial bool IsNotRecognized { get; private set; }

    /// <summary>The site's project of the game and its last release; empty while none is surely the author's.</summary>
    [ObservableProperty]
    public partial string SiteLine { get; private set; } = "";

    /// <summary>How many files the folder holds and whether they told the game, after the folder's path.</summary>
    [ObservableProperty]
    public partial string FolderNote { get; private set; } = "";

    /// <summary>The game's versions on this computer that a project can take, each a tile the author turns off or on.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GameOption> VersionTiles { get; private set; } = [];

    /// <summary>The game's versions that cannot be compared now, each saying why.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<GameOption> BlockedVersions { get; private set; } = [];

    /// <summary>The add-ons of the site's project, which the project takes once each has a folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PartsTitle))]
    public partial IReadOnlyList<PartOption> Parts { get; private set; } = [];

    public string PartsTitle => GameWording.Join([Strings.Creator_Parts, Parts.Count > 0 ? Strings.Creator_PartsFromSite : ""]);

    /// <summary>Why the folder given to an add-on is refused; empty while nothing is.</summary>
    [ObservableProperty]
    public partial string PartProblemTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string PartProblemText { get; private set; } = "";

    /// <summary>Gives an add-on a folder, unless it is in or around a game's or another part's.</summary>
    /// <param name="part">The add-on.</param>
    /// <param name="folder">The folder; null when files were dropped instead.</param>
    internal void TakeFolder(PartOption part, string? folder)
    {
        (PartProblemTitle, PartProblemText) = folder is null ? (Strings.Creator_DroppedFilesTitle, Strings.Creator_DroppedFiles)
            : _options.FirstOrDefault(game => PathComparison.Overlaps(game.Folder, folder)) is { } game
                ? (PathComparison.AreSame(game.Folder, folder) ? Strings.Creator_GameFolderTitle : Strings.Creator_InsideGameTitle, Strings.Creator_GameFolder)
            : new CreatorProject { Parts = [.. PartsOf(besides: part)] }.IsTaken(part.Name, folder) ? ("", Strings.Creator_FolderTaken)
            : ("", "");
        if (PartProblemText.Length == 0)
        {
            part.Folder = folder!;
        }
    }

    [RelayCommand]
    private async Task PickPartAsync(PartOption part)
    {
        if (await _services.Pickers.PickFolderAsync(part.PickName, Folder).ConfigureAwait(true) is { } folder)
        {
            TakeFolder(part, folder);
        }
    }

    [RelayCommand]
    private void ShowList() => (_byAuthor, ShowsList) = (true, true);

    [RelayCommand]
    private void ToggleVersion(GameOption copy)
    {
        if (!_turnedOff.Remove(copy.Entry.Key))
        {
            _turnedOff.Add(copy.Entry.Key);
        }

        ShowVersions();
    }

    // A recognition that looked at no game, e.g. while the list could not be read, is made again once there are games.
    private void Recognize()
    {
        if (_versions is null && (_candidates < 0 || (_candidates == 0 && _options.Count > 0)))
        {
            _ = RecognizeAsync([.. _options]);
        }
    }

    private async Task RecognizeAsync(IReadOnlyList<GameOption> games)
    {
        var run = ++_recognitions;
        _candidates = games.Count;
        Recognition? recognition = null;
        try
        {
            recognition = await GameRecognizer.RecognizeAsync(Folder, [.. games.Select(game => new RecognitionCandidate(game.Entry.Key, game.Name, game.Folder))],
                _creator.Access, _creator.FileSystem ?? PhysicalFileSystem.Instance, _closing.Token)
                    .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return;
        }
        // A folder that cannot be read, or whose drive does not answer, tells nothing of the game, which the list then offers.
        catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException)
        {
        }

        if (run != _recognitions || games.Count == 0)
        {
            _recognition ??= recognition;
            ShowNote();
            return;
        }

        _recognition = recognition;
        IsNotRecognized = recognition is { Winner: null };
        ShowNote();
        // The author chose for themselves meanwhile, which the recognition leaves as it is.
        if (_byAuthor)
        {
            return;
        }

        if (recognition?.Winner is { } winner && _options.FirstOrDefault(option => option.Entry.Key == winner.Key) is { } game)
        {
            Show(game, byAuthor: false);
        }
        else
        {
            ShowsList = true;
        }
    }

    private void Show(GameOption game, bool byAuthor)
    {
        _byAuthor |= byAuthor;
        ShowsList = false;
        if (_shownKey != game.Entry.Key)
        {
            _shownKey = game.Entry.Key;
            (_hints, SiteLine, Parts, PartProblemTitle, PartProblemText) = (null, "", [], "", "");
            _turnedOff.Clear();
            _otherLayout.Clear();
            ShowVersions();
            _layouts?.Cancel();
            _layouts = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
            _ = ShowLayoutsAsync(_layouts.Token);
            _ = HintAsync(game.Entry);
        }

        ShowNote();
    }

    // The shown game's copies, the best fit first, re-judged with every state; a list that stays the same keeps its tiles and
    // the keyboard's focus on them.
    private void ShowVersions()
    {
        if (_shownKey is not { } key || (_options.FirstOrDefault(option => option.Entry.Key == key) ?? Shown) is not { } shown)
        {
            return;
        }

        Shown = shown;
        var scores = _recognition?.Games.ToDictionary(game => game.Key, game => game.Score) ?? [];
        _copies = [shown, .. _options.Where(option => option != shown && IsCopy(option.Entry, shown.Name, shown.Entry))
            .OrderByDescending(option => scores.GetValueOrDefault(option.Entry.Key))];
        foreach (var option in _options)
        {
            option.IsSelected = _copies.Contains(option) && !_turnedOff.Contains(option.Entry.Key);
            option.IsOtherLayout = _otherLayout.GetValueOrDefault(option.Entry.Key);
        }

        List<GameOption> tiles = [.. _copies.Where(copy => copy.IsSelectable)];
        List<GameOption> blocked = [.. _copies.Where(copy => !copy.IsSelectable)];
        if (!tiles.SequenceEqual(VersionTiles))
        {
            VersionTiles = tiles;
        }

        if (!blocked.SequenceEqual(BlockedVersions))
        {
            BlockedVersions = blocked;
        }

        CreateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Tells which copies of a game lay their folders out unlike a first one, as the project's comparison tells them, each as soon as
    /// it is listed; a copy that cannot be listed, e.g. on a drive that does not answer, says nothing.
    /// </summary>
    /// <param name="creator">The library page, which reads the folders.</param>
    /// <param name="firstFolder">The folder of the copy whose layout counts.</param>
    /// <param name="firstRenames">Where the project's folders lie on that copy; empty when they lie as it lays them out.</param>
    /// <param name="copies">The other copies, each with its folder.</param>
    /// <param name="otherLayout">Told of each copy laid out otherwise, on the UI thread.</param>
    /// <param name="cancellationToken">Stops the look, e.g. once the dialog closed.</param>
    /// <returns>Completes once every copy was looked at.</returns>
    internal static async Task TellLayoutsAsync(CreatorPageViewModel creator, string firstFolder, ValueList<FolderRename> firstRenames,
        IReadOnlyList<(GameKey Key, string Folder)> copies, Action<GameKey> otherLayout, CancellationToken cancellationToken)
    {
        var comparer = new FolderComparer(creator.Options, creator.FileSystem, creator.Access);
        var installs = creator.Agent.State.Games.SelectMany(view => view.Installs).Select(install => new ExistingInstall(install.Id, install.InstallDirectory)).ToList();
        try
        {
            var first = FolderLayout.Resolve(firstRenames, await comparer.ListGameAsync(firstFolder, installs, cancellationToken).ConfigureAwait(true), null);
            foreach (var (key, folder) in copies)
            {
                try
                {
                    if (FolderLayout.Resolve([], await comparer.ListGameAsync(folder, installs, cancellationToken).ConfigureAwait(true), first).Layout.Renames.Count > 0)
                    {
                        otherLayout(key);
                    }
                }
                catch (Exception exception) when (exception is CreatorBlockedException or TimeoutException or IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or CreatorBlockedException or TimeoutException or IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task ShowLayoutsAsync(CancellationToken cancellationToken)
    {
        var copies = _copies.Where(copy => copy.IsSelectable).ToList();
        if (copies.Count >= 2)
        {
            await TellLayoutsAsync(_creator, copies[0].Folder, [], [.. copies.Skip(1).Select(copy => (copy.Entry.Key, copy.Folder))], key =>
            {
                _otherLayout[key] = true;
                ShowVersions();
            }, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task HintAsync(DetectedGameEntry game)
    {
        var hints = await ProjectHints.AskAsync(_services.Agent, game).ConfigureAwait(true);
        if (_shownKey != game.Key || hints is null || _closing.IsCancellationRequested)
        {
            return;
        }

        _hints = hints;
        SiteLine = Text.Format(Strings.Creator_SiteProject, ("team", hints.Site.TeamName), ("hasRelease", hints.Release.Length > 0 ? "true" : "false"),
            ("release", GameWording.ShortVersion(hints.Release)));
        Parts = [.. hints.Features.Select(feature => feature.Name).Distinct().Select(name => new PartOption(this, name, hints.FeatureFrom(name)))];
    }

    private void ShowNote() => FolderNote = _recognition is not { } recognition ? ""
        : Text.Format(Strings.Library_Joined, ("first", ""), ("second", GameWording.Join(
        [
            Text.Format(Strings.Creator_FileCount, ("count", recognition.FileCount)),
            recognition.Winner is { } winner && _copies.Exists(copy => copy.Entry.Key == winner.Key) ? Strings.Creator_Recognized : "",
        ])));

    // The project's parts: the base in the author's folder, then the add-ons given a folder, besides the one being given one.
    private IEnumerable<ProjectPart> PartsOf(PartOption? besides = null) =>
    [
        new ProjectPart { AuthorFolder = Folder },
        .. Parts.Where(part => part != besides && part.HasFolder).Select(part => new ProjectPart { Name = part.Name, AuthorFolder = part.Folder }),
    ];
}
