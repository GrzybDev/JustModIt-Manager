using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

public sealed partial class ReviewViewModel
{
    private readonly Dictionary<string, StepRow> _stepRows = new(StringComparer.Ordinal);

    public ObservableCollection<StepRow> Steps { get; } = [];

    [ObservableProperty]
    public partial string StepsState { get; private set; } = "";

    [ObservableProperty]
    public partial bool StepsInOrder { get; private set; }

    [RelayCommand]
    private async Task ToAdvancedAsync()
    {
        if (_session.Review is not { } review || !await _page.Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(
                Strings.Creator_ToAdvancedTitle, Strings.Creator_ToAdvanced, Strings.Creator_ToAdvancedConfirm, Icons.ListOrdered)
            {
                ConfirmIcon = Icons.ListOrdered,
            }).ConfigureAwait(true))
        {
            return;
        }

        _session.Change(project => project.ToAdvanced(review.Plan));
    }

    [RelayCommand]
    private async Task ToSimpleAsync()
    {
        if (await _page.Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(Strings.Creator_ToSimpleTitle, Strings.Creator_ToSimple,
                Strings.Creator_ToSimpleConfirm, Icons.Undo, DialogTone.Danger)
            {
                ConfirmIcon = Icons.Undo,
            }).ConfigureAwait(true))
        {
            _session.Change(project => project with { Mode = CreatorMode.Simple, Steps = default });
        }
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
        _session.Change(project => project with { Steps = project.Steps.Add(step) });
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
    private void RemoveStep(StepRow row) =>
        _session.Change(project => project with { Steps = new(project.Steps.Items.RemoveAt(row.Index)) });

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
                row.Second = path;
            }

            row.First = path;
        }
    }

    /// <summary>Moves a step of an advanced project to another place.</summary>
    /// <param name="from">The step's place now.</param>
    /// <param name="to">Its new place.</param>
    internal void MoveStep(int from, int to)
    {
        var steps = _session.Project.Steps.Items;
        if (from == to || from < 0 || to < 0 || from >= steps.Length || to >= steps.Length)
        {
            return;
        }

        var step = steps[from];
        _session.Change(project => project with { Steps = new(steps.RemoveAt(from).Insert(to, step)) });
    }

    private void ShowSteps(PackagePlan plan, CreatorProject project)
    {
        var findings = plan.Findings.Where(finding => finding.StepIndex is not null && !IsHosting(finding.Kind))
            .ToLookup(finding => finding.StepIndex!.Value);
        var fresh = new List<StepRow>(project.Steps.Count);
        for (var i = 0; i < project.Steps.Count; i++)
        {
            fresh.Add(new StepRow("step:" + i.ToString(Text.Culture)));
        }

        var rows = ReviewRows.Reuse(_stepRows, fresh, null);
        for (var i = 0; i < rows.Count; i++)
        {
            Fill(rows[i], i, project.Steps, plan, findings[i]);
        }

        Controls.CardGrid.Sync(Steps, rows);
        var problems = plan.Findings.Count(finding => finding.StepIndex is not null && finding.BlocksBuild && !IsHosting(finding.Kind))
            + rows.Count(row => row.MoveBefore is not null);
        StepsInOrder = problems == 0;
        StepsState = StepsInOrder ? Strings.Creator_StepsInOrder : Text.Format(Strings.Creator_StepsProblems, ("count", problems));
    }

    private void Fill(StepRow row, int index, ValueList<ProjectStep> steps, PackagePlan plan, IEnumerable<PlanFinding> findings)
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
        (row.KindText, row.KindIcon, row.KindTone) = CreatorWording.Kind(kind);
        (row.First, row.Second, row.HasSecond, row.PicksAuthorFile) = (first, second, kind != OperationKind.Delete,
            kind is OperationKind.Copy or OperationKind.Patch);
        (row.FirstLabel, row.SecondLabel) = kind switch
        {
            OperationKind.Copy => (Strings.Creator_StepSource, Strings.Creator_StepCopyTo),
            OperationKind.Patch => (Strings.Creator_StepSource, Strings.Creator_StepPatchOf),
            OperationKind.Rename => (Strings.Creator_StepGameFile, Strings.Creator_StepNewName),
            _ => (Strings.Creator_StepGameFile, ""),
        };
        (row.CanMoveUp, row.CanMoveDown) = (index > 0, index < steps.Count - 1);
        row.MoveUpName = Text.Format(Strings.Creator_StepMoveUp, ("number", number));
        row.MoveDownName = Text.Format(Strings.Creator_StepMoveDown, ("number", number));
        row.RemoveName = Text.Format(Strings.Creator_StepRemove, ("number", number));
        row.DragName = Text.Format(Strings.Creator_StepDrag, ("number", number));
        List<PlanFinding> found = [.. findings];
        List<FindingLine> lines = [.. found.Select(Line)];
        if (!lines.SequenceEqual(row.Findings))
        {
            row.Findings = lines;
        }

        row.MoveBefore = WriterBefore(plan, index);
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
        row.IsFirstFaulty = row.MoveBefore is not null || Names(blocking, first, authorFile);
        row.IsSecondFaulty = row.HasSecond && Names(blocking, second, authorFile: false);
        row.HasProblem = row.MoveBefore is not null || blocking.Count > 0;
        row.Edited = Edit;
    }

    // A copy's two fields often hold the same path, so only the finding's kind tells which of them is wrong.
    private static bool Names(List<PlanFinding> findings, string field, bool authorFile) =>
        findings.Any(finding => (finding.Kind == FindingKind.AuthorFileMissing) == authorFile
            && PackagePath.Comparer.Equals(finding.Path, field.Trim()));

    // A rename of a game file that an earlier step copied over takes the author's file instead of the game's original,
    // which the author most likely meant to set aside first. Only the first step at that path finds the original there,
    // and a file new to the game has none. A patch needs the original in place, so patching and then renaming is no
    // mistake: it gives what renaming and then patching the new name gives.
    private static int? WriterBefore(PackagePlan plan, int index)
    {
        var operation = index < plan.Operations.Count ? plan.Operations[index] : null;
        if (operation is not { Kind: OperationKind.Rename, From: { } from }
            || operation.Original is not { } original || !PathComparison.IsWithin(plan.AuthorDirectory, original.FullPath))
        {
            return null;
        }

        for (var i = 0; i < index; i++)
        {
            var earlier = plan.Operations[i];
            if (PackagePath.Comparer.Equals(earlier.Path, from) || PackagePath.Comparer.Equals(earlier.From, from))
            {
                return earlier.Replaces is not null ? i : null;
            }
        }

        return null;
    }

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
        if (index < _session.Project.Steps.Count && _session.Project.Steps[index] != step)
        {
            _session.Change(project => project with { Steps = new(project.Steps.Items.SetItem(index, step)) });
        }
    }
}
