using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Pages.Library;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>Where the second step stands.</summary>
public enum FilesState
{
    /// <summary>No folder yet: the drop zone.</summary>
    Empty,
    Comparing,

    /// <summary>The comparison was cancelled, or it refused or failed; <see cref="FilesStepViewModel.ProblemTitle"/> says why.</summary>
    Stopped,

    /// <summary>Every file of the author's is the game's own, so the package would be empty.</summary>
    NothingToPackage,
    Compared,
}

/// <summary>
/// The second step: the author's folder, laid out like the game, compared with the game on the thread pool as soon as it
/// is given, with the stage, the bytes and the file shown and a way to cancel.
/// </summary>
public sealed partial class FilesStepViewModel : ObservableObject, ICreatorStepView
{
    private readonly CreatorPageViewModel _page;
    private readonly CreatorSession _session;
    private CancellationTokenSource? _comparing;
    private (GameKey? Key, string Folder)? _comparedWith;
    private bool _comparesOnShow;

    /// <summary>Creates the step for a project, which compares the folder of a project opened from its file once shown.</summary>
    /// <param name="page">The creator.</param>
    /// <param name="session">The project.</param>
    public FilesStepViewModel(CreatorPageViewModel page, CreatorSession session)
    {
        _page = page;
        _session = session;
        _comparesOnShow = session.Project.AuthorFolder.Length > 0;
        _session.PropertyChanged += OnSessionChanged;
        ShowFolder();
    }

