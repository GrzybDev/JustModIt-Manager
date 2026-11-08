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
    // Beyond a few numbers, the marks on the rows tell more than a list of them.
    private const int NamedPlaces = 5;

    private readonly CreatorPageViewModel _page;
    private readonly CreatorSession _session;
    private readonly Dictionary<string, StepRow> _stepRows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RuntimeRow> _runtimeRows = new(StringComparer.Ordinal);
    private bool _runtimeFoldsWhenSolved;
    private bool _runtimeAsks;

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
    public partial bool IsAdvanced { get; private set; }

    [ObservableProperty]
    public partial string Lead { get; private set; } = "";

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

    public void Dispose() => _session.PropertyChanged -= OnSessionChanged;

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
        _page.Services.Links.Open(_page.Game is { } game ? Navigation.PageLinks.ValidateFiles(game.Key) : null);
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
    private Task CompareAgainAsync()
    {
        _runtimeFoldsWhenSolved = true;
        return _page.CompareAgainAsync();
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
            Controls.CardGrid.Sync(Steps, ReviewRows.Reuse(_stepRows, [], null));
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
