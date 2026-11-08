using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Creator;

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
    public partial Tone Tone { get; set; }

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

    /// <summary>The library's file name as its notice takes it, which names the notice until the author names the tool.</summary>
    public string FileName => LibraryNotice.NameAfterFile(Path);

    /// <summary>What the package's description calls the library.</summary>
    [ObservableProperty]
    public partial string LibraryName { get; set; } = "";

    [ObservableProperty]
    public partial string License { get; set; } = "";

    [ObservableProperty]
    public partial string Source { get; set; } = "";

    [ObservableProperty]
    public partial bool IsComplete { get; set; }

    /// <summary>What another project of the library gives for it, offered while the row's own is incomplete; null for none.</summary>
    public KnownNotice? KnownNotice { get; set; }

    /// <summary>The words of the link that takes <see cref="KnownNotice"/>; empty while there is none.</summary>
    [ObservableProperty]
    public partial string UseNoticeText { get; set; } = "";

    /// <summary>Called when the author typed the tool's licence or source, or the library's name.</summary>
    public Action<RuleRow>? Edited { get; set; }

    // Only on the author's click, since filling it in unasked would clear a block the author never looked at; one edit for all three.
    [RelayCommand]
    private void UseNotice()
    {
        if (KnownNotice is not { } known || Edited is not { } edited)
        {
            return;
        }

        Edited = null;
        (License, Source, LibraryName) = (known.License, known.SourceUrl, known.Name.Length > 0 ? known.Name : LibraryName);
        Edited = edited;
        edited(this);
    }

    partial void OnLibraryNameChanged(string value) => Edited?.Invoke(this);

    partial void OnLicenseChanged(string value) => Edited?.Invoke(this);

    partial void OnSourceChanged(string value) => Edited?.Invoke(this);
}
