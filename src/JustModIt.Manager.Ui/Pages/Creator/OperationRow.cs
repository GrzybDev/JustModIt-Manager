using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Creator;

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
    public partial Tone KindTone { get; set; }

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
