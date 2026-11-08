using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

// What changed since the comparison, which starts it again, and since the part's last build, which the cell lists, and what the
// cell's plan shares with the first version's, which the table and the panel tell.
public sealed partial class CellViewModel
{
    private CancellationTokenSource? _checking;
    private bool _checkPending;
    private HashSet<string> _changed = [];
    private IReadOnlySet<GameKey> _comparedBeside = new HashSet<GameKey>();
    private string _changedDetail = "";

    /// <summary>How many files changed since the part's last build, as the head of <see cref="Changes"/>; empty when none did.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChanges))]
    public partial string ChangedText { get; private set; } = "";

    public bool HasChanges => ChangedText.Length > 0;

    /// <summary>The operations of the files that changed since the part's last build, in the package's order.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<OperationRow> Changes { get; private set; } = [];

    /// <summary>
    /// Compares again once the author's files or the game's build differ from what the comparison saw, telling the files by
    /// their sizes and times alone. While a build runs or the version is blocked, the check waits until that ends.
    /// </summary>
    /// <returns>Completes once the check ended; a comparison it starts goes on.</returns>
    internal async Task CheckAsync()
    {
        if (_session.ComparisonOf(Key) is not { } comparison || _compared is not (CellState.Compared or CellState.NothingToPackage)
            || Game is not { } game)
        {
            return;
        }

        _checkPending = _block is not null || _page.Build.IsBuilding;
        if (_checkPending)
        {
            return;
        }

        _checking?.Cancel();
        using var checking = _checking = new CancellationTokenSource();
        try
        {
            var stamps = comparison.Files.Where(file => file.Author is not null).ToDictionary(file => file.AuthorPath!, file => file.Author!.Value);
            if ((game.Game.BuildId != _comparedWith?.Build
                    || await FileChanges.CheckAsync(comparison.AuthorDirectory, stamps, _session.Project.FoldersInside(Key.Part, comparison.AuthorDirectory),
                        _page.Access, _page.FileSystem ?? PhysicalFileSystem.Instance, checking.Token).ConfigureAwait(true) is not { Count: 0 })
                && !checking.IsCancellationRequested)
            {
                _ = CompareAsync();
            }
        }
        catch (OperationCanceledException) when (checking.IsCancellationRequested)
        {
        }
        finally
        {
            if (_checking == checking)
            {
                _checking = null;
            }
        }
    }

    /// <summary>Remembers which other versions' files the comparison under way leaves out of the author's work.</summary>
    /// <param name="versions">The versions that share the cell's folder and can be read now.</param>
    internal void ComparedBeside(IReadOnlySet<GameKey> versions) => _comparedBeside = versions;

    /// <summary>
    /// Compares again when another version that shares the folder has come since the comparison, or a version it counted no longer
    /// shares the folder. A version only out of reach for now keeps counting, since its files were told apart while they could be.
    /// </summary>
    internal void CompareIfOutdated()
    {
        if (_compared is not (CellState.Compared or CellState.NothingToPackage) || _block is not null || _page.Build.IsBuilding)
        {
            return;
        }

        var beside = _page.Beside(this).ToList();
        if (beside.Exists(other => other.CanBeRead && !_comparedBeside.Contains(other.Key.Version))
            || _comparedBeside.Any(version => !beside.Exists(other => other.Key.Version == version)))
        {
            _ = CompareAsync();
        }
    }

    /// <summary>Runs a check asked for while a build ran or the version was blocked, once nothing stands in its way.</summary>
    internal void CheckIfAsked()
    {
        if (_checkPending)
        {
            _ = CheckAsync();
        }
    }

