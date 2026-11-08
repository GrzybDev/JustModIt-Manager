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
/// What the package does for players, in the order it does it, for the author to check. A simple part shows the operations
/// the comparison derived, each with its reason and the choices the rules allow; an advanced one the author's ordered steps,
/// edited in place. The site's hosting rules and the files the package's software changes in the game stand beside them.
/// </summary>
public sealed partial class ReviewViewModel : ObservableObject, IDisposable
{
    private readonly ProjectPageViewModel _page;
    private readonly CreatorSession _session;

    /// <summary>Creates the check of a project.</summary>
    /// <param name="page">The project's page.</param>
    /// <param name="session">The project.</param>
    public ReviewViewModel(ProjectPageViewModel page, CreatorSession session)
    {
        _page = page;
        _session = session;
        _session.PropertyChanged += OnSessionChanged;
    }

    [ObservableProperty]
    public partial bool IsAdvanced { get; private set; }

    /// <summary>The part view's heading: the operations of a simple part, or the steps of an advanced one.</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = "";

    [ObservableProperty]
    public partial string Lead { get; private set; } = "";

    /// <summary>That a step runs on every version unless some are turned off, said only while the project has several versions.</summary>
    [ObservableProperty]
    public partial string VersionsLead { get; private set; } = "";

    [ObservableProperty]
    public partial string Counts { get; private set; } = "";

    /// <summary>Problems of the whole package rather than of one operation, e.g. that it is too large.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblems))]
    public partial IReadOnlyList<FindingLine> Problems { get; private set; } = [];

    public bool HasProblems => Problems.Count > 0;

    public void Dispose() => _session.PropertyChanged -= OnSessionChanged;

    private async Task<string?> PickGameFileAsync()
    {
        if (_session.Comparison is not { } comparison)
        {
            return null;
        }

        var file = await _page.Services.Pickers.PickFileAsync(Strings.Creator_PickGameFileTitle, startIn: comparison.GameDirectory)
            .ConfigureAwait(true);
        return file is not null && PathComparison.IsWithin(comparison.GameDirectory, file)
            ? comparison.Game.Spell(comparison.Game.Layout.ToPackage(PackagePath.FromFullPath(comparison.GameDirectory, file)))
            : null;
    }

    // Every edit, also of the package's description, is planned again; a check out of sight catches up once it is shown, so a
    // large package's rows are not rebuilt for each key the author types in the description.
    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CreatorSession.Review) && _page.Tab != ProjectTab.Description)
        {
            Show();
        }
    }

    /// <summary>Shows the plan as it is now.</summary>
    internal void Show()
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
        Title = IsAdvanced ? Text.Format(Strings.Creator_StepsTitle, ("part", part.Name.Length > 0 ? part.Name : Strings.Creator_PartBase))
            : Strings.Creator_OperationsTitle;
        Lead = IsAdvanced
            ? Strings.Creator_StepsLead
            : Text.Format(Strings.Creator_ReviewLead, ("count", compared), ("game", game));
        VersionsLead = IsAdvanced && _session.Project.Versions.Count > 1 ? Strings.Creator_StepsEveryVersion : "";
        AddNote = Text.Format(Strings.Creator_AddNote, ("game", game));
        if (IsAdvanced)
        {
            ShowOperations([]);
            ShowSteps(plan, part);
        }
        else
        {
            _steps = ReviewRows.Reuse(_stepRows, [], null);
            SyncSteps();
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
