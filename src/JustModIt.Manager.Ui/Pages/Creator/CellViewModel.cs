using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>Where a part stands on a version of the game.</summary>
public enum CellState
{
    /// <summary>The part has no folder of the author's yet.</summary>
    NoFolder,
    Comparing,

    /// <summary>The author stopped the comparison.</summary>
    Stopped,

    /// <summary>The comparison refused or failed; <see cref="CellViewModel.ProblemTitle"/> says why.</summary>
    Failed,

    /// <summary>The author's folder is not there.</summary>
    FolderGone,

    /// <summary>Every file of the author's is the game's own, so the package would be empty.</summary>
    NothingToPackage,
    Compared,

    /// <summary>The stores do not report the version's game.</summary>
    GameGone,

    /// <summary>A package is installed on the game, whose files would count as the game's.</summary>
    Installed,

    /// <summary>The store or a job is at work on the game.</summary>
    NotReady,

    /// <summary>A test package is in the game, so its files are no baseline for a comparison or a build.</summary>
    TestInstalled,

    /// <summary>An add-on waits for its base on the version, over whose plan it is planned, to be compared.</summary>
    Waiting,

    /// <summary>An add-on's base on the version was not compared, so the add-on is not either.</summary>
    BaseMissing,
}

/// <summary>A row of a project's table: one part over the versions of the game.</summary>
/// <param name="Page">The project's page, which renames the part.</param>
/// <param name="Part">The part's name; empty for the base.</param>
/// <param name="Caption">What the part is, under its name: an add-on as the site offers it, else the base's mode.</param>
/// <param name="Mode">The add-on's mode; empty for the base, whose caption says it.</param>
/// <param name="Cells">The part's cells, in the versions' order.</param>
public sealed record CellRow(ProjectPageViewModel Page, string Part, string Caption, string Mode, ValueList<CellViewModel> Cells)
{
    public bool IsFeature => Part.Length > 0;

    public string Name => IsFeature ? Part : Strings.Creator_PartBase;

    /// <summary>The add-on's name in the row's head, which renames it once the author leaves the box.</summary>
    public string TypedName
    {
        get => Part;
        set => _ = Page.RenamePartAsync(Part, value);
    }
}

/// <summary>
/// A part of the project on a version of the game: the author's folder, laid out like the game or like the version, compared
/// with the game on the thread pool, with the stage, the bytes and the file shown and a way to stop, or why it cannot be
/// compared now.
/// </summary>
public sealed partial class CellViewModel : ObservableObject, IDisposable
{
    private readonly ProjectPageViewModel _page;
    private readonly CreatorSession _session;
    private CancellationTokenSource? _comparing;
    private (GameKey Key, string Folder, string? Build)? _comparedWith;
    private string _folder;
    private string _inside;
    private CellState _compared = CellState.NoFolder;
    private CellState? _block;
    private (string Title, string Text) _blockNotice = ("", "");
    private DateTimeOffset _comparedAt;
    private string _work = "";
    private bool _unanswered;

    /// <summary>Creates a cell, which compares once the stores tell where its game is.</summary>
    /// <param name="page">The project's page.</param>
    /// <param name="session">The project.</param>
    /// <param name="key">The cell's part and version.</param>
    public CellViewModel(ProjectPageViewModel page, CreatorSession session, CellKey key)
    {
        _page = page;
        _session = session;
        Key = key;
        (_folder, _inside) = (Folder, Inside);
        _compared = _folder.Length > 0 ? CellState.Comparing : CellState.NoFolder;
        (Stage, State) = (Strings.Creator_CompareListing, _compared);
        _session.PropertyChanged += OnSessionChanged;
        ShowBrief();
    }

    public ProjectPageViewModel Page => _page;

    public CellKey Key { get; private set; }

    public ProjectPart Part => _session.Project.Parts.FirstOrDefault(part => part.Name == Key.Part) ?? _session.Project.Parts[0];

