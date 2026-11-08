using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>
/// The third step: what the package does for players, in the order it does it, for the author to check. A simple
/// project shows the operations the comparison derived, each with its reason and the choices the rules allow; an
/// advanced one the author's ordered steps, edited in place. The site's hosting rules come first and the call to check
/// everything last; only a plan nothing blocks can be confirmed.
/// </summary>
public sealed partial class ReviewViewModel : ObservableObject, ICreatorStepView
{
    // Beyond a few numbers, the marks on the rows tell more than a list of them.
    private const int NamedPlaces = 5;

    private readonly CreatorPageViewModel _page;
    private readonly CreatorSession _session;

    /// <summary>Creates the step for a project.</summary>
    /// <param name="page">The creator.</param>
    /// <param name="session">The project.</param>
    public ReviewViewModel(CreatorPageViewModel page, CreatorSession session)
    {
        _page = page;
        _session = session;
        _session.PropertyChanged += OnSessionChanged;
    }

    [ObservableProperty]
    public partial bool IsAdvanced { get; private set; }

    [ObservableProperty]
    public partial string Lead { get; private set; } = "";

    [ObservableProperty]
    public partial string Counts { get; private set; } = "";

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

    public void OnShown()
    {
        Show();
        _ = _page.ReadKeptPathsAsync();
    }

    public void Dispose() => _session.PropertyChanged -= OnSessionChanged;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        _session.IsConfirmed = true;
        _page.GoTo(CreatorStep.Package);
    }

    [RelayCommand]
    private void Back() => _page.GoTo(CreatorStep.Files);

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
        var part = _session.Part;
        var game = _session.Version?.Game.Name ?? "";
        var findings = plan.Findings.Where(finding => finding.OperationIndex is not null && !IsHosting(finding.Kind))
            .ToLookup(finding => finding.OperationIndex!.Value);
        IsAdvanced = part.Mode == CreatorMode.Advanced;
        var compared = comparison.Files.Count(file => file.AuthorPath is not null);
        Lead = IsAdvanced
            ? Strings.Creator_StepsLead
            : Text.Format(Strings.Creator_ReviewLead, ("count", compared), ("game", game));
        AddNote = Text.Format(Strings.Creator_AddNote, ("game", game));
        if (IsAdvanced)
        {
            ShowOperations([]);
            ShowSteps(plan, part);
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

    private void ShowCheck(PackagePlan plan)
    {
        var errors = Rules.Count(row => row.IsError);
        var questions = Rules.Count(row => !row.IsError && row.Findings.Any(finding => finding.BlocksBuild));
        var runtime = plan.Findings.Count(finding => finding.BlocksBuild && finding.RuntimeIndex is not null && !IsHosting(finding.Kind));
        var found = plan.Findings.Where(finding => finding.BlocksBuild && finding.RuntimeIndex is null && !IsHosting(finding.Kind)
            && !IsMetadata(finding.Kind)).ToList();
        var atPlaces = found.Select(finding => IsAdvanced ? finding.StepIndex : finding.OperationIndex).OfType<int>().ToList();
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
