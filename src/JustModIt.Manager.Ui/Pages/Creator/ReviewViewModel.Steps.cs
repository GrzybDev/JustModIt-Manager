using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

public sealed partial class ReviewViewModel
{
    private readonly Dictionary<string, StepRow> _stepRows = new(StringComparer.Ordinal);
    private List<StepRow> _steps = [];
    private bool _showsAllSteps;

    /// <summary>The steps shown: every one but a long tail of copies and patches, until the author asks for it.</summary>
    public ObservableCollection<StepRow> Steps { get; } = [];

    /// <summary>How many steps the list folds at its end; empty while it folds none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreSteps))]
    public partial string MoreSteps { get; private set; } = "";

    public bool HasMoreSteps => MoreSteps.Length > 0;

    [RelayCommand]
    private async Task ToAdvancedAsync()
    {
        if (IsAdvanced || _session.Review is null || !await _page.Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(
                Strings.Creator_ToAdvancedTitle, Strings.Creator_ToAdvanced, Strings.Creator_ToAdvancedConfirm, Icons.ListOrdered)
            {
                ConfirmIcon = Icons.ListOrdered,
            }).ConfigureAwait(true))
        {
            // A switch the author declined leaves the mode's pills as the mode is.
            OnPropertyChanged(nameof(IsAdvanced));
            return;
        }

