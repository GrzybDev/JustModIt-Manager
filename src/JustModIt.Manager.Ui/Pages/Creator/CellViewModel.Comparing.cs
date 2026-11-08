using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Pages.Library;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Pages.Creator;

// The comparison of the cell's folder with its game, and what it shows while it runs and once it ended.
public sealed partial class CellViewModel
{
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
            LogComparisonFailed(_page.Creator.Log, exception);
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
            IsIndeterminate ? "" : Text.Format(Strings.GamePage_BytesOf, ("done", Formats.FileSize(progress.BytesDone)),
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

    [LoggerMessage(Level = LogLevel.Error, Message = "The comparison of the files failed unexpectedly")]
    private static partial void LogComparisonFailed(ILogger logger, Exception exception);
}
