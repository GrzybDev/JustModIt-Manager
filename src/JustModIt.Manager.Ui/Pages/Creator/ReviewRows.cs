using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>How a row of the check is tinted.</summary>
public enum RowTone
{
    Neutral,
    Success,
    Warning,
    Danger,
}

/// <summary>A row of a list that the check keeps while its item stays, so a control in it keeps its focus.</summary>
public interface IReviewRow
{
    /// <summary>What the row stands for, the same across plans.</summary>
    string Key { get; }
}

/// <summary>What a file of the review may be turned into, as a pill.</summary>
/// <param name="choice">The choice.</param>
/// <param name="title">How the pill names it.</param>
/// <param name="isDefault">The choice the comparison made.</param>
public sealed partial class ChoiceOption(FileChoice choice, string title, bool isDefault) : ObservableObject
{
    public FileChoice Choice { get; } = choice;

    public string Title { get; } = title;

    public bool IsDefault { get; } = isDefault;

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}

/// <summary>A problem or warning about an operation or step, which the author can accept when the rules allow it.</summary>
/// <param name="Text">What it is about, as a sentence.</param>
/// <param name="Tone">Its colour.</param>
/// <param name="AcknowledgementKey">The key that accepts it; null for one that cannot be accepted.</param>
/// <param name="IsAccepted">The author accepted it, which the line says in place of the way to accept it.</param>
public sealed record FindingLine(string Text, RowTone Tone, string? AcknowledgementKey = null, bool IsAccepted = false)
{
    public bool CanAcknowledge => AcknowledgementKey is not null && !IsAccepted;

    public Geometry Icon => Tone == RowTone.Danger ? Theme.Icons.CircleX : Theme.Icons.TriangleAlert;
}

/// <summary>An operation of a simple project, or a file the package leaves out that the author decides about.</summary>
/// <param name="key">What the row stands for.</param>
public sealed partial class OperationRow(string key) : ObservableObject, IReviewRow
{
    public string Key { get; } = key;

    /// <summary>The compared file behind the row; null for an operation the author added.</summary>
    public ReviewEntry? Entry { get; set; }

    /// <summary>The extra step behind the row; null for a compared file.</summary>
    public int? ExtraStep { get; set; }

    /// <summary>The group of new files the row belongs to; null outside a group.</summary>
    public OperationGroup? Group { get; set; }

    [ObservableProperty]
    public partial string Number { get; set; } = "";

    [ObservableProperty]
    public partial string KindText { get; set; } = "";

    [ObservableProperty]
    public partial Geometry KindIcon { get; set; } = Theme.Icons.Copy;

    [ObservableProperty]
    public partial RowTone KindTone { get; set; }

    /// <summary>The kind is shown on the group's head, so rows inside a group leave it out.</summary>
    [ObservableProperty]
    public partial bool ShowsKind { get; set; } = true;

    [ObservableProperty]
    public partial string Folder { get; set; } = "";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>The game file a rename takes; empty for any other operation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRename))]
    public partial string FromFolder { get; set; } = "";

    [ObservableProperty]
    public partial string FromName { get; set; } = "";

    public bool IsRename => FromName.Length > 0;

    [ObservableProperty]
    public partial string Reason { get; set; } = "";

    [ObservableProperty]
    public partial string Size { get; set; } = "";

    [ObservableProperty]
    public partial bool CanEdit { get; set; }

    [ObservableProperty]
    public partial bool CanExclude { get; set; }

    [ObservableProperty]
    public partial bool CanRemove { get; set; }

    /// <summary>The author left the file out, which the row says with a way back.</summary>
    [ObservableProperty]
    public partial bool IsExcluded { get; set; }

    /// <summary>A file the author probably removed, whose choice stands in the row.</summary>
    [ObservableProperty]
    public partial bool IsDecision { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [RelayCommand]
    private void ToggleEdit() => IsEditing = !IsEditing;

    [ObservableProperty]
    public partial IReadOnlyList<ChoiceOption> Choices { get; set; } = [];

    /// <summary>What the editor says about the choices; empty for none.</summary>
    [ObservableProperty]
    public partial string EditText { get; set; } = "";

    /// <summary>A note under the editor, e.g. what carrying a whole game file asks; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditNote))]
    public partial string EditNote { get; set; } = "";

    public bool HasEditNote => EditNote.Length > 0;

    /// <summary>A hosting rule refuses the operation; the rules above say why.</summary>
    [ObservableProperty]
    public partial bool BreaksRules { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFindings))]
    public partial IReadOnlyList<FindingLine> Findings { get; set; } = [];

    public bool HasFindings => Findings.Count > 0;
}

