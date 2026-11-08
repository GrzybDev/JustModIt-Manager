using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>A version of the game a step of an advanced part may run on, as a pill in the step's row.</summary>
/// <param name="row">The step's row, told when the author turns the pill.</param>
/// <param name="key">The version's game.</param>
/// <param name="store">The version's store, which names the pill.</param>
public sealed partial class StepVersion(StepRow row, GameKey key, string store) : ObservableObject
{
    public GameKey Key { get; } = key;

    public string Store { get; } = store;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <summary>The pill can turn; the last one on cannot, since a step limited to no version would run on every one.</summary>
    [ObservableProperty]
    public partial bool CanTurn { get; set; } = true;

    partial void OnIsOnChanged(bool value)
    {
        row.ShowVersions();
        row.Edited?.Invoke(row);
    }
}

/// <summary>A step of an advanced part: one line, which unfolds the step's paths to edit them in place.</summary>
/// <param name="key">What the row stands for.</param>
public sealed partial class StepRow(string key) : ObservableObject, IReviewRow
{
    public string Key { get; } = key;

    public int Index { get; set; }

    /// <summary>The step's fields are open; a problem opens them, and they stay open until the author folds them.</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [RelayCommand]
    private void ToggleEdit() => IsEditing = !IsEditing;

    public string EditName => Text.Format(Strings.Creator_EditStep, ("number", Number));

    /// <summary>The step's paths on one line; a file that keeps its path is named once.</summary>
    public string Summary => !HasSecond ? First : string.Equals(First, Second, StringComparison.OrdinalIgnoreCase) ? Second : $"{First} → {Second}";

    /// <summary>A copy's size, or how many originals a patch meets across the versions; empty for neither.</summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = "";

    /// <summary>The versions of the game the step may run on, as pills; none in a project of one version.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVersions))]
    public partial IReadOnlyList<StepVersion> Versions { get; set; } = [];

    public bool HasVersions => Versions.Count > 0;

    /// <summary>The versions the step runs on, as its line says them.</summary>
    [ObservableProperty]
    public partial string VersionsText { get; set; } = "";

    /// <summary>The step runs on some versions only, which its line marks.</summary>
    [ObservableProperty]
    public partial bool IsLimited { get; set; }

    public string VersionsName => Text.Format(Strings.Creator_StepVersionsName, ("number", Number));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditName), nameof(VersionsName), nameof(MoveUpName), nameof(MoveDownName), nameof(RemoveName), nameof(DragName))]
    public partial string Number { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindText), nameof(KindIcon), nameof(KindTone), nameof(FirstLabel), nameof(SecondLabel), nameof(HasSecond),
        nameof(PicksAuthorFile), nameof(Summary))]
    public partial OperationKind Kind { get; set; }

    public string KindText => CreatorWording.Kind(Kind).Text;

    public Geometry KindIcon => CreatorWording.Kind(Kind).Icon;

    public Tone KindTone => CreatorWording.Kind(Kind).Tone;

    public string FirstLabel => PicksAuthorFile ? Strings.Creator_StepSource : Strings.Creator_StepGameFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string First { get; set; } = "";

    public string SecondLabel => Kind switch
    {
        OperationKind.Copy => Strings.Creator_StepCopyTo,
        OperationKind.Patch => Strings.Creator_StepPatchOf,
        OperationKind.Rename => Strings.Creator_StepNewName,
        _ => "",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string Second { get; set; } = "";

    public bool HasSecond => Kind != OperationKind.Delete;

    /// <summary>The first path is the author's file, which a dialog can pick.</summary>
    public bool PicksAuthorFile => Kind is OperationKind.Copy or OperationKind.Patch;

    [ObservableProperty]
    public partial bool CanMoveUp { get; set; }

    [ObservableProperty]
    public partial bool CanMoveDown { get; set; }

    public string MoveUpName => Text.Format(Strings.Creator_StepMoveUp, ("number", Number));

    public string MoveDownName => Text.Format(Strings.Creator_StepMoveDown, ("number", Number));

    public string RemoveName => Text.Format(Strings.Creator_StepRemove, ("number", Number));

    public string DragName => Text.Format(Strings.Creator_StepDrag, ("number", Number));

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

    /// <summary>Called when the author typed a path or turned a version.</summary>
    public Action<StepRow>? Edited { get; set; }

    partial void OnFirstChanged(string value) => Edited?.Invoke(this);

    partial void OnSecondChanged(string value) => Edited?.Invoke(this);

    /// <summary>Keeps the last version on from turning off.</summary>
    internal void ShowVersions()
    {
        var on = Versions.Count(version => version.IsOn);
        foreach (var version in Versions)
        {
            version.CanTurn = !version.IsOn || on > 1;
        }
    }
}
