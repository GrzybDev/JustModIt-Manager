using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

public sealed partial class ReviewViewModel
{
    private const int FoldedGroupSize = 7;

    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReviewRow> _operationRows = new(StringComparer.Ordinal);
    private List<IReviewRow> _operations = [];

    /// <summary>The operations shown: every one but those of a folded group of new files.</summary>
    public ObservableCollection<IReviewRow> Operations { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIdentical))]
    public partial IReadOnlyList<string> IdenticalFiles { get; private set; } = [];

    public bool HasIdentical => IdenticalFiles.Count > 0;

    [ObservableProperty]
    public partial string IdenticalCount { get; private set; } = "";

    [ObservableProperty]
    public partial bool ShowsIdentical { get; set; }

    /// <summary>The panel that adds a deletion or a rename of a game file is open.</summary>
    [ObservableProperty]
    public partial bool IsAdding { get; set; }

    /// <summary>The game file a rename is being added for; empty while none is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNamingRename))]
    public partial string RenameFrom { get; private set; } = "";

    public bool IsNamingRename => RenameFrom.Length > 0;

    [ObservableProperty]
    public partial string RenameTo { get; set; } = "";

    [ObservableProperty]
    public partial string AddNote { get; private set; } = "";

    [RelayCommand]
    private void Choose(ChoiceOption option)
    {
        if (Operations.OfType<OperationRow>().FirstOrDefault(row => row.Choices.Contains(option)) is { Entry: { } entry })
        {
            _session.ChangePart(part => part.WithChoice(entry, option.Choice));
        }
    }

    [RelayCommand]
    private void Exclude(OperationRow row)
    {
        if (row.Entry is { } entry && entry.Choices.Contains(FileChoice.Skip))
        {
            _session.ChangePart(part => part.WithChoice(entry, FileChoice.Skip));
        }
    }

    [RelayCommand]
    private void Restore(OperationRow row)
    {
        if (row.Entry is { } entry)
        {
            _session.ChangePart(part => part.WithChoice(entry, entry.DefaultChoice));
        }
    }

    [RelayCommand]
    private void Remove(OperationRow row)
    {
        if (row.ExtraStep is { } index)
        {
            _session.ChangePart(part => part.WithoutExtraStep(index));
        }
    }

    [RelayCommand]
    private void ToggleGroup(OperationGroup group)
    {
        group.IsExpanded = !group.IsExpanded;
        _expanded[group.Key] = group.IsExpanded;
        Controls.CardGrid.Sync(Operations, Visible(_operations));
    }

    [RelayCommand]
    private void ToggleIdentical() => ShowsIdentical = !ShowsIdentical;

    [RelayCommand]
    private void ToggleAdding()
    {
        IsAdding = !IsAdding;
        RenameFrom = "";
    }

    [RelayCommand]
    private async Task AddDeleteAsync()
    {
        if (await PickGameFileAsync().ConfigureAwait(true) is { } path)
        {
            _session.ChangePart(part => part.WithExtraStep(new DeleteFileStep(path)));
            IsAdding = false;
        }
    }

    [RelayCommand]
    private async Task AddRenameAsync()
    {
        if (await PickGameFileAsync().ConfigureAwait(true) is { } path)
        {
            RenameFrom = path;
            RenameTo = Path.ChangeExtension(path, null) + "_o" + Path.GetExtension(path);
        }
    }

    [RelayCommand]
    private void ConfirmRename()
    {
        if (RenameTo.Trim() is { Length: > 0 } to)
        {
            var from = RenameFrom;
            _session.ChangePart(part => part.WithExtraStep(new RenameFileStep(from, to)));
        }

        (RenameFrom, IsAdding) = ("", false);
    }

    private void ShowOperations(List<IReviewRow> fresh)
    {
        OperationGroup? group = null;
        _operations = ReviewRows.Reuse(_operationRows, fresh, (from, to) =>
        {
            switch (from, to)
            {
                case (OperationGroup newer, OperationGroup kept):
                    (kept.Folder, kept.Count, kept.Range, kept.Size) = (newer.Folder, newer.Count, newer.Range, newer.Size);
                    break;
                case (OperationRow newer, OperationRow kept):
                    Copy(newer, kept);
                    break;
            }
        });
        foreach (var row in _operations)
        {
            switch (row)
            {
                case OperationGroup head:
                    group = head;
                    break;
                case OperationRow { Group: not null } member:
                    member.Group = group;
                    break;
            }
        }

        Controls.CardGrid.Sync(Operations, Visible(_operations));
    }

    private static List<IReviewRow> Visible(List<IReviewRow> rows) =>
        [.. rows.Where(row => row is not OperationRow { Group.IsExpanded: false })];

    // New files of one folder that follow each other form a group under one head, so a folder of many files reads as
    // one row.
    private List<IReviewRow> OperationRows(CreatorReview review, ILookup<int, PlanFinding> findings)
    {
        var plan = review.Plan;
        var operations = plan.Operations;
        var entries = new Dictionary<string, ReviewEntry>(PackagePath.Comparer);
        foreach (var entry in review.Entries.Where(entry => entry.Choice != FileChoice.Skip))
        {
            entries.TryAdd(entry.File.Path, entry);
        }

        var renames = new Dictionary<string, int>(PackagePath.Comparer);
        for (var i = 0; i < operations.Count; i++)
        {
            if (operations[i] is { Kind: OperationKind.Rename, From: { } from })
            {
                renames.TryAdd(from, i + 1);
            }
        }

        var refused = plan.Findings.Where(finding => finding.OperationIndex is not null && IsHosting(finding.Kind)
            && finding.Severity == FindingSeverity.Error).Select(finding => finding.OperationIndex!.Value).ToHashSet();
        var keys = new RowKeys();
        var rows = new List<IReviewRow>();
        for (var i = 0; i < operations.Count;)
        {
            var last = i;
            while (IsNewFile(operations[i]) && last + 1 < operations.Count && IsNewFile(operations[last + 1])
                && CreatorWording.FolderOf(operations[last + 1].Path) == CreatorWording.FolderOf(operations[i].Path))
            {
                last++;
            }

            var group = last > i ? NewGroup(operations, i, last, keys) : null;
            if (group is not null)
            {
                rows.Add(group);
            }

            for (; i <= last; i++)
            {
                var operation = operations[i];
                var row = new OperationRow(keys.Next(KeyOf(operation))) { Group = group };
                var entry = operation.Reason == OperationReason.AddedStep ? null : entries.GetValueOrDefault(operation.Path);
                Fill(row, operation, i, entry, findings[i], renames.GetValueOrDefault(operation.Path));
                row.BreaksRules = refused.Contains(i);
                rows.Add(row);
            }
        }

        foreach (var entry in review.Entries.Where(entry => entry.Choice == FileChoice.Skip
            && (entry.IsChangedByAuthor || entry.File.Difference == FileDifference.Missing)))
        {
            var row = new OperationRow(keys.Next("left out:" + entry.File.Path));
            Fill(row, entry);
            rows.Add(row);
        }

        return rows;
    }

    private OperationGroup NewGroup(ValueList<PlannedOperation> operations, int first, int last, RowKeys keys)
    {
        var folder = CreatorWording.FolderOf(operations[first].Path).TrimEnd('/');
        var key = keys.Next("group:" + folder);
        return new OperationGroup(key)
        {
            Folder = folder,
            Count = Text.Format(Strings.Creator_GroupCount, ("count", last - first + 1)),
            Range = Text.Format(Strings.Creator_GroupRange, ("first", first + 1), ("last", last + 1)),
            Size = Formats.FileSize(Enumerable.Range(first, last - first + 1).Sum(i => operations[i].SourceFile?.Stamp.Size ?? 0)),
            IsExpanded = _expanded.TryGetValue(key, out var expanded) ? expanded : last - first + 1 < FoldedGroupSize,
        };
    }

    private static void Copy(OperationRow from, OperationRow to)
    {
        (to.Entry, to.ExtraStep, to.Group, to.Number, to.KindText, to.KindIcon, to.KindTone, to.ShowsKind) =
            (from.Entry, from.ExtraStep, from.Group, from.Number, from.KindText, from.KindIcon, from.KindTone, from.ShowsKind);
        (to.Folder, to.Name, to.FromFolder, to.FromName, to.Reason, to.Size) =
            (from.Folder, from.Name, from.FromFolder, from.FromName, from.Reason, from.Size);
        (to.CanEdit, to.CanExclude, to.CanRemove, to.IsExcluded, to.IsDecision) =
            (from.CanEdit, from.CanExclude, from.CanRemove, from.IsExcluded, from.IsDecision);
        // New pills would take the keyboard focus from the one just pressed.
        if (!to.Choices.Select(choice => (choice.Choice, choice.IsChecked))
            .SequenceEqual(from.Choices.Select(choice => (choice.Choice, choice.IsChecked))))
        {
            to.Choices = from.Choices;
        }

        (to.EditText, to.EditNote, to.BreaksRules) = (from.EditText, from.EditNote, from.BreaksRules);
        if (!to.Findings.SequenceEqual(from.Findings))
        {
            to.Findings = from.Findings;
        }
    }

    private static bool IsNewFile(PlannedOperation operation) =>
        operation is { Kind: OperationKind.Copy, Reason: OperationReason.NewFile };

    /// <summary>Fills a row with what it shows of an operation of the plan.</summary>
    /// <param name="row">The row.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="index">Its place in the plan, from 0.</param>
    /// <param name="entry">The compared file behind it, whose choices the row offers; null for none.</param>
    /// <param name="findings">The plan's findings about it.</param>
    /// <param name="renamedBy">The number of the operation that renames its original away; 0 for none.</param>
    internal static void Fill(OperationRow row, PlannedOperation operation, int index, ReviewEntry? entry,
        IEnumerable<PlanFinding> findings, int renamedBy)
    {
        var (kindText, kindIcon, kindTone) = CreatorWording.Kind(operation.Kind);
        (row.Number, row.KindText, row.KindIcon, row.KindTone) = ((index + 1).ToString(Text.Culture), kindText, kindIcon, kindTone);
        (row.Folder, row.Name) = (CreatorWording.FolderOf(operation.Path), CreatorWording.GameIndexName(operation.Path));
        (row.FromFolder, row.FromName) = operation.From is { } from
            ? (CreatorWording.FolderOf(from), CreatorWording.GameIndexName(from))
            : ("", "");
        row.Reason = IsNewFile(operation) ? ""
            : CreatorWording.Why(operation, operation.Reason == OperationReason.OriginalRenamedAway ? renamedBy : 0);
        row.Size = operation.Kind == OperationKind.Copy && operation.SourceFile is { } source ? Formats.FileSize(source.Stamp.Size) : "";
        row.Entry = entry;
        row.ExtraStep = operation.Reason == OperationReason.AddedStep ? operation.StepIndex : null;
        row.CanEdit = entry?.Choices.Count > 1;
        row.CanExclude = entry?.Choices.Contains(FileChoice.Skip) == true;
        row.CanRemove = row.ExtraStep is not null;
        row.IsExcluded = false;
        row.IsDecision = false;
        row.ShowsKind = row.Group is null;
        row.Findings = [.. findings.Select(Line)];
        if (entry is not null)
        {
            row.Choices = ChoicesOf(entry);
            row.EditText = Text.Format(Strings.Creator_EditText, ("difference", entry.File.Difference.ToString()),
                ("from", CreatorWording.GameIndexName(entry.File.RenameFrom ?? "")));
            row.EditNote = entry.Choices.Contains(FileChoice.Patch) ? Strings.Creator_WholeFileNote : "";
        }

        if (entry?.File.ExpectedFolder is { } expected)
        {
            row.Findings = [.. row.Findings, new FindingLine(Text.Format(Strings.Creator_ExpectedFolder, ("folder", expected)), RowTone.Warning)];
        }
    }

    private static void Fill(OperationRow row, ReviewEntry entry)
    {
        var missing = entry.File.Difference == FileDifference.Missing;
        (row.Number, row.Folder, row.Name) = ("–", CreatorWording.FolderOf(entry.File.Path), CreatorWording.GameIndexName(entry.File.Path));
        (row.KindText, row.KindIcon, row.KindTone) = missing
            ? (Strings.Creator_KindKept, Icons.File, RowTone.Neutral)
            : (Strings.Creator_KindExcluded, Icons.CircleMinus, RowTone.Neutral);
        row.Entry = entry;
        row.IsExcluded = !missing;
        row.IsDecision = missing;
        var folder = CreatorWording.FolderOf(entry.File.Path).TrimEnd('/');
        row.Reason = missing
            ? Text.Format(Strings.Creator_MissingReason, ("folder", folder), ("root", folder.Length == 0 ? "true" : "false"))
            : Strings.Creator_ExcludedReason;
        row.Size = entry.File.Author is { } author ? Formats.FileSize(author.Size) : "";
        row.Choices = missing ? ChoicesOf(entry) : [];
        (row.CanEdit, row.CanExclude, row.CanRemove, row.ShowsKind, row.BreaksRules) = (false, false, false, true, false);
        row.Findings = [];
    }

    private static IReadOnlyList<ChoiceOption> ChoicesOf(ReviewEntry entry) =>
    [
        .. entry.Choices.Select(choice => new ChoiceOption(choice, CreatorWording.Choice(choice, entry.File.Difference),
            choice == entry.DefaultChoice && entry.Choices.Count > 1)
        {
            IsChecked = choice == entry.Choice,
        }),
    ];

    // A simple project has one operation per path, so a file keeps its row, and its open editor, when its kind changes.
    private static string KeyOf(PlannedOperation operation) =>
        operation.Reason == OperationReason.AddedStep
            ? $"added:{operation.StepIndex}"
            : $"file:{operation.Path}";
}