    public CreatorPageViewModel Page => _page;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDropZone), nameof(IsComparing), nameof(IsStopped), nameof(HasNothingToPackage),
        nameof(IsCompared), nameof(ShowsFolder))]
    public partial FilesState State { get; private set; }

    public bool ShowsDropZone => State == FilesState.Empty;

    public bool ShowsFolder => State != FilesState.Empty;

    public bool IsComparing => State == FilesState.Comparing;

    public bool IsStopped => State == FilesState.Stopped;

    public bool HasNothingToPackage => State == FilesState.NothingToPackage;

    public bool IsCompared => State == FilesState.Compared;

    [ObservableProperty]
    public partial string FolderName { get; private set; } = "";

    [ObservableProperty]
    public partial string FolderPath { get; private set; } = "";

    /// <summary>A folder is dragged over the drop zone.</summary>
    [ObservableProperty]
    public partial bool IsDragOver { get; set; }

    /// <summary>Why the folder given cannot be compared, shown under the drop zone; empty while there is nothing to say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string ProblemTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string ProblemText { get; private set; } = "";

    public bool HasProblem => ProblemTitle.Length > 0;

    /// <summary>Files rather than a folder were dropped.</summary>
    [ObservableProperty]
    public partial bool DroppedFiles { get; private set; }

    [ObservableProperty]
    public partial string Stage { get; private set; } = "";

    [ObservableProperty]
    public partial string Percent { get; private set; } = "";

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; private set; } = true;

    [ObservableProperty]
    public partial string FileLine { get; private set; } = "";

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

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

    public void OnShown()
    {
        if (_comparesOnShow)
        {
            _ = CompareAsync();
        }
    }

    public Task<bool> CanLeaveAsync() => Task.FromResult(true);

    public void Dispose()
    {
        _session.PropertyChanged -= OnSessionChanged;
        _comparing?.Cancel();
    }

    /// <summary>Takes a folder the author dropped or picked and compares it with the game.</summary>
    /// <param name="folder">The folder's full path.</param>
    public void UseFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        DroppedFiles = false;
        if (_page.Game is { } game && PathComparison.Overlaps(game.Game.InstallDirectory, folder))
        {
            (ProblemTitle, ProblemText) = (PathComparison.AreSame(game.Game.InstallDirectory, folder)
                ? Strings.Creator_GameFolderTitle
                : Strings.Creator_InsideGameTitle, Strings.Creator_GameFolder);
            State = FilesState.Empty;
            return;
        }

        if (_session.Project.AuthorFolder.Length == 0 || !PathComparison.AreSame(folder, _session.Project.AuthorFolder))
        {
            _ = _session.UseAsync(null);
            _session.Change(project => project with { AuthorFolder = folder });
        }

        _ = CompareAsync();
    }

    /// <summary>Says that files, not a folder, were dropped.</summary>
    public void RefuseFiles() => DroppedFiles = true;

    /// <summary>Compares the project's folder with its game, then opens the check of the operations.</summary>
    /// <returns>Completes once the comparison ended, was cancelled or refused.</returns>
    public async Task CompareAsync()
    {
        if (_page.Game is not { } game || _session.Project.AuthorFolder is not { Length: > 0 } folder)
        {
            return;
        }

        _comparing?.Cancel();
        using var comparing = _comparing = new CancellationTokenSource();
        (_comparedWith, _comparesOnShow) = ((_session.Project.Game?.Key, _session.Project.GameFolder), false);
        ShowFolder();
        (ProblemTitle, ProblemText) = ("", "");
        (Stage, Percent, FileLine, Progress, IsIndeterminate) = (Strings.Creator_CompareListing, "", "", 0, true);
        State = FilesState.Comparing;
        var installs = _page.Agent.State.Games.SelectMany(view => view.Installs)
            .Select(install => new ExistingInstall(install.Id, install.InstallDirectory))
            .ToList();
        try
        {
            var comparison = await new FolderComparer(_page.Options, _page.FileSystem)
                .CompareAsync(new ComparisonRequest(game.Game.InstallDirectory, folder, installs),
                    new LatestProgress<CompareProgress>(Show), comparing.Token)
                .ConfigureAwait(true);
            if (_comparing == comparing)
            {
                await _session.UseAsync(comparison).ConfigureAwait(true);
            }

            if (_comparing == comparing)
            {
                Finish(comparison);
            }
        }
        catch (OperationCanceledException) when (comparing.IsCancellationRequested)
        {
            Stop(comparing, Strings.Creator_CompareCancelledTitle, Strings.Creator_CompareCancelled);
        }
        catch (CreatorBlockedException blocked)
        {
            Stop(comparing, CreatorWording.BlockTitle(blocked.Blocks[0], game.Game.InstallDirectory),
                string.Join(' ', blocked.Blocks.Select(block => CreatorWording.Block(block, game.Game.InstallDirectory))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Stop(comparing, Strings.Creator_CompareFailedTitle, Strings.Creator_CompareFailed);
        }
        // Anything else ends the comparison like the failures above rather than the window, which would lose the author's
        // unsaved work.
        catch (Exception exception)
        {
            LogComparisonFailed(_page.Log, exception);
            Stop(comparing, Strings.Creator_CompareStoppedTitle, Text.Format(Strings.Creator_Unexpected, ("error", exception.GetType().Name)));
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
    private async Task PickFolderAsync()
    {
        var folder = await _page.Services.Pickers.PickFolderAsync(Strings.Creator_PickFolderTitle,
            _session.Project.AuthorFolder.Length > 0 ? _session.Project.AuthorFolder : null).ConfigureAwait(true);
        if (folder is not null)
        {
            UseFolder(folder);
        }
    }

    [RelayCommand]
    private void Cancel() => _comparing?.Cancel();

    [RelayCommand]
    private Task CompareAgainAsync() => CompareAsync();

    [RelayCommand]
    private void ChangeFolder()
    {
        _comparing?.Cancel();
        _ = _session.UseAsync(null);
        (ProblemTitle, ProblemText, DroppedFiles) = ("", "", false);
        State = FilesState.Empty;
    }

    [RelayCommand]
    private void ChangeGame() => _page.GoTo(CreatorStep.Game);

    [RelayCommand]
    private void Back() => _page.GoTo(CreatorStep.Game);

    [RelayCommand]
    private void Continue() => _page.GoTo(CreatorStep.Review);

    [RelayCommand]
    private void ToggleIdentical() => ShowsIdentical = !ShowsIdentical;

    private void ShowFolder()
    {
        var folder = _session.Project.AuthorFolder;
        FolderPath = folder;
        FolderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
    }

    private void Show(CompareProgress progress)
    {
        if (State != FilesState.Comparing)
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
        Summary = Text.Format(Strings.Creator_Compared, ("count", comparison.Files.Count(file => file.AuthorPath is not null)));
        ShowsIdentical = false;
        if (CreatorPageViewModel.HasNothingToPackage(_session))
        {
            (NothingTitle, NothingText) = identical.Count > 0
                ? (Strings.Creator_NothingTitle, Strings.Creator_Nothing)
                : (Strings.Creator_EmptyFolderTitle, Strings.Creator_EmptyFolder);
            State = FilesState.NothingToPackage;
            return;
        }

        State = FilesState.Compared;
        if (ReferenceEquals(_page.Content, this))
        {
            _page.GoTo(CreatorStep.Review);
        }
    }

    // A comparison the author has moved on from, e.g. by choosing another game, has nothing to say.
    private void Stop(CancellationTokenSource comparing, string title, string text)
    {
        if (_comparing == comparing)
        {
            (ProblemTitle, ProblemText) = (title, text);
            State = FilesState.Stopped;
        }
    }

    // A comparison with a game the project no longer names counts for nothing, so the folder is compared again with the
    // game it names now once the step is shown.
    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        var project = _session.Project;
        if (e.PropertyName != nameof(CreatorSession.Project) || _comparedWith is not { } compared
            || (compared.Key == project.Game?.Key && compared.Folder == project.GameFolder))
        {
            return;
        }

        _comparing?.Cancel();
        (_comparing, _comparedWith, _comparesOnShow) = (null, null, project.AuthorFolder.Length > 0);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The comparison of the files failed unexpectedly")]
    private static partial void LogComparisonFailed(ILogger logger, Exception exception);
}
