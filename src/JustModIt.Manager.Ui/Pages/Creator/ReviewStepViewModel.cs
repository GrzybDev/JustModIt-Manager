using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>
/// The third step: what the package does for players, in the order it does it, for the author to check. A simple
/// project shows the operations the comparison derived, each with its reason and the choices the rules allow; an
/// advanced one the author's ordered steps, edited in place. The site's hosting rules come first and the call to check
/// everything last; only a plan nothing blocks can be confirmed.
/// </summary>
public sealed partial class ReviewStepViewModel : ObservableObject, ICreatorStepView
{
    private const int FoldedGroupSize = 7;

    // Beyond a few numbers, the marks on the rows tell more than a list of them.
    private const int NamedPlaces = 5;

    private readonly CreatorPageViewModel _page;
    private readonly CreatorSession _session;
    private readonly Dictionary<string, bool> _expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReviewRow> _operationRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuleRow> _ruleRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StepRow> _stepRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeRow> _runtimeRows = new(StringComparer.Ordinal);
    private bool _runtimeFoldsWhenSolved;
    private bool _runtimeAsks;
    private List<IReviewRow> _operations = [];

    /// <summary>Creates the step for a project.</summary>
    /// <param name="page">The creator.</param>
    /// <param name="session">The project.</param>
    public ReviewStepViewModel(CreatorPageViewModel page, CreatorSession session)
    {
        _page = page;
        _session = session;
        _session.PropertyChanged += OnSessionChanged;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimple))]
    public partial bool IsAdvanced { get; private set; }

    public bool IsSimple => !IsAdvanced;

    [ObservableProperty]
    public partial string Lead { get; private set; } = "";

    public ObservableCollection<RuleRow> Rules { get; } = [];

    [ObservableProperty]
    public partial string RulesState { get; private set; } = "";

    [ObservableProperty]
    public partial RowTone RulesTone { get; private set; }

    [ObservableProperty]
    public partial bool HasRules { get; private set; }

    /// <summary>The rules found nothing that keeps the site from taking the package.</summary>
    [ObservableProperty]
    public partial bool RulesMet { get; private set; }

    /// <summary>The operations shown: every one but those of a folded group of new files.</summary>
    public ObservableCollection<IReviewRow> Operations { get; } = [];

    public ObservableCollection<StepRow> Steps { get; } = [];

    [ObservableProperty]
    public partial string Counts { get; private set; } = "";

    [ObservableProperty]
    public partial string StepsState { get; private set; } = "";

    [ObservableProperty]
    public partial bool StepsInOrder { get; private set; }

    /// <summary>Problems of the whole package rather than of one operation, e.g. that it is too large.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblems))]
    public partial IReadOnlyList<FindingLine> Problems { get; private set; } = [];

    public bool HasProblems => Problems.Count > 0;

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

    [ObservableProperty]
    public partial string CheckText { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> Checklist { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial bool CanConfirm { get; private set; }

    /// <summary>Why the plan cannot be confirmed yet; empty when it can.</summary>
    [ObservableProperty]
    public partial string ConfirmBlocked { get; private set; } = "";

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

    /// <summary>The card shows its files and tiles rather than only its title.</summary>
    [ObservableProperty]
    public partial bool IsRuntimeOpen { get; set; }

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

    public void OnShown()
    {
        Show();
        _ = _page.ReadKeptPathsAsync();
    }

    public Task<bool> CanLeaveAsync() => Task.FromResult(true);

    public void Dispose() => _session.PropertyChanged -= OnSessionChanged;

    [RelayCommand]
    private void Choose(ChoiceOption option)
    {
        if (Operations.OfType<OperationRow>().FirstOrDefault(row => row.Choices.Contains(option)) is { Entry: { } entry })
        {
            _session.Change(project => project.WithChoice(entry, option.Choice));
        }
    }

    [RelayCommand]
    private void Exclude(OperationRow row)
    {
        if (row.Entry is { } entry && entry.Choices.Contains(FileChoice.Skip))
        {
            _session.Change(project => project.WithChoice(entry, FileChoice.Skip));
        }
    }

    [RelayCommand]
    private void Restore(OperationRow row)
    {
        if (row.Entry is { } entry)
        {
            _session.Change(project => project.WithChoice(entry, entry.DefaultChoice));
        }
    }

    [RelayCommand]
    private void Remove(OperationRow row)
    {
        if (row.ExtraStep is { } index)
        {
            _session.Change(project => project.WithoutExtraStep(index));
        }
    }

    [RelayCommand]
    private void ToggleGroup(OperationGroup group)
    {
        group.IsExpanded = !group.IsExpanded;
        _expanded[group.Key] = group.IsExpanded;
        ReviewRows.Show(Operations, Visible(_operations));
    }

    [RelayCommand]
    private void ToggleIdentical() => ShowsIdentical = !ShowsIdentical;

    // A store library renamed away and replaced is found in both operations, and leaving out one leaves the other against
    // the rules.
    [RelayCommand]
    private void ExcludeRule(RuleRow rule)
    {
        if (IsAdvanced)
        {
            var steps = rule.Findings.Select(finding => finding.StepIndex).OfType<int>().Distinct().OrderDescending().ToList();
            _session.Change(project => project with
            {
                Steps = new(steps.Aggregate(project.Steps.Items, (remaining, step) => remaining.RemoveAt(step))),
            });
            return;
        }

        var entries = rule.Findings.Select(finding => RowOf(finding)?.Entry).OfType<ReviewEntry>()
            .Where(entry => entry.Choices.Contains(FileChoice.Skip)).ToList();
        _session.Change(project => entries.Aggregate(project, (changed, entry) => changed.WithChoice(entry, FileChoice.Skip)));
    }

    [RelayCommand]
    private void Accept(string key) => _session.Describe(project => project.Acknowledge(key));

    [RelayCommand]
    private void AcceptRule(RuleRow rule)
    {
        var keys = rule.Findings.Select(finding => finding.AcknowledgementKey).OfType<string>().ToList();
        _session.Describe(project => keys.Aggregate(project, (changed, key) => changed.Acknowledge(key)));
    }

    // Whose code a library is changes only the answer: a name, licence and source typed for it stay.
    [RelayCommand]
    private void MarkOwnCode(RuleRow rule) => _session.Describe(project => project.LibraryNoticeOf(rule.Path) switch
    {
        null or { IsOwn: true } => project,
        { License.Length: 0, SourceUrl.Length: 0 } notice when notice.Name.Length == 0 || notice.Name == rule.FileName
            => project.WithoutLibraryNotice(rule.Path),
        var notice => project.WithLibraryNotice(notice with { IsOwn = true }),
    });

    [RelayCommand]
    private void MarkThirdParty(RuleRow rule) => _session.Describe(project => project.LibraryNoticeOf(rule.Path) switch
    {
        { IsOwn: false } => project,
        null => project.WithLibraryNotice(new LibraryNotice(rule.Path, "", "", rule.FileName)),
        var notice => project.WithLibraryNotice(notice with { IsOwn = false }),
    });

    [RelayCommand]
    private void AddLibraryNotice(RuleRow rule) =>
        _session.Describe(project => project.WithLibraryNotice(new LibraryNotice(rule.Path, "", "", rule.FileName, IsOwn: true)));

    [RelayCommand]
    private void RemoveLibraryNotice(RuleRow rule) => _session.Describe(project => project.WithoutLibraryNotice(rule.Path));

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
            _session.Change(project => project.WithExtraStep(new DeleteFileStep(path)));
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
            _session.Change(project => project.WithExtraStep(new RenameFileStep(from, to)));
        }

        (RenameFrom, IsAdding) = ("", false);
    }

    [RelayCommand]
    private async Task ToAdvancedAsync()
    {
        if (_session.Review is not { } review || !await _page.Services.Dialogs.ConfirmAsync(new ConfirmDialogViewModel(
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
        if (await _page.Services.Dialogs.ConfirmAsync(new ConfirmDialogViewModel(Strings.Creator_ToSimpleTitle, Strings.Creator_ToSimple,
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

    [RelayCommand]
    private void ToggleRuntime()
    {
        IsRuntimeOpen = !IsRuntimeOpen;
        _runtimeFoldsWhenSolved = false;
    }

    [RelayCommand(CanExecute = nameof(CanAddRuntime))]
    private async Task AddRuntimeChangesAsync()
    {
        _runtimeFoldsWhenSolved = false;
        if (await PickGameFileAsync().ConfigureAwait(true) is { } path)
        {
            _session.Change(project => project.WithRuntimeFile(new RuntimeFileChoice(path, RuntimeFileKind.ChangesGameFile)));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddRuntime))]
    private void StartRuntimePath()
    {
        (RuntimePath, RuntimePathProblem, IsTypingRuntime) = ("", "", true);
        _runtimeFoldsWhenSolved = false;
    }

    [RelayCommand]
    private void CancelRuntimePath() => (IsTypingRuntime, RuntimePath) = (false, "");

    [RelayCommand(CanExecute = nameof(CanConfirmRuntimePath))]
    private void ConfirmRuntimePath()
    {
        var path = _session.Comparison?.Game.Spell(TypedRuntimePath) ?? TypedRuntimePath;
        _session.Change(project => project.WithRuntimeFile(new RuntimeFileChoice(path, RuntimeFileKind.CreatesFile)));
        (IsTypingRuntime, RuntimePath) = (false, "");
    }

    [RelayCommand]
    private void RemoveRuntime(RuntimeRow row)
    {
        _runtimeFoldsWhenSolved = false;
        if (row.Entry is { } entry)
        {
            _session.Change(project => project.WithChoice(entry, entry.DefaultChoice));
            return;
        }

        var path = row.Path;
        _session.Change(project => project.WithoutRuntimeFile(path));
    }

    [RelayCommand]
    private void UseRuntimeSuggestion(RuntimeRow row)
    {
        _runtimeFoldsWhenSolved = false;
        var (path, suggested) = (row.Path, row.Suggestion);
        _session.Change(project => project.RuntimeFiles.FirstOrDefault(file => PackagePath.Comparer.Equals(file.Path, path)) is { } file
            ? project with { RuntimeFiles = new(project.RuntimeFiles.Items.Replace(file, file with { Path = suggested })) }
            : project);
    }

    [RelayCommand]
    private void CheckFiles()
    {
        _runtimeFoldsWhenSolved = true;
        if (_page.Game is { } game && Navigation.PageLinks.ValidateFiles(game.Key) is { } check)
        {
            _page.Services.Links.Open(check);
        }
    }

    [RelayCommand]
    private void ShowRuntimeFile(RuntimeRow row)
    {
        _runtimeFoldsWhenSolved = true;
        if (_session.Comparison is { } comparison)
        {
            _page.Services.Links.OpenFolder(Path.GetDirectoryName(PackagePath.ToFullPath(comparison.GameDirectory, row.Path))!);
        }
    }

    [RelayCommand]
    private async Task CompareAgainAsync()
    {
        _runtimeFoldsWhenSolved = true;
        _page.GoTo(CreatorStep.Files);
        if (_page.Content is FilesStepViewModel files)
        {
            await files.CompareAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        _session.IsConfirmed = true;
        _page.GoTo(CreatorStep.Package);
    }

    [RelayCommand]
    private void Back() => _page.GoTo(CreatorStep.Files);

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

    private async Task<string?> PickGameFileAsync()
    {
        if (_session.Comparison is not { } comparison)
        {
            return null;
        }

        var file = await _page.Services.Pickers.PickFileAsync(Strings.Creator_PickGameFileTitle, startIn: comparison.GameDirectory)
            .ConfigureAwait(true);
        return file is not null && PathComparison.IsWithin(comparison.GameDirectory, file)
            ? comparison.Game.Spell(PackagePath.FromFullPath(comparison.GameDirectory, file))
            : null;
    }

    // Every edit, also of the package's metadata on the last step, is planned again; a check out of sight catches up once
    // it is shown, so a large package's rows are not rebuilt for each key the author types elsewhere.
    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CreatorSession.Review) && ReferenceEquals(_page.Content, this))
        {
            Show();
        }
    }

    private void Show()
    {
        if (_session.Review is not { } review || _session.Comparison is not { } comparison)
        {
            return;
        }

        var plan = review.Plan;
        var project = _session.Project;
        var findings = plan.Findings.Where(finding => finding.OperationIndex is not null && !IsHosting(finding.Kind))
            .ToLookup(finding => finding.OperationIndex!.Value);
        IsAdvanced = project.Mode == CreatorMode.Advanced;
        var compared = comparison.Files.Count(file => file.AuthorPath is not null);
        Lead = IsAdvanced
            ? Strings.Creator_StepsLead
            : Text.Format(Strings.Creator_ReviewLead, ("count", compared), ("game", project.Game?.Name ?? ""));
        AddNote = Text.Format(Strings.Creator_AddNote, ("game", project.Game?.Name ?? ""));
        if (IsAdvanced)
        {
            ShowOperations([]);
            ShowSteps(plan, project);
        }
        else
        {
            ReviewRows.Show(Steps, ReviewRows.Reuse(_stepRows, [], null));
            ShowOperations(OperationRows(review, findings));
        }

        ShowRules(plan);
        ShowRuntime(review);
        var excluded = review.Entries.Count(entry => entry.IsChangedByAuthor && entry.Choice == FileChoice.Skip);
        Counts = CreatorWording.Counts(plan.Operations, IsAdvanced, excluded);
        Problems = [.. plan.Findings.Where(finding => finding.OperationIndex is null && finding.RuntimeIndex is null && !IsHosting(finding.Kind)
            && !IsMetadata(finding.Kind) && finding.Kind != FindingKind.RuntimeWithoutCode).Select(Line)];
        List<string> identical = [.. review.Entries.Where(entry => entry.File.Difference == FileDifference.Identical).Select(entry => entry.File.Path)];
        if (!identical.SequenceEqual(IdenticalFiles, StringComparer.Ordinal))
        {
            IdenticalFiles = identical;
        }

        IdenticalCount = Text.Format(Strings.Creator_IdenticalLeftOut, ("count", IdenticalFiles.Count));
        ShowCheck(plan);
    }

    // The card opens by itself once it asks for files or a file has a problem, unless the author folded it meanwhile. It folds
    // again once a problem is solved outside it, by the store, in the folder or by comparing again, but stays open while the
    // author edits the list in it.
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
        ReviewRows.Show(RuntimeRows, rows);
        RuntimeRowCount = rows.Count;
        RuntimeCount = rows.Count > 0 ? Text.Format(Strings.Creator_RuntimeCount, ("count", rows.Count)) : "";
        var problems = rows.Count(row => row.HasProblem);
        RuntimeProblems = problems > 0 ? Text.Format(Strings.Creator_RuntimeProblems, ("count", problems)) : "";
        ShowsRuntimePrompt = plan.CarriesCode && rows.Count == 0;
        RuntimeNotice = plan.Findings.FirstOrDefault(finding => finding.Kind == FindingKind.RuntimeWithoutCode) is { } notice
            ? CreatorWording.Finding(notice)
            : "";
        CanAddRuntime = rows.Count < RuntimeFile.MaxPerPackage;
        var asks = problems > 0 || ShowsRuntimePrompt;
        if (asks && !_runtimeAsks && !IsRuntimeOpen)
        {
            (IsRuntimeOpen, _runtimeFoldsWhenSolved) = (true, problems > 0);
        }
        else if (!asks && _runtimeFoldsWhenSolved && !IsTypingRuntime)
        {
            (IsRuntimeOpen, _runtimeFoldsWhenSolved) = (false, false);
        }

        _runtimeAsks = asks;

        OnPropertyChanged(nameof(CheckFilesLabel));
        OnPropertyChanged(nameof(CanCheckFiles));

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

    // The author decides about a file, not about each operation the rules find it in, e.g. a store library renamed away
    // and replaced.
    private void ShowRules(PackagePlan plan)
    {
        var fresh = plan.Findings.Where(finding => IsHosting(finding.Kind))
            .GroupBy(finding => $"{finding.Kind}:{finding.ToolId ?? finding.Path}", StringComparer.Ordinal)
            .Select(found => RuleOf(found.Key, [.. found]))
            .ToList();
        var rows = ReviewRows.Reuse(_ruleRows, fresh, Update);
        ReviewRows.Show(Rules, rows);
        foreach (var row in rows)
        {
            ShowNotice(row);
        }

        var errors = rows.Count(row => row.IsError);
        var questions = rows.Count(row => !row.IsError && row.Findings.Any(finding => finding.BlocksBuild));
        HasRules = rows.Count > 0;
        RulesMet = errors == 0 && questions == 0;
        RulesTone = errors > 0 ? RowTone.Danger : questions > 0 ? RowTone.Warning : RowTone.Success;
        RulesState = RulesMet
            ? Strings.Creator_RulesMet
            : Library.GameWording.Join(
            [
                errors > 0 ? Text.Format(Strings.Creator_RulesErrors, ("count", errors)) : "",
                questions > 0 ? Text.Format(Strings.Creator_RulesQuestions, ("count", questions)) : "",
            ]);

        static void Update(RuleRow fresh, RuleRow kept)
        {
            (kept.Findings, kept.Kind, kept.Title, kept.Text, kept.Tone, kept.Icon) =
                (fresh.Findings, fresh.Kind, fresh.Title, fresh.Text, fresh.Tone, fresh.Icon);
            (kept.CanExclude, kept.IsAccepted, kept.IsThirdParty, kept.HasNotice, kept.ToolId, kept.Path) =
                (fresh.CanExclude, fresh.IsAccepted, fresh.IsThirdParty, fresh.HasNotice, fresh.ToolId, fresh.Path);
        }
    }

    private RuleRow RuleOf(string key, IReadOnlyList<PlanFinding> findings)
    {
        var finding = findings[0];
        var tool = finding.ToolId is { } id ? _page.Options.Rules.ThirdPartyTools.FirstOrDefault(rule => rule.Id == id) : null;
        var blocks = findings.Any(found => found.BlocksBuild);
        var kind = finding.Kind switch
        {
            FindingKind.ThirdPartyTool => RuleKind.Tool,
            FindingKind.AddedLibrary => RuleKind.Library,
            _ when finding.Severity == FindingSeverity.Error => RuleKind.Error,
            _ => RuleKind.Accept,
        };
        var notice = kind == RuleKind.Library ? _session.Project.LibraryNoticeOf(finding.Path!) : null;
        return new RuleRow(key)
        {
            Findings = findings,
            Kind = kind,
            Title = CreatorWording.RuleTitle(finding, tool?.Name ?? ""),
            Text = CreatorWording.RuleText(finding),
            Tone = kind == RuleKind.Error ? RowTone.Danger
                : blocks ? RowTone.Warning
                : kind == RuleKind.Library && notice is null ? RowTone.Neutral
                : RowTone.Success,
            Icon = kind == RuleKind.Error ? Icons.CircleX
                : blocks || kind == RuleKind.Library ? Icons.FileCode
                : Icons.CircleCheck,
            CanExclude = kind == RuleKind.Error && findings.All(CanExclude),
            IsAccepted = !blocks,
            IsThirdParty = notice is { IsOwn: false },
            HasNotice = notice is not null,
            ToolId = finding.ToolId ?? "",
            Path = finding.Path ?? "",
        };
    }

    // Filled with Edited unset, so showing the project's licence and source does not write them back as an edit.
    private void ShowNotice(RuleRow row)
    {
        row.Edited = null;
        var (name, license, source, complete) = row.IsTool
            ? _session.Project.ToolNotices.FirstOrDefault(notice => notice.ToolId == row.ToolId) is { } tool
                ? ("", tool.License, tool.SourceUrl, tool.IsComplete)
                : ("", "", "", false)
            : _session.Project.LibraryNoticeOf(row.Path) is { } library
                ? (library.Name, library.License, library.SourceUrl, library.IsComplete)
                : ("", "", "", false);
        (row.LibraryName, row.License, row.Source, row.IsComplete) = (name, license, source, complete);
        row.Edited = row.IsTool
            ? edited => _session.Describe(project => project.WithToolNotice(new ToolNotice(edited.ToolId, edited.License, edited.Source)))
            : edited => _session.Describe(project => project.WithLibraryNotice(
                new LibraryNotice(edited.Path, edited.License, edited.Source, edited.LibraryName, IsOwn: !edited.IsThirdParty)));
    }

    private bool CanExclude(PlanFinding finding) =>
        IsAdvanced ? finding.StepIndex is not null : RowOf(finding)?.Entry?.Choices.Contains(FileChoice.Skip) == true;

    private OperationRow? RowOf(PlanFinding finding) =>
        finding.OperationIndex is { } index && _session.Review is { } review && index < review.Plan.Operations.Count
            ? _operationRows.GetValueOrDefault(KeyOf(review.Plan.Operations[index])) as OperationRow
            : null;

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

        ReviewRows.Show(Operations, Visible(_operations));
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

    private static void Fill(OperationRow row, PlannedOperation operation, int index, ReviewEntry? entry,
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

        ReviewRows.Show(Steps, rows);
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

    private void ShowCheck(PackagePlan plan)
    {
        var errors = Rules.Count(row => row.IsError);
        var questions = Rules.Count(row => !row.IsError && row.Findings.Any(finding => finding.BlocksBuild));
        var runtime = plan.Findings.Count(finding => finding.BlocksBuild && finding.RuntimeIndex is not null && !IsHosting(finding.Kind));
        var found = plan.Findings.Where(finding => finding.BlocksBuild && finding.RuntimeIndex is null && !IsHosting(finding.Kind)
            && !IsMetadata(finding.Kind)).ToList();
        // The planner takes such a rename, but it would set the author's file aside instead of the game's original.
        var atPlaces = found.Select(finding => IsAdvanced ? finding.StepIndex : finding.OperationIndex).OfType<int>()
            .Concat(Steps.Where(row => row.MoveBefore is not null).Select(row => row.Index)).ToList();
        var marked = found.Count(finding => (IsAdvanced ? finding.StepIndex : finding.OperationIndex) is null);
        List<string> places = [.. atPlaces.Distinct().Order().Select(index => (index + 1).ToString(Text.Culture))];
        CanConfirm = errors + questions + atPlaces.Count + marked + runtime == 0 && plan.Operations.Count > 0;
        List<string> clauses = [];
        if (errors + questions > 0)
        {
            clauses.Add(Text.Format(Strings.Creator_BlockedRules, ("case", errors == 0 ? "questions" : questions == 0 ? "errors" : "both"),
                ("errors", errors), ("questions", questions)));
        }

        if (places.Count is > 0 and <= NamedPlaces)
        {
            clauses.Add(Text.Format(Strings.Creator_BlockedAt, ("count", atPlaces.Count), ("advanced", IsAdvanced ? "true" : "false"),
                ("places", places.Count), ("numbers", CreatorWording.List(places))));
        }
        else
        {
            marked += atPlaces.Count;
        }

        if (runtime > 0)
        {
            clauses.Add(Text.Format(Strings.Creator_BlockedRuntime, ("count", runtime)));
        }

        if (marked > 0 || (!CanConfirm && clauses.Count == 0))
        {
            clauses.Add(Text.Format(Strings.Creator_BlockedMarked, ("count", Math.Max(marked, 1))));
        }

        ConfirmBlocked = CanConfirm ? "" : Text.Format(Strings.Creator_ConfirmBlocked, ("what", CreatorWording.List(clauses)));
        CheckText = Text.Format(IsAdvanced ? Strings.Creator_CheckSteps : Strings.Creator_CheckOperations,
            ("count", plan.Operations.Count));
        List<string> checklist = IsAdvanced
            ? [Strings.Creator_CheckStepFiles, Strings.Creator_CheckRenameOrder]
            : plan.Operations.Any(operation => operation.Kind == OperationKind.Rename)
                ? [Strings.Creator_CheckPlaces, Strings.Creator_CheckRename]
                : [Strings.Creator_CheckPlaces];
        if (plan.CarriesCode)
        {
            checklist.Add(Strings.Creator_CheckRuntime);
        }

        Checklist = [.. checklist, Strings.Creator_CheckNothingExtra];
    }

    private static FindingLine Line(PlanFinding finding) =>
        new(CreatorWording.Finding(finding), finding.Severity == FindingSeverity.Error ? RowTone.Danger : RowTone.Warning,
            finding.IsAcknowledged ? null : finding.AcknowledgementKey);

    // A simple project has one operation per path, so a file keeps its row, and its open editor, when its kind changes.
    private static string KeyOf(PlannedOperation operation) =>
        operation.Reason == OperationReason.AddedStep
            ? $"added:{operation.StepIndex}"
            : $"file:{operation.Path}";

    /// <summary>Whether a finding is the site's hosting rule rather than a problem of the plan itself.</summary>
    /// <param name="kind">The finding's kind.</param>
    internal static bool IsHosting(FindingKind kind) => kind is FindingKind.StoreLibrary or FindingKind.CircumventionFile
        or FindingKind.CopyOfGameFile or FindingKind.WholeGameExecutable or FindingKind.WholeGameFile or FindingKind.WholeGameArchive
        or FindingKind.ThirdPartyTool or FindingKind.AddedLibrary or FindingKind.LargePatch or FindingKind.RuntimeCode
        or FindingKind.RuntimeSettings or FindingKind.RuntimeSave;

    /// <summary>Whether a finding is about the package's metadata, which its last step asks for.</summary>
    /// <param name="kind">The finding's kind.</param>
    internal static bool IsMetadata(FindingKind kind) => kind is FindingKind.MissingTitle or FindingKind.MissingVersion;
}