/// <summary>The head of new files in one folder, which folds them into one row.</summary>
/// <param name="key">What the row stands for.</param>
public sealed partial class OperationGroup(string key) : ObservableObject, IReviewRow
{
    public string Key { get; } = key;

    [ObservableProperty]
    public partial string Folder { get; set; } = "";

    [ObservableProperty]
    public partial string Count { get; set; } = "";

    /// <summary>The operations it holds, shown while it is folded.</summary>
    [ObservableProperty]
    public partial string Range { get; set; } = "";

    [ObservableProperty]
    public partial string Size { get; set; } = "";

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;
}

/// <summary>What a hosting rule says about the package, with what the author can do about it.</summary>
public enum RuleKind
{
    /// <summary>The site does not take the package with it; the file has to go.</summary>
    Error,

    /// <summary>A warning the author may accept.</summary>
    Accept,

    /// <summary>A third-party tool, which needs its licence and source.</summary>
    Tool,

    /// <summary>A library the package adds, which is the author's own code or, when the author says so, a tool.</summary>
    Library,
}

/// <summary>A finding of the hosting rules in the rules' card.</summary>
/// <param name="key">What the row stands for.</param>
public sealed partial class RuleRow(string key) : ObservableObject, IReviewRow
{
    public string Key { get; } = key;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsError), nameof(IsAccept), nameof(IsTool), nameof(IsLibrary), nameof(NeedsNotice),
        nameof(OffersNotice), nameof(CanRemoveNotice))]
    public partial RuleKind Kind { get; set; }

    public bool IsError => Kind == RuleKind.Error;

    public bool IsAccept => Kind == RuleKind.Accept;

    public bool IsTool => Kind == RuleKind.Tool;

    public bool IsLibrary => Kind == RuleKind.Library;

    /// <summary>The author said the library is another author's tool.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemoveNotice))]
    public partial bool IsThirdParty { get; set; }

    /// <summary>The project holds the library's licence and source, as far as the author gave them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsNotice), nameof(OffersNotice), nameof(CanRemoveNotice))]
    public partial bool HasNotice { get; set; }

    /// <summary>The row asks for the tool's or the library's licence and source.</summary>
    public bool NeedsNotice => IsTool || (IsLibrary && HasNotice);

    /// <summary>The author's own library, which may carry a licence and source once the author adds them.</summary>
    public bool OffersNotice => IsLibrary && !HasNotice;

    public bool CanRemoveNotice => IsLibrary && HasNotice && !IsThirdParty;

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial RowTone Tone { get; set; }

    [ObservableProperty]
    public partial Geometry Icon { get; set; } = Theme.Icons.CircleX;

    /// <summary>The findings behind the row: one, or one for each operation the rule finds the same file in.</summary>
    public IReadOnlyList<PlanFinding> Findings { get; set; } = [];

    /// <summary>The row can leave the file out, as the findings' operations offer it.</summary>
    [ObservableProperty]
    public partial bool CanExclude { get; set; }

    [ObservableProperty]
    public partial bool IsAccepted { get; set; }

    /// <summary>The tool's id, for a tool's row.</summary>
    public string ToolId { get; set; } = "";

    /// <summary>The library's game path, for a library's row.</summary>
    public string Path { get; set; } = "";

    /// <summary>The library's file name, which names its notice until the author names the tool.</summary>
    public string FileName => CreatorWording.GameIndexName(Path);

    /// <summary>What the package's description calls the library.</summary>
    [ObservableProperty]
    public partial string LibraryName { get; set; } = "";

    [ObservableProperty]
    public partial string License { get; set; } = "";

    [ObservableProperty]
    public partial string Source { get; set; } = "";

    [ObservableProperty]
    public partial bool IsComplete { get; set; }

    /// <summary>Called when the author typed the tool's licence or source, or the library's name.</summary>
    public Action<RuleRow>? Edited { get; set; }

    partial void OnLibraryNameChanged(string value) => Edited?.Invoke(this);

    partial void OnLicenseChanged(string value) => Edited?.Invoke(this);

    partial void OnSourceChanged(string value) => Edited?.Invoke(this);
}

