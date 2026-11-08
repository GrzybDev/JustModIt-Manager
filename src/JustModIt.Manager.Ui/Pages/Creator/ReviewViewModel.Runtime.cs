using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

public sealed partial class ReviewViewModel
{
    private readonly Dictionary<string, RuntimeRow> _runtimeRows = new(StringComparer.Ordinal);

    /// <summary>The files the package's software writes while the game runs, as the plan declares them.</summary>
    public ObservableCollection<RuntimeRow> RuntimeRows { get; } = [];

    /// <summary>How many files the card holds, e.g. "2 files"; empty while it holds none.</summary>
    [ObservableProperty]
    public partial string RuntimeCount { get; private set; } = "";

    /// <summary>How many problems the card's files have, as its chip says it; empty while they have none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuntimeProblems))]
    public partial string RuntimeProblems { get; private set; } = "";

    public bool HasRuntimeProblems => RuntimeProblems.Length > 0;

    /// <summary>The package carries a program but the list is empty, so the card asks.</summary>
    [ObservableProperty]
    public partial bool ShowsRuntimePrompt { get; private set; }

    /// <summary>A warning of the whole list, e.g. that the package carries no program; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuntimeNotice))]
    public partial string RuntimeNotice { get; private set; } = "";

    public bool HasRuntimeNotice => RuntimeNotice.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuntimeRows))]
    public partial int RuntimeRowCount { get; private set; }

    public bool HasRuntimeRows => RuntimeRowCount > 0;

    /// <summary>The list has room for another file.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddRuntimeChangesCommand), nameof(StartRuntimePathCommand))]
    public partial bool CanAddRuntime { get; private set; } = true;

    /// <summary>The box for a path the software creates is open in place of the tiles.</summary>
    [ObservableProperty]
    public partial bool IsTypingRuntime { get; private set; }

    [ObservableProperty]
    public partial string RuntimePath { get; set; } = "";

    /// <summary>Why the path typed cannot be added; empty while it can, or while nothing is typed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuntimePathProblem))]
    public partial string RuntimePathProblem { get; private set; } = "";

    public bool HasRuntimePathProblem => RuntimePathProblem.Length > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRuntimePathCommand))]
    public partial bool CanConfirmRuntimePath { get; private set; }

    /// <summary>The label of the button that starts the store's check of the game's files.</summary>
    public string CheckFilesLabel => Text.Format(Strings.GamePage_CheckFiles, ("store", _page.GameStoreName));

    /// <summary>The store can start its check of the game's files from a link.</summary>
    public bool CanCheckFiles => _page.Game is { } game && Navigation.PageLinks.ValidateFiles(game.Key) is not null;

    /// <summary>The store of the version, whose icon the button of its check shows.</summary>
    public string CheckStore => _page.Game?.Key.Store.Value ?? "";

    /// <summary>How the store brings the original files back where it has no link to start that.</summary>
    public string StoreCheckWords => Text.Format(Strings.Creator_StoreCheckWords, ("storeId", CheckStore), ("store", _page.GameStoreName));

    [RelayCommand(CanExecute = nameof(CanAddRuntime))]
    private async Task AddRuntimeChangesAsync()
    {
        if (await PickGameFileAsync().ConfigureAwait(true) is { } path)
        {
            _session.ChangePart(part => part.WithRuntimeFile(new RuntimeFileChoice(path, RuntimeFileKind.ChangesGameFile)));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddRuntime))]
    private void StartRuntimePath()
    {
        (RuntimePath, RuntimePathProblem, IsTypingRuntime) = ("", "", true);
    }

    [RelayCommand]
    private void CancelRuntimePath() => (IsTypingRuntime, RuntimePath) = (false, "");

    [RelayCommand(CanExecute = nameof(CanConfirmRuntimePath))]
    private void ConfirmRuntimePath()
    {
        var path = _session.Comparison?.Game.Spell(TypedRuntimePath) ?? TypedRuntimePath;
        _session.ChangePart(part => part.WithRuntimeFile(new RuntimeFileChoice(path, RuntimeFileKind.CreatesFile)));
        (IsTypingRuntime, RuntimePath) = (false, "");
    }

    [RelayCommand]
    private void RemoveRuntime(RuntimeRow row)
    {
        if (row.Entry is { } entry)
        {
            _session.ChangePart(part => part.WithChoice(entry, entry.DefaultChoice));
            return;
        }

        var path = row.Path;
        _session.ChangePart(part => part.WithoutRuntimeFile(path));
    }

    [RelayCommand]
    private void UseRuntimeSuggestion(RuntimeRow row)
    {
        var (path, suggested) = (row.Path, row.Suggestion);
        _session.ChangePart(part => part.RuntimeFiles.FirstOrDefault(file => PackagePath.Comparer.Equals(file.Path, path)) is { } file
            ? part with { RuntimeFiles = new(part.RuntimeFiles.Items.Replace(file, file with { Path = suggested })) }
            : part);
    }

    [RelayCommand]
    private void CheckFiles()
    {
        _page.Services.Links.Open(_page.Game is { } game ? Navigation.PageLinks.ValidateFiles(game.Key) : null);
    }

    [RelayCommand]
    private void ShowRuntimeFile(RuntimeRow row)
    {
        if (_session.Comparison is { } comparison)
        {
            _page.Services.Links.OpenFolder(Path.GetDirectoryName(PackagePath.ToFullPath(comparison.GameDirectory, comparison.Game.Layout.ToGame(row.Path)))!);
        }
    }

    // An author on Windows may well type the folder's own separator, and the problem names the path as typed.
    private string TypedRuntimePath => RuntimePath.Trim().Replace('\\', PackagePath.Separator);

    // Checked as the author types, the way the plan checks the path once it is on the list.
    partial void OnRuntimePathChanged(string value)
    {
        var path = TypedRuntimePath;
        var listed = _session.Review?.Plan.RuntimeFiles.Select(file => file.Path) ?? [];
        var problem = path.Length == 0 ? null
            : CreatorPlanner.RuntimePathProblem(path, _page.Options)
                ?? (listed.Contains(path, PackagePath.Comparer) ? FindingKind.DuplicatePath : null);
        RuntimePathProblem = problem is { } kind ? CreatorWording.Finding(new PlanFinding(kind, value.Trim())) : "";
        CanConfirmRuntimePath = path.Length > 0 && problem is null;
    }

    private void ShowRuntime(CreatorReview review)
    {
        var plan = review.Plan;
        var entries = review.Entries.Where(entry => entry.Choice == FileChoice.Runtime)
            .ToDictionary(entry => entry.File.Path, PackagePath.Comparer);
        var findings = plan.Findings.Where(finding => finding.RuntimeIndex is not null && !IsHosting(finding.Kind))
            .ToLookup(finding => finding.RuntimeIndex!.Value);
        var keys = new RowKeys();
        var fresh = plan.RuntimeFiles.Select((file, index) => RuntimeRowOf(file, keys.Next("runtime:" + file.Path), entries, findings[index]))
            .ToList();
        var rows = ReviewRows.Reuse(_runtimeRows, fresh, CopyRuntime);
        Controls.CardGrid.Sync(RuntimeRows, rows);
        RuntimeRowCount = rows.Count;
        RuntimeCount = rows.Count > 0 ? Text.Format(Strings.Creator_RuntimeCount, ("count", rows.Count)) : "";
        var problems = rows.Count(row => row.HasProblem);
        RuntimeProblems = problems > 0 ? Text.Format(Strings.Creator_RuntimeProblems, ("count", problems)) : "";
        ShowsRuntimePrompt = plan.CarriesCode && rows.Count == 0;
        RuntimeNotice = plan.Findings.FirstOrDefault(finding => finding.Kind == FindingKind.RuntimeWithoutCode) is { } notice
            ? CreatorWording.Finding(notice)
            : "";
        CanAddRuntime = rows.Count < RuntimeFile.MaxPerPackage;
        OnPropertyChanged(nameof(CheckFilesLabel));
        OnPropertyChanged(nameof(CanCheckFiles));
        OnPropertyChanged(nameof(CheckStore));
        OnPropertyChanged(nameof(StoreCheckWords));

        static void CopyRuntime(RuntimeRow from, RuntimeRow to)
        {
            (to.Path, to.Entry, to.Folder, to.Name, to.State, to.StateIcon) = (from.Path, from.Entry, from.Folder, from.Name, from.State, from.StateIcon);
            (to.IsFromChoice, to.Reason, to.RemoveName, to.HasProblem, to.OffersFixes) =
                (from.IsFromChoice, from.Reason, from.RemoveName, from.HasProblem, from.OffersFixes);
            (to.Suggestion, to.SuggestionText) = (from.Suggestion, from.SuggestionText);
            if (!to.Findings.SequenceEqual(from.Findings))
            {
                to.Findings = from.Findings;
            }
        }
    }

    private static RuntimeRow RuntimeRowOf(PlannedRuntimeFile file, string key, Dictionary<string, ReviewEntry> entries,
        IEnumerable<PlanFinding> found)
    {
        List<PlanFinding> findings = [.. found];
        // A file declared as created that the game holds already has its problem said; its chip keeps to the declaration.
        var (state, icon) = file.WrittenByPackage ? ("Package", Icons.Package)
            : file.Kind == RuntimeFileKind.CreatesFile ? ("New", Icons.Plus)
            : file.GameFile is not null ? ("InGame", Icons.Copy)
            : ("", Icons.File);
        var missing = findings.FirstOrDefault(finding => finding.SuggestedPath is not null);
        var suggestion = file.FromChoice ? "" : missing?.SuggestedPath ?? "";
        return new RuntimeRow(key)
        {
            Path = file.Path,
            Entry = file.FromChoice ? entries.GetValueOrDefault(file.Path) : null,
            Folder = CreatorWording.FolderOf(file.Path),
            Name = CreatorWording.GameIndexName(file.Path),
            State = Text.Format(Strings.Creator_RuntimeState, ("state", state),
                ("size", file.GameFile is { } game ? Formats.FileSize(game.Stamp.Size) : "")),
            StateIcon = icon,
            IsFromChoice = file.FromChoice,
            Reason = file.FromChoice ? Strings.Creator_RuntimeWhy : "",
            RemoveName = Text.Format(Strings.Creator_RemoveRuntime, ("path", file.Path)),
            Findings = [.. findings.Select(Line)],
            HasProblem = findings.Any(finding => finding.BlocksBuild),
            OffersFixes = findings.Any(finding => finding.Kind is FindingKind.RuntimeFileInGame or FindingKind.RuntimeFileKept),
            Suggestion = suggestion,
            SuggestionText = suggestion.Length == 0 ? ""
                : Text.Format(Strings.Creator_FixPath, ("name", PackagePath.Comparer.Equals(CreatorWording.FolderOf(file.Path),
                    CreatorWording.FolderOf(suggestion)) ? CreatorWording.GameIndexName(suggestion) : suggestion)),
        };
    }
}