    /// <summary>The cell's version; null once the project no longer has it.</summary>
    public ProjectVersion? Version => _session.VersionOf(Key.Version);

    /// <summary>The cell's game as the agent reported it; null until it did, or while the stores do not report it.</summary>
    public DetectedGameEntry? Game => _page.GameOf(Key.Version);

    /// <summary>The author's folder of the cell: the version's own, else the part's.</summary>
    public string Folder => Part.FolderOf(Key.Version);

    public string StoreName => GameWording.StoreName(Key.Version.Store);

    // The other parts' folders within the cell's, whose files its comparison leaves to them.
    private string Inside => string.Join('|', _session.Project.FoldersInside(Key.Part, Folder));

    /// <summary>The cell is the one the panel and the tabs show.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; internal set; }

    /// <summary>The cell's plan; null until it is compared and planned.</summary>
    public CreatorReview? Review => _session.ReviewOf(Key);

    /// <summary>The cell has a plan, also one a later comparison that stopped or failed left standing.</summary>
    public bool HasReview => Review is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsComparing), nameof(HasNothingToPackage), nameof(IsCompared), nameof(IsBlockedByInstall),
        nameof(CanCompare), nameof(IsBlocked), nameof(Line), nameof(HoldsTest), nameof(WaitsForFolder))]
    public partial CellState State { get; private set; }

    /// <summary>The cell is an add-on's, planned over its base's cell on the same version.</summary>
    public bool IsFeature => Key.Part.Length > 0;

    /// <summary>The add-on has no folder yet, so its panel is all a place to drop one.</summary>
    public bool WaitsForFolder => IsFeature && State == CellState.NoFolder;

    /// <summary>Which release on the site offers the add-on; empty when the site's last release offers none of its name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DropText))]
    public partial string FromSite { get; internal set; } = "";

    /// <summary>Where the add-on comes from and that its folder can be dropped, before the way to pick one.</summary>
    public string DropText => GameWording.Join([FromSite, Strings.Creator_FeatureFolder]);

    /// <summary>The picker's title for the add-on's folder, which screen readers read as the way to pick.</summary>
    public string PickName => Text.Format(Strings.Creator_PickFeatureName, ("name", Key.Part));

    public bool IsComparing => State == CellState.Comparing;

    public bool HasNothingToPackage => State == CellState.NothingToPackage;

    public bool IsCompared => State == CellState.Compared;

    /// <summary>The version's game cannot be compared or built for now.</summary>
    public bool IsBlocked => _block is not null;

    /// <summary>The version's game can be listed now.</summary>
    internal bool CanBeRead => !IsBlocked && Game is not null;

    public bool IsBlockedByInstall => State == CellState.Installed;

    /// <summary>A test package is in the version's game.</summary>
    public bool HoldsTest => State == CellState.TestInstalled;

    /// <summary>The words of the way to take the cell's version off the project.</summary>
    public string RemoveText => Text.Format(Strings.Creator_RemoveVersion, ("store", StoreName));

    /// <summary>The words of the way to take the cell's add-on off the project.</summary>
    public string RemovePartText => Text.Format(Strings.Creator_RemovePart, ("name", Key.Part));

    /// <summary>The comparison can start again: the cell has a folder, a game and nothing in the way.</summary>
    public bool CanCompare => Game is not null && _block is null && !IsComparing && Folder.Length > 0 && State is not (CellState.Waiting or CellState.BaseMissing);

    /// <summary>How the cell stands, as its head says it, with what the base's last package says of the compared files.</summary>
    public LineContent Line => State switch
    {
        CellState.Comparing => new LineContent(Icons.LoaderCircle, Tone.Info, Text.Format(Strings.Creator_Comparing, ("percent", Percent))) { IsBusy = true },
        CellState.Compared or CellState.NothingToPackage => new LineContent(Icons.CircleCheck, Tone.Success, Text.Format(Strings.Creator_ComparedWhen,
            ("when", Formats.RelativeTime(_comparedAt, _page.Services.Clock).ToLower(Text.Culture))), Detail),
        _ => new LineContent(State switch
            {
                CellState.NotReady => Icons.Clock,
                CellState.Waiting => Icons.Hourglass,
                CellState.TestInstalled or CellState.Installed => Icons.TriangleAlert,
                CellState.NoFolder or CellState.FolderGone => Icons.FolderOpen,
                CellState.GameGone => Icons.Gamepad,
                CellState.BaseMissing => Icons.Package,
                _ => Icons.CircleX,
            },
            State is CellState.Stopped or CellState.GameGone or CellState.NotReady or CellState.Waiting or CellState.BaseMissing ? Tone.Neutral
                // A drive that does not answer may come back with the folder.
                : State is CellState.TestInstalled or CellState.NoFolder || (State == CellState.FolderGone && _unanswered) ? Tone.Warning : Tone.Danger,
            Text.Format(Strings.Creator_CellState, ("state", State.ToString()), ("work", _work), ("version", ""), ("store", StoreName)),
            State == CellState.Waiting ? Text.Format(Strings.Creator_CellDetail, ("detail", "Waiting")) : null),
    };

    /// <summary>What the base's last package says of the compared files; empty when there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line))]
    public partial string Detail { get; private set; } = "";

    /// <summary>What stopped the comparison; empty while nothing did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoticeTitle), nameof(HasNotice))]
    public partial string ProblemTitle { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoticeText), nameof(HasNotice))]
    public partial string ProblemText { get; private set; } = "";

    /// <summary>What the cell's callout says: what keeps the version from being compared, else what stopped the comparison.</summary>
    public string NoticeTitle => _block is null ? ProblemTitle : _blockNotice.Title;

    public string NoticeText => _block is null ? ProblemText : _blockNotice.Text;

    public bool HasNotice => NoticeTitle.Length > 0 || NoticeText.Length > 0;

    /// <summary>Files rather than a folder were dropped.</summary>
    [ObservableProperty]
    public partial bool DroppedFiles { get; private set; }

    [ObservableProperty]
    public partial string Stage { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line))]
    public partial string Percent { get; private set; } = "";

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; private set; } = true;

    [ObservableProperty]
    public partial string FileLine { get; private set; } = "";

    /// <summary>Why there is nothing to package: the folder is empty, or its files are the game's own.</summary>
    [ObservableProperty]
    public partial string NothingTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string NothingText { get; private set; } = "";

    /// <summary>The files that are the game's own, which the package leaves out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIdentical))]
    public partial IReadOnlyList<string> IdenticalFiles { get; private set; } = [];

    public bool HasIdentical => IdenticalFiles.Count > 0;

    [ObservableProperty]
    public partial string IdenticalCount { get; private set; } = "";

    [ObservableProperty]
    public partial bool ShowsIdentical { get; set; }

    public void Dispose()
    {
        _session.PropertyChanged -= OnSessionChanged;
        _comparing?.Cancel();
        _checking?.Cancel();
    }

    /// <summary>
    /// Tells again what keeps the version's game from being compared, once the agent's state or the game changed; a block that
    /// goes compares the folder again, since the game's files may have changed meanwhile, except after a test, whose undo
    /// puts the files back as they were.
    /// </summary>
    internal void ShowGame()
    {
        var was = _block;
        if (Game is not { } game)
        {
            Block(_page.IsGone(Key.Version) ? CellState.GameGone : null, Strings.Creator_GameGoneTitle,
                Text.Format(Strings.Creator_GameGone, ("game", Version?.Game.Name ?? ""), ("store", StoreName)));
        }
        else
        {
            var (block, text) = NewProjectDialogViewModel.Judge(game, _page.Agent.State, Strings.Creator_VersionNotReady);
            switch (block)
            {
                case GameBlock.Installed:
                    Block(CellState.Installed, Text.Format(Strings.Creator_CellState, ("state", nameof(CellState.Installed)), ("work", ""), ("version", "")), text);
                    break;
                case GameBlock.TestInstalled:
                    Block(CellState.TestInstalled, Strings.Creator_GameTestInstalled, Strings.Creator_RebuildBlocked);
                    break;
                case GameBlock.NotReady:
                    _work = GameWording.GameState(game.Game.State, game.Game.Reason, game.Key.Store);
                    Block(CellState.NotReady, "", text);
                    break;
                default:
                    Block(null, "", "");
                    break;
            }
        }

        if (_block is null && Game is not null && (was is not (null or CellState.TestInstalled) || _comparedWith is null))
        {
            _ = CompareAsync();
        }
        else
        {
            CheckIfAsked();
        }
    }

    /// <summary>Compares the part's folder with its game.</summary>
    /// <returns>Completes once the comparison ended, was stopped or refused.</returns>
    [RelayCommand]
    public async Task CompareAsync()
    {
        if (Game is not { } game || _block is not null || Folder is not { Length: > 0 } folder)
        {
            return;
        }

        // An add-on is planned over its base's plan, so it compares once its base did; a base that will not, e.g. stopped, takes
        // the add-on with it.
        if (IsFeature && _page.Cells.FirstOrDefault(cell => cell.Key == Key with { Part = "" }) is { State: not (CellState.Compared or CellState.NothingToPackage) } first)
        {
            Show(first.IsComparing ? CellState.Waiting : CellState.BaseMissing);
            return;
        }

        // A build reads the files the comparison would plan from, so the comparison waits until it ended.
        _checkPending = _page.Build.IsBuilding;
        if (_checkPending)
        {
            return;
        }

        _comparing?.Cancel();
        _checking?.Cancel();
        using var comparing = _comparing = new CancellationTokenSource();
        _comparedWith = (game.Key, game.Game.InstallDirectory, game.Game.BuildId);
        (ProblemTitle, ProblemText, DroppedFiles, _unanswered) = ("", "", false, false);
        (Detail, ChangedText, _changed) = ("", "", []);
        ShowChanges();
        // A cell that waits for another's comparison has no stage yet.
        (Stage, Percent, FileLine, Progress, IsIndeterminate) = ("", "", "", 0, true);
        Show(CellState.Comparing);
        try
        {
            var comparison = await _page.CompareOneAsync(this, folder, new LatestProgress<CompareProgress>(Show), comparing.Token)
                .ConfigureAwait(true);
            if (_comparing == comparing)
            {
                await _session.UseAsync(Key, comparison).ConfigureAwait(true);
                // What an undone test left in the game may have gone meanwhile, e.g. by the store's check of the files.
                await _page.ReadKeptPathsAsync().ConfigureAwait(true);
            }

            if (_comparing == comparing)
            {
                Finish(comparison);
                await ShowRecordAsync(_page.Record, comparing.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (comparing.IsCancellationRequested)
        {
            Stop(comparing, CellState.Stopped, Strings.Creator_CompareCancelledTitle, Strings.Creator_CompareCancelled);
        }
        catch (CreatorBlockedException blocked)
        {
            var reason = blocked.Blocks[0].Reason;
            _unanswered = _comparing == comparing && reason == CreatorBlockReason.AuthorFolderUnavailable;
            Stop(comparing, reason is CreatorBlockReason.AuthorFolderMissing or CreatorBlockReason.AuthorFolderUnavailable ? CellState.FolderGone : CellState.Failed,
                CreatorWording.BlockTitle(blocked.Blocks[0], game.Game.InstallDirectory),
                string.Join(' ', blocked.Blocks.Select(block => CreatorWording.Block(block, game.Game.InstallDirectory))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            Stop(comparing, CellState.Failed, Strings.Creator_CompareFailedTitle, Strings.Creator_CompareFailed);
        }
        // Anything else ends the comparison like the failures above rather than the window.
        catch (Exception exception)
        {
            LogComparisonFailed(_page.Log, exception);
            Stop(comparing, CellState.Failed, Strings.Creator_CompareStoppedTitle, Text.Format(Strings.Creator_Unexpected, ("error", exception.GetType().Name)));
        }
        finally
        {
            if (_comparing == comparing)
            {
                _comparing = null;
            }
        }
    }

    [RelayCommand]
    private void Drop(string? folder)
    {
        if (folder is null)
        {
            DroppedFiles = true;
        }
        else
        {
            _ = UseFolderAsync(folder);
        }
    }

    [RelayCommand]
    private async Task ChangeFolderAsync()
    {
        if (await _page.Services.Pickers.PickFolderAsync(IsFeature ? PickName : Strings.Creator_PickFolderTitle, Folder.Length > 0 ? Folder : null).ConfigureAwait(true) is { } folder)
        {
            await UseFolderAsync(folder).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void Cancel() => _comparing?.Cancel();

    [RelayCommand]
    private void OpenGame()
    {
        if (Game is { } game)
        {
            _page.Services.Navigator.Navigate(new GameRoute(game.Key));
        }
    }

    [RelayCommand]
    private void ToggleIdentical() => ShowsIdentical = !ShowsIdentical;

    // With several versions the folder may be this version's own; each cell whose folder changed compares again by itself.
    private async Task UseFolderAsync(string folder)
    {
        DroppedFiles = false;
        if (_page.Refuses(Key.Part, folder))
        {
            return;
        }

        var part = Part;
        var own = false;
        if (_session.Project.Versions.Count > 1)
        {
            var answer = await _page.Services.Dialogs.ShowAsync(new ChoiceDialogViewModel(Strings.Creator_ChangeFolder, "",
            [
                new DialogChoice("version", Icons.FolderOpen, Text.Format(Strings.Creator_FolderForVersion, ("store", StoreName)), Strings.Creator_FolderForVersionText),
                new DialogChoice("all", Icons.Folder, Strings.Creator_FolderForAll, Strings.Creator_FolderForAllText),
            ])).ConfigureAwait(true);
            if (answer.Key is null || Part != part)
            {
                return;
            }

            own = answer.Key == "version";
        }

        var changed = !own ? part with { AuthorFolder = folder, VersionFolders = default }
            : PathComparison.AreSame(folder, part.AuthorFolder) ? part with { VersionFolders = part.VersionFolders.Remove(Key.Version) }
            : part with { VersionFolders = part.VersionFolders.SetItem(Key.Version, folder) };
        if (changed == part)
        {
            _ = CompareAsync();
            return;
        }

        _session.Change(project => project with { Parts = new(project.Parts.Items.Replace(part, changed)) });
    }

    /// <summary>Compares an add-on that waited for its base, or holds it back, once the base's state changed.</summary>
    internal void BaseChanged()
    {
        if (_compared is CellState.Waiting or CellState.BaseMissing)
        {
            _ = CompareAsync();
        }
    }

    /// <summary>Takes the add-on's new name, which keys the cell; its words follow with the plan made under the name.</summary>
    /// <param name="part">The add-on's new name.</param>
    internal void Rename(string part)
    {
        Key = Key with { Part = part };
        OnPropertyChanged(nameof(Key));
        OnPropertyChanged(nameof(RemovePartText));
        OnPropertyChanged(nameof(PickName));
    }

    private void Block(CellState? block, string title, string text)
    {
        if (block is not null && block != CellState.TestInstalled)
        {
            _comparing?.Cancel();
            _comparing = null;
        }

        (_block, _blockNotice) = (block, (title, text));
        Show(_compared);
    }

    private void Show(CellState state)
    {
        _compared = state;
        State = _block ?? state;
        OnPropertyChanged(nameof(CanCompare));
        OnPropertyChanged(nameof(Line));
        OnPropertyChanged(nameof(NoticeTitle));
        OnPropertyChanged(nameof(NoticeText));
        OnPropertyChanged(nameof(HasNotice));
        ShowBrief();
    }

    partial void OnPercentChanged(string value) => ShowBrief();

    private void Show(CompareProgress progress)
    {
        if (State != CellState.Comparing)
        {
            return;
        }

        IsIndeterminate = progress.Stage == ComparisonStage.ListingFiles || progress.BytesTotal == 0;
        Progress = progress.BytesTotal > 0 ? 100.0 * progress.BytesDone / progress.BytesTotal : 0;
        Percent = IsIndeterminate ? "" : Formats.Percent(progress.BytesDone, progress.BytesTotal);
        Stage = GameWording.Join(
        [
            Text.Format(Strings.Creator_CompareStage, ("stage", progress.Stage.ToString())),
            IsIndeterminate ? "" : Text.Format(Strings.Creator_BytesOf, ("done", Formats.FileSize(progress.BytesDone)),
                ("total", Formats.FileSize(progress.BytesTotal))),
        ]);
        FileLine = progress.FileCount == 0 ? ""
            : GameWording.Join(
            [
                Text.Format(Strings.Creator_FileOf, ("number", Math.Min(progress.FilesDone + 1, progress.FileCount)),
                    ("count", progress.FileCount)),
                progress.CurrentFile ?? "",
            ]);
    }

    private void Finish(FolderComparison comparison)
    {
        var identical = comparison.Files.Where(file => file.Difference == FileDifference.Identical).Select(file => file.Path).ToList();
        IdenticalFiles = identical;
        IdenticalCount = Text.Format(Strings.Creator_IdenticalCount, ("count", identical.Count));
        ShowsIdentical = false;
        _comparedAt = _page.Services.Clock.GetUtcNow();
        // The author's files only repeat the game's, so a simple part has nothing to package.
        if (Part is { Mode: CreatorMode.Simple, ExtraSteps.Count: 0 } && comparison.Files.All(file => file.Difference == FileDifference.Identical))
        {
            (NothingTitle, NothingText) = identical.Count > 0
                ? (Strings.Creator_NothingTitle, Strings.Creator_Nothing)
                : (Strings.Creator_EmptyFolderTitle, Strings.Creator_EmptyFolder);
            Show(CellState.NothingToPackage);
        }
        else
        {
            Show(CellState.Compared);
        }
    }

    // A comparison the author has moved on from, e.g. by changing the folder, has nothing to say.
    private void Stop(CancellationTokenSource comparing, CellState state, string title, string text)
    {
        if (_comparing == comparing)
        {
            (ProblemTitle, ProblemText) = (title, text);
            Show(state);
        }
    }

    // A comparison of another folder, of files another part now takes or gives back, or with a game the version no longer names,
    // counts for nothing, so the folder is compared again with the game it names now.
    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CreatorSession.Review))
        {
            ShowChanges();
            return;
        }

        if (e.PropertyName != nameof(CreatorSession.Project) || Version is not { } version)
        {
            return;
        }

        if (_folder != Folder || _inside != Inside)
        {
            (_folder, _inside) = (Folder, Inside);
            OnPropertyChanged(nameof(Folder));
            _comparing?.Cancel();
            (_comparing, _comparedWith) = (null, null);
            _ = _session.UseAsync(Key, null);
            _ = CompareAsync();
            return;
        }

        if (_comparedWith is not { } compared || (compared.Key == version.Game.Key && compared.Folder == version.GameFolder))
        {
            return;
        }

        _comparing?.Cancel();
        (_comparing, _comparedWith) = (null, null);
        // The page takes the game the version names a moment later, and the comparison then starts with it.
        if (Game?.Key == version.Game.Key)
        {
            _ = CompareAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The comparison of the files failed unexpectedly")]
    private static partial void LogComparisonFailed(ILogger logger, Exception exception);
}