/// <summary>A file the package's software writes while the game runs, in the check's card of such files.</summary>
/// <param name="key">What the row stands for: the file's path.</param>
public sealed partial class RuntimeRow(string key) : ObservableObject, IReviewRow
{
    public string Key { get; } = key;

    /// <summary>The file's game path, which the plan declares.</summary>
    public string Path { get; set; } = "";

    /// <summary>A file of the comparison the author chose to declare; removing it takes the choice back.</summary>
    public ReviewEntry? Entry { get; set; }

    [ObservableProperty]
    public partial string Folder { get; set; } = "";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>How the file stands to the game, as its chip says it.</summary>
    [ObservableProperty]
    public partial string State { get; set; } = "";

    [ObservableProperty]
    public partial Geometry StateIcon { get; set; } = Theme.Icons.Copy;

    [ObservableProperty]
    public partial bool IsFromChoice { get; set; }

    /// <summary>Why the package does not carry the file, for one chosen among the compared files; empty otherwise.</summary>
    [ObservableProperty]
    public partial string Reason { get; set; } = "";

    [ObservableProperty]
    public partial string RemoveName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFindings))]
    public partial IReadOnlyList<FindingLine> Findings { get; set; } = [];

    public bool HasFindings => Findings.Count > 0;

    /// <summary>Something keeps the file from being declared, which tints the row.</summary>
    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    /// <summary>The game already holds a file the software creates, which the store's check, its folder and a new comparison fix.</summary>
    [ObservableProperty]
    public partial bool OffersFixes { get; set; }

    /// <summary>The game file a mistyped path most likely means; empty when there is none to offer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestion))]
    public partial string Suggestion { get; set; } = "";

    public bool HasSuggestion => Suggestion.Length > 0;

    [ObservableProperty]
    public partial string SuggestionText { get; set; } = "";
}

/// <summary>A step of an advanced project, its paths edited in place.</summary>
/// <param name="key">What the row stands for.</param>
public sealed partial class StepRow(string key) : ObservableObject, IReviewRow
{
    public string Key { get; } = key;

    public int Index { get; set; }

    [ObservableProperty]
    public partial string Number { get; set; } = "";

    [ObservableProperty]
    public partial OperationKind Kind { get; set; }

    [ObservableProperty]
    public partial string KindText { get; set; } = "";

    [ObservableProperty]
    public partial Geometry KindIcon { get; set; } = Theme.Icons.Copy;

    [ObservableProperty]
    public partial RowTone KindTone { get; set; }

    [ObservableProperty]
    public partial string FirstLabel { get; set; } = "";

    [ObservableProperty]
    public partial string First { get; set; } = "";

    [ObservableProperty]
    public partial string SecondLabel { get; set; } = "";

    [ObservableProperty]
    public partial string Second { get; set; } = "";

    [ObservableProperty]
    public partial bool HasSecond { get; set; }

    /// <summary>The first path is the author's file, which a dialog can pick.</summary>
    [ObservableProperty]
    public partial bool PicksAuthorFile { get; set; }

    [ObservableProperty]
    public partial bool CanMoveUp { get; set; }

    [ObservableProperty]
    public partial bool CanMoveDown { get; set; }

    [ObservableProperty]
    public partial string MoveUpName { get; set; } = "";

    [ObservableProperty]
    public partial string MoveDownName { get; set; } = "";

    [ObservableProperty]
    public partial string RemoveName { get; set; } = "";

