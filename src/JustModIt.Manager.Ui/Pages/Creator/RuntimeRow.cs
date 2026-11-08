using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>A file the package's software writes while the game runs, in the check's card of such files.</summary>
/// <param name="key">What the row stands for: the file's path.</param>
public sealed partial class RuntimeRow(string key) : ObservableObject, IReviewRow
{
    public string Key { get; } = key;

    /// <summary>The file's game path, which the plan declares.</summary>
    public required string Path { get; init; }

    /// <summary>A file of the comparison the author chose to declare; removing it takes the choice back.</summary>
    public ReviewEntry? Entry { get; set; }

    public string Folder => CreatorWording.FolderOf(Path);

    public string Name => CreatorWording.GameIndexName(Path);

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

    public string RemoveName => Text.Format(Strings.Creator_RemoveRuntime, ("path", Path));

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