        await _session.SettledAsync().ConfigureAwait(true);
        List<(GameKey, PackagePlan)> plans = [.. PartCells().Where(cell => _session.ReviewOf(cell) is not null)
            .Select(cell => (cell.Version, _session.ReviewOf(cell)!.Plan))];
        _session.ChangePart(part => part.ToAdvanced(plans));
    }

    [RelayCommand]
    private async Task ToSimpleAsync()
    {
        if (IsAdvanced && await _page.Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(Strings.Creator_ToSimpleTitle, Strings.Creator_ToSimple,
                Strings.Creator_ToSimpleConfirm, Icons.Undo, DialogTone.Danger)
            {
                ConfirmIcon = Icons.Undo,
            }).ConfigureAwait(true))
        {
            _session.ChangePart(part => part with { Mode = CreatorMode.Simple, Steps = default });
            return;
        }

        OnPropertyChanged(nameof(IsAdvanced));
    }

    [RelayCommand]
    private void AddStep(string kind)
    {
        ProjectStep step = kind switch
        {
            nameof(OperationKind.Copy) => new CopyFileStep("", ""),
            nameof(OperationKind.Patch) => new PatchFileStep("", ""),
            nameof(OperationKind.Rename) => new RenameFileStep("", ""),
            _ => new DeleteFileStep(""),
        };
        _session.ChangePart(part => part with { Steps = part.Steps.Add(step) });
    }

    [RelayCommand]
    private void MoveUp(StepRow row) => MoveStep(row.Index, row.Index - 1);

    [RelayCommand]
    private void MoveDown(StepRow row) => MoveStep(row.Index, row.Index + 1);

    [RelayCommand]
    private void MoveBefore(StepRow row)
    {
        if (row.MoveBefore is { } target)
        {
            MoveStep(row.Index, target);
        }
    }

    [RelayCommand]
    private void RemoveStep(StepRow row)
    {
        Reopen(row.Index, null);
        _session.ChangePart(part => part with { Steps = new(part.Steps.Items.RemoveAt(row.Index)) });
    }

    [RelayCommand]
    private void ShowAllSteps()
    {
        _showsAllSteps = true;
        SyncSteps();
    }

    [RelayCommand]
    private async Task PickSourceAsync(StepRow row)
    {
        if (_session.Comparison is not { } comparison)
        {
            return;
        }

        var file = await _page.Services.Pickers.PickFileAsync(Strings.Creator_PickFileTitle, startIn: comparison.AuthorDirectory)
            .ConfigureAwait(true);
        if (file is not null && PathComparison.IsWithin(comparison.AuthorDirectory, file))
        {
            var path = PackagePath.FromFullPath(comparison.AuthorDirectory, file);
            if (row.Second.Length == 0)
            {
                row.Second = comparison.Game.Layout.ToPackage(path);
            }

            row.First = path;
        }
    }

    /// <summary>Moves a step of an advanced project to another place.</summary>
    /// <param name="from">The step's place now.</param>
    /// <param name="to">Its new place.</param>
    internal void MoveStep(int from, int to)
    {
        var steps = _session.Part.Steps.Items;
        if (from == to || from < 0 || to < 0 || from >= steps.Length || to >= steps.Length)
        {
            return;
        }

        var step = steps[from];
        Reopen(from, to);
        _session.ChangePart(part => part with { Steps = new(steps.RemoveAt(from).Insert(to, step)) });
    }

    private static string StepKey(int index) => "step:" + index.ToString(Text.Culture);

    // The cells of the part shown, one per version.
    private IEnumerable<CellKey> PartCells() => _session.Cells.Where(cell => cell.Part == _session.Part.Name);

    // A row is kept by its place, so the fields open in a row follow the step that moves or goes.
    private void Reopen(int from, int? to)
    {
        List<bool> open = [.. Enumerable.Range(0, _session.Part.Steps.Count).Select(i => _stepRows.GetValueOrDefault(StepKey(i))?.IsEditing == true)];
        var moved = open[from];
        open.RemoveAt(from);
        if (to is { } place)
        {
            open.Insert(place, moved);
        }

        for (var i = 0; i < open.Count; i++)
        {
            if (_stepRows.GetValueOrDefault(StepKey(i)) is { } row)
            {
                row.IsEditing = open[i];
            }
        }
    }

    private void ShowSteps(PackagePlan plan, ProjectPart part)
    {
        var findings = plan.Findings.Where(finding => finding.StepIndex is not null && !IsHosting(finding.Kind))
            .ToLookup(finding => finding.StepIndex!.Value);
        // What every version's plan makes of each step, e.g. the originals a patch meets on them.
        var operations = PartCells().Select(_session.ReviewOf).OfType<CreatorReview>().SelectMany(each => each.Plan.Operations)
            .Where(operation => operation.StepIndex is not null).ToLookup(operation => operation.StepIndex!.Value);
        var own = plan.Operations.Where(operation => operation.StepIndex is not null).ToDictionary(operation => operation.StepIndex!.Value);
        _steps = [.. Enumerable.Range(0, part.Steps.Count).Select(i => _stepRows.GetValueOrDefault(StepKey(i)) ?? new StepRow(StepKey(i)))];
        ReviewRows.Remember(_stepRows, _steps);
        for (var i = 0; i < _steps.Count; i++)
        {
            Fill(_steps[i], i, part.Steps, findings[i]);
            var row = _steps[i];
            var originals = operations[i].Where(operation => operation is { Kind: OperationKind.Patch, Original: not null })
                .Select(operation => operation.Original!.Stamp.Size).Distinct().Count();
            row.Detail = originals > 1 ? Text.Format(Strings.Creator_PatchVariants, ("count", originals))
                : own.GetValueOrDefault(i) is { Kind: OperationKind.Copy, SourceFile: { } source } ? Formats.FileSize(source.Stamp.Size)
                : "";
        }

        SyncSteps();
    }

    // A long tail of copies and patches without problems folds under one line, past its first step; only a tail, so a row's
    // place in the list stays its step's, which dragging relies on.
    private void SyncSteps()
    {
        var tail = _steps.Count - 1 - _steps.FindLastIndex(row => row.Kind is not (OperationKind.Copy or OperationKind.Patch) || row.HasFindings);
        var folded = _showsAllSteps || tail <= FoldedGroupSize ? 0 : tail - 1;
        Controls.CardGrid.Sync(Steps, _steps[..(_steps.Count - folded)]);
        MoreSteps = folded > 0 ? Text.Format(Strings.Creator_MoreSteps, ("count", folded)) : "";
    }

    private void Fill(StepRow row, int index, ValueList<ProjectStep> steps, IEnumerable<PlanFinding> findings)
    {
        row.Edited = null;
        var (kind, first, second) = steps[index] switch
        {
            CopyFileStep copy => (OperationKind.Copy, copy.Source, copy.Path),
            PatchFileStep patch => (OperationKind.Patch, patch.Source, patch.Path),
            RenameFileStep rename => (OperationKind.Rename, rename.From, rename.To),
            DeleteFileStep delete => (OperationKind.Delete, delete.Path, ""),
            _ => throw new ArgumentOutOfRangeException(nameof(steps)),
        };
        var number = (index + 1).ToString(Text.Culture);
        (row.Index, row.Number, row.Kind) = (index, number, kind);
        (row.First, row.Second) = (first, second);
        (row.CanMoveUp, row.CanMoveDown) = (index > 0, index < steps.Count - 1);
        FillVersions(row, steps[index].Only);
        List<PlanFinding> found = [.. findings];
        // The row says this one in words of its own, with the move that solves it.
        List<FindingLine> lines = [.. found.Where(finding => finding.MoveBefore is null).Select(Line)];
        if (!lines.SequenceEqual(row.Findings))
        {
            row.Findings = lines;
        }

        row.MoveBefore = found.Find(finding => finding.MoveBefore is not null)?.MoveBefore;
        if (row.MoveBefore is { } before)
        {
            var target = (before + 1).ToString(Text.Culture);
            row.MoveBeforeText = Text.Format(Strings.Creator_RenamesWritten, ("number", target), ("name", CreatorWording.GameIndexName(first)));
            row.MoveBeforeAction = Text.Format(Strings.Creator_MoveBefore, ("number", target));
        }

        var blocking = found.Where(finding => finding.BlocksBuild).ToList();
        var missing = blocking.FirstOrDefault(finding => finding.SuggestedPath is not null);
        row.Suggestion = missing?.SuggestedPath ?? "";
        row.SuggestionText = missing is null ? ""
            : Text.Format(Strings.Creator_FixPath, ("name", PackagePath.Comparer.Equals(CreatorWording.FolderOf(missing.Path!),
                CreatorWording.FolderOf(row.Suggestion)) ? CreatorWording.GameIndexName(row.Suggestion) : row.Suggestion));
        var authorFile = kind is OperationKind.Copy or OperationKind.Patch;
        row.IsFirstFaulty = Names(blocking, first, authorFile);
        row.IsSecondFaulty = row.HasSecond && Names(blocking, second, authorFile: false);
        row.HasProblem = blocking.Count > 0;
        row.IsEditing |= row.HasProblem;
        row.Edited = Edit;
    }

    // Pills of a project's versions, kept while its versions stay, so the one just pressed keeps the keyboard's focus.
    private void FillVersions(StepRow row, ValueList<GameKey> only)
    {
        var versions = _session.Project.Versions;
        if (versions.Count < 2)
        {
            row.Versions = [];
            return;
        }

        if (!row.Versions.Select(version => version.Key).SequenceEqual(versions.Select(version => version.Game.Key)))
        {
            row.Versions = [.. versions.Select(version => new StepVersion(row, version.Game.Key, GameWording.StoreName(version.Game.Key.Store)))];
        }

        foreach (var version in row.Versions)
        {
            version.IsOn = only.Count == 0 || only.Contains(version.Key);
        }

        row.ShowVersions();
        row.IsLimited = only.Count > 0;
        row.VersionsText = Text.Format(Strings.Creator_StepVersions, ("all", only.Count == 0 ? "true" : "false"),
            ("stores", only.Count == 0 ? "" : CreatorWording.List([.. only.Select(key => GameWording.StoreName(key.Store))])));
    }

    // A copy's two fields often hold the same path, so only the finding's kind tells which of them is wrong.
    private static bool Names(List<PlanFinding> findings, string field, bool authorFile) =>
        findings.Any(finding => (finding.Kind == FindingKind.AuthorFileMissing) == authorFile
            && PackagePath.Comparer.Equals(finding.Path, field.Trim()));

    private void Edit(StepRow row)
    {
        ProjectStep step = row.Kind switch
        {
            OperationKind.Copy => new CopyFileStep(row.First.Trim(), row.Second.Trim()),
            OperationKind.Patch => new PatchFileStep(row.First.Trim(), row.Second.Trim()),
            OperationKind.Rename => new RenameFileStep(row.First.Trim(), row.Second.Trim()),
            _ => new DeleteFileStep(row.First.Trim()),
        };
        var index = row.Index;
        var steps = _session.Part.Steps;
        if (index >= steps.Count)
        {
            return;
        }

        // Every version on is no limit, and a project of one version shows no pills, so its steps keep what they had.
        var edited = step with
        {
            Only = row.Versions.Count == 0 ? steps[index].Only
                : row.Versions.All(version => version.IsOn) ? default
                : [.. row.Versions.Where(version => version.IsOn).Select(version => version.Key)],
        };
        if (steps[index] != edited)
        {
            _session.ChangePart(part => part with { Steps = new(part.Steps.Items.SetItem(index, edited)) });
        }
    }
}
