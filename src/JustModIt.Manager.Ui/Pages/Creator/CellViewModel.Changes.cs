using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

// What changed since the comparison, which starts it again, and since the part's last build, which the cell lists.
public sealed partial class CellViewModel
{
    private CancellationTokenSource? _checking;
    private bool _checkPending;
    private HashSet<string> _changed = [];

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
        if (_session.Comparison is not { } comparison || _compared is not (CellState.Compared or CellState.NothingToPackage)
            || _page.Game is not { } game)
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
                    || await FileChanges.CheckAsync(comparison.AuthorDirectory, stamps, [], _page.Access, _page.FileSystem ?? PhysicalFileSystem.Instance,
                        checking.Token).ConfigureAwait(true) is not { Count: 0 })
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
        var comparison = _session.Comparison;
        var part = record?.Parts.FirstOrDefault(built => built.Part == _session.Part.Name);
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

        if (_session.Comparison != comparison)
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
        ShowChanges();
    }

    // The rows follow the plan, which changes with every edit.
    private void ShowChanges() => Changes = _changed.Count == 0 || _session.Review is not { } review ? []
        : [.. review.Plan.Operations.Select((operation, index) => (Operation: operation, Index: index))
            .Where(planned => _changed.Contains(planned.Operation.Path))
            .Select(planned =>
            {
                var row = new OperationRow(planned.Operation.Path);
                ReviewViewModel.Fill(row, planned.Operation, planned.Index, null, [], 0);
                return row;
            })];
}