    [ObservableProperty]
    public partial string DragName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFindings))]
    public partial IReadOnlyList<FindingLine> Findings { get; set; } = [];

    public bool HasFindings => Findings.Count > 0 || MoveBefore is not null;

    /// <summary>The step this rename should come before, as it takes the file that step wrote; null when it is in place.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFindings))]
    public partial int? MoveBefore { get; set; }

    [ObservableProperty]
    public partial string MoveBeforeText { get; set; } = "";

    [ObservableProperty]
    public partial string MoveBeforeAction { get; set; } = "";

    /// <summary>Something keeps the step from running as the author meant it, which tints the row.</summary>
    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    /// <summary>The first path is the one a problem is about.</summary>
    [ObservableProperty]
    public partial bool IsFirstFaulty { get; set; }

    /// <summary>The second path is the one a problem is about.</summary>
    [ObservableProperty]
    public partial bool IsSecondFaulty { get; set; }

    /// <summary>The game file a mistyped path most likely means; empty when there is none to offer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSuggestion))]
    public partial string Suggestion { get; set; } = "";

    public bool HasSuggestion => Suggestion.Length > 0;

    [ObservableProperty]
    public partial string SuggestionText { get; set; } = "";

    // Only a game path can be missing, which a patch names second and a rename or a deletion first.
    [RelayCommand]
    private void UseSuggestion()
    {
        if (Kind == OperationKind.Patch)
        {
            Second = Suggestion;
        }
        else
        {
            First = Suggestion;
        }
    }

    /// <summary>Called when the author typed a path.</summary>
    public Action<StepRow>? Edited { get; set; }

    partial void OnFirstChanged(string value) => Edited?.Invoke(this);

    partial void OnSecondChanged(string value) => Edited?.Invoke(this);
}

/// <summary>Brings the shown rows in line with the rows of a new plan, keeping each instance that stays.</summary>
internal static class ReviewRows
{
    /// <summary>The rows to show, each taking the instance shown before under its key, brought up to date.</summary>
    /// <param name="known">The rows shown before, by key; replaced with the rows returned.</param>
    /// <param name="fresh">The rows of the new plan, in order, with keys of their own.</param>
    /// <param name="update">Copies a fresh row's values into the instance shown before; null keeps the instance as it is.</param>
    /// <typeparam name="TRow">The rows' type.</typeparam>
    public static List<TRow> Reuse<TRow>(Dictionary<string, TRow> known, IReadOnlyList<TRow> fresh, Action<TRow, TRow>? update)
        where TRow : class, IReviewRow
    {
        var rows = new List<TRow>(fresh.Count);
        foreach (var row in fresh)
        {
            if (known.GetValueOrDefault(row.Key) is { } kept && kept.GetType() == row.GetType())
            {
                update?.Invoke(row, kept);
                rows.Add(kept);
            }
            else
            {
                rows.Add(row);
            }
        }

        known.Clear();
        foreach (var row in rows)
        {
            known[row.Key] = row;
        }

        return rows;
    }

    /// <summary>Removes, moves and adds as few rows as it can, so a row that stays keeps its controls and their focus.</summary>
    /// <param name="rows">The rows shown.</param>
    /// <param name="wanted">The rows to show, in order, each instance once.</param>
    /// <typeparam name="TRow">The rows' type.</typeparam>
    public static void Show<TRow>(ObservableCollection<TRow> rows, IReadOnlyList<TRow> wanted)
        where TRow : class
    {
        var keep = new HashSet<TRow>(wanted, ReferenceEqualityComparer.Instance);
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(rows[i]))
            {
                rows.RemoveAt(i);
            }
        }

        var shown = new HashSet<TRow>(rows, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < wanted.Count; i++)
        {
            var row = wanted[i];
            if (i < rows.Count && ReferenceEquals(rows[i], row))
            {
                continue;
            }

            if (shown.Contains(row))
            {
                rows.Move(IndexOf(rows, row, i + 1), i);
            }
            else
            {
                rows.Insert(i, row);
            }
        }
    }

    private static int IndexOf<TRow>(ObservableCollection<TRow> rows, TRow row, int start)
        where TRow : class
    {
        for (var i = start; i < rows.Count; i++)
        {
            if (ReferenceEquals(rows[i], row))
            {
                return i;
            }
        }

        throw new ArgumentException("The row is not shown.", nameof(row));
    }
}

/// <summary>Numbers repeated keys, so each row of a list has one of its own, e.g. two runs of new files in one folder.</summary>
internal sealed class RowKeys
{
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);

    /// <summary>The key itself the first time, then with how many times it came.</summary>
    /// <param name="key">What the row stands for.</param>
    public string Next(string key)
    {
        var count = _seen.GetValueOrDefault(key) + 1;
        _seen[key] = count;
        return count == 1 ? key : key + "#" + count.ToString(CultureInfo.InvariantCulture);
    }
}