    /// <summary>
    /// Tells again what changed since the part's last build. A file under a new stamp is read only when the build holds its hash,
    /// and one whose bytes did not change keeps its new stamp in the record, so the library's card can tell without reading it.
    /// </summary>
    /// <param name="record">The project's record of its builds; null when it has none.</param>
    /// <param name="cancellationToken">Stops the reading, e.g. once a newer comparison starts.</param>
    /// <returns>Completes once the cell tells it.</returns>
    internal async Task ShowRecordAsync(BuildRecord? record, CancellationToken cancellationToken)
    {
        var comparison = _session.ComparisonOf(Key);
        var part = record?.Parts.FirstOrDefault(built => built.Part == Key.Part);
        var folder = part?.Folders.FirstOrDefault(built => comparison is not null && PathComparison.AreSame(built.Folder, comparison.AuthorDirectory));
        BuildChanges? changes = null;
        if (IsCompared && part is not null && folder is not null)
        {
            changes = await FileChanges.SinceAsync(comparison!, folder, _page.FileSystem, cancellationToken)
                .ConfigureAwait(true);
            if (changes.Restamped.Count > 0)
            {
                try
                {
                    await _page.Library.UpdateBuildAsync(_page.ProjectId,
                        current => (current ?? new BuildRecord([])).WithStamps(part.Part, folder.Folder, changes.Restamped), CancellationToken.None)
                            .ConfigureAwait(true);
                }
                // The record keeps the earlier stamps, so the card counts those files once more until the next comparison.
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        if (_session.ComparisonOf(Key) != comparison)
        {
            return;
        }

        var version = part?.Version ?? "";
        (string, object?)[] since = [("hasVersion", version.Length > 0 ? "true" : "false"), ("version", GameWording.ShortVersion(version))];
        _changed = [.. changes?.Changed.Select(file => file.Path) ?? []];
        Detail = !IsCompared ? ""
            : part is null ? Text.Format(Strings.Creator_CellState, ("state", "Ready"), ("work", ""))
            : changes is { Count: 0 } ? Text.Format(Strings.Creator_CellState, [("state", "Unchanged"), ("work", ""), .. since])
            : "";
        ChangedText = IsCompared && changes is { Count: > 0 } ? Text.Format(Strings.Creator_ChangedSince, [("count", changes.Count), .. since]) : "";
        _changedDetail = IsCompared && changes is { Count: > 0 } ? Text.Format(Strings.Creator_CellDetail, [("detail", "Changed"), ("count", changes.Count), .. since]) : "";
        ShowChanges();
    }

    /// <summary>How the cell stands in the project's table: its state, and under it what its plan holds.</summary>
    public LineContent Brief { get; private set; } = new(Icons.FolderOpen, Tone.Neutral, "");

    /// <summary>The cell as a screen reader names it: its part, its store and how it stands.</summary>
    [ObservableProperty]
    public partial string Name { get; private set; } = "";

    /// <summary>What the cell's plan has in common with the first version's and which folders lie elsewhere; empty when nothing.</summary>
    [ObservableProperty]
    public partial string Folded { get; private set; } = "";

    // The rows follow the plan, which changes with every edit, and so do the table's words.
    private void ShowChanges()
    {
        OnPropertyChanged(nameof(HasReview));
        Changes = _changed.Count == 0 || Review is not { } review ? []
            : [.. review.Plan.Operations.Select((operation, index) => (Operation: operation, Index: index))
                .Where(planned => _changed.Contains(planned.Operation.Path))
                .Select(planned =>
                {
                    var row = new OperationRow(planned.Operation.Path);
                    ReviewViewModel.Fill(row, planned.Operation, planned.Index, null, [], 0);
                    return row;
                })];
        ShowBrief();
    }

    /// <summary>Tells again how the cell stands in the project's table and what it shares with the part's first version.</summary>
    internal void ShowBrief()
    {
        var plan = Review?.Plan;
        var first = _page.Cells.FirstOrDefault(cell => cell.Key.Part == Key.Part);
        // A comparison that stopped or failed leaves a plan of the files as they were, which says nothing of what is shared now.
        var shared = first != this && first is { IsCompared: true, Review.Plan: var theirs } && plan is not null ? PackageGroups.Shared(theirs, plan) : ((int, int)?)null;
        Folded = GameWording.Join(
        [
            shared is (var files, _) ? Text.Format(Strings.Creator_SharedFiles, ("count", files), ("store", first!.StoreName)) : "",
            shared is (_, > 0 and var own) ? Text.Format(Strings.Creator_OwnPatches, ("count", own)) : "",
            .. (Version?.Folders ?? default).Select(rename => Text.Format(Strings.Creator_FolderMoved, ("from", rename.Package), ("to", rename.Game))),
        ]);
        Brief = State == CellState.NothingToPackage ? new LineContent(Icons.TriangleAlert, Tone.Warning, NothingTitle)
            : State != CellState.Compared ? Line
            : plan is null ? Line with { Detail = null }
            : ReviewViewModel.Tally(plan) is { Open: > 0 } tally ? new LineContent(Icons.TriangleAlert, Tone.Warning, Text.Format(Strings.Creator_CellState,
                ("state", "Decisions"), ("count", tally.Open)), tally.Accepted > 0 ? Text.Format(Strings.Creator_CellDetail, ("detail", "Accepted"), ("count", tally.Accepted)) : null)
            : shared is (var all, 0) && all == plan.Operations.Count && all == first!.Review!.Plan.Operations.Count
                ? new LineContent(Icons.CircleCheck, Tone.Success, Text.Format(Strings.Creator_CellState, ("state", "SameAs"), ("store", first.StoreName)),
                    Text.Format(Strings.Creator_CellDetail, ("detail", "SameAs")))
            : _changedDetail.Length > 0 ? new LineContent(Icons.CircleCheck, Tone.Success, Text.Format(Strings.Creator_CellState, ("state", "Compared")), _changedDetail)
            : new LineContent(Icons.CircleCheck, Tone.Success, Detail.Length > 0 ? Detail : Text.Format(Strings.Creator_CellState, ("state", "Compared")),
                GameWording.Join([Text.Format(Strings.Creator_CountOperations, ("count", plan.Operations.Count)),
                    plan.CopiedBytes is { } bytes ? Formats.FileSize(bytes) : ""]));
        OnPropertyChanged(nameof(Brief));
        Name = Text.Format(Strings.Creator_BlockedCell, ("part", Key.Part.Length > 0 ? Key.Part : Strings.Creator_PartBase), ("store", StoreName), ("text", Brief.Label));
    }
}
