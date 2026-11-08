using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

public sealed partial class ReviewViewModel
{
    private readonly Dictionary<string, RuleRow> _ruleRows = new(StringComparer.Ordinal);

    /// <summary>
    /// The rules that wait for the author: those to fix or to answer, and every added library, which counts as the authors'
    /// code until the author says otherwise.
    /// </summary>
    public ObservableCollection<RuleRow> OpenRules { get; } = [];

    /// <summary>The other rules, which the author accepted or answered.</summary>
    public ObservableCollection<RuleRow> AcceptedRules { get; } = [];

    [ObservableProperty]
    public partial string RulesState { get; private set; } = "";

    [ObservableProperty]
    public partial Tone RulesTone { get; private set; }

    /// <summary>The rules found nothing that keeps the site from taking the package.</summary>
    [ObservableProperty]
    public partial bool RulesMet { get; private set; }

    // A store library renamed away and replaced is found in both operations, and leaving out one leaves the other against
    // the rules.
    [RelayCommand]
    private void ExcludeRule(RuleRow rule)
    {
        if (IsAdvanced)
        {
            var steps = rule.Findings.Select(finding => finding.StepIndex).OfType<int>().Distinct().OrderDescending().ToList();
            _session.ChangePart(part => part with
            {
                Steps = new(steps.Aggregate(part.Steps.Items, (remaining, step) => remaining.RemoveAt(step))),
            });
            return;
        }

        var entries = rule.Findings.Select(finding => RowOf(finding)?.Entry).OfType<ReviewEntry>()
            .Where(entry => entry.Choices.Contains(FileChoice.Skip)).ToList();
        _session.ChangePart(part => entries.Aggregate(part, (changed, entry) => changed.WithChoice(entry, FileChoice.Skip)));
    }

    [RelayCommand]
    private void Accept(string key) => _session.Change(project => project.Acknowledge(key));

    [RelayCommand]
    private void UndoAccept(string key) => _session.Change(project => project.Unacknowledge(key));

    [RelayCommand]
    private void AcceptRule(RuleRow rule) =>
        _session.Change(project => KeysOf(rule).Aggregate(project, (changed, key) => changed.Acknowledge(key)));

    [RelayCommand]
    private void UndoRule(RuleRow rule) =>
        _session.Change(project => KeysOf(rule).Aggregate(project, (changed, key) => changed.Unacknowledge(key)));

    private static List<string> KeysOf(RuleRow rule) => [.. rule.Findings.Select(finding => finding.AcknowledgementKey).OfType<string>()];

    // Whose code a library is changes only the answer: a name, licence and source typed for it stay.
    [RelayCommand]
    private void MarkOwnCode(RuleRow rule) => _session.Change(project => project.LibraryNoticeOf(rule.Path) switch
    {
        null or { IsOwn: true } => project,
        { License.Length: 0, SourceUrl.Length: 0 } notice when notice.Name.Length == 0 || notice.Name == rule.FileName
            => project.WithoutLibraryNotice(rule.Path),
        var notice => project.WithLibraryNotice(notice with { IsOwn = true }),
    });

    [RelayCommand]
    private void MarkThirdParty(RuleRow rule) => _session.Change(project => project.LibraryNoticeOf(rule.Path) switch
    {
        { IsOwn: false } => project,
        null => project.WithLibraryNotice(new LibraryNotice(rule.Path, "", "", rule.FileName)),
        var notice => project.WithLibraryNotice(notice with { IsOwn = false }),
    });

    [RelayCommand]
    private void AddLibraryNotice(RuleRow rule) =>
        _session.Change(project => project.WithLibraryNotice(new LibraryNotice(rule.Path, "", "", rule.FileName, IsOwn: true)));

    [RelayCommand]
    private void RemoveLibraryNotice(RuleRow rule) => _session.Change(project => project.WithoutLibraryNotice(rule.Path));

    /// <summary>
    /// How a plan's rules stand, as their rows count them: errors to fix, questions to answer, the rows that wait for the author
    /// (those and every added library) and the others, accepted or answered.
    /// </summary>
    /// <param name="plan">The plan.</param>
    internal static (int Errors, int Questions, int Open, int Accepted) Tally(PackagePlan plan)
    {
        int errors = 0, questions = 0, open = 0, all = 0;
        foreach (var group in RuleGroups(plan))
        {
            var first = group.First();
            var isError = first.Severity == FindingSeverity.Error && first.Kind is not (FindingKind.ThirdPartyTool or FindingKind.AddedLibrary);
            var blocks = group.Any(finding => finding.BlocksBuild);
            (errors, questions, all) = (errors + (isError ? 1 : 0), questions + (!isError && blocks ? 1 : 0), all + 1);
            open += blocks || first.Kind == FindingKind.AddedLibrary ? 1 : 0;
        }

        return (errors, questions, open, all - open);
    }

    // The author decides about a file, not about each operation the rules find it in, e.g. a store library renamed away
    // and replaced. Only a build finds a large patch, so one the author accepted is known by its key alone, which stands among
    // the rules, with the way to take it back, while the plan still patches its path.
    private static IEnumerable<IGrouping<string, PlanFinding>> RuleGroups(PackagePlan plan)
    {
        var patched = plan.Operations.Where(operation => operation.Kind == OperationKind.Patch).Select(operation => operation.Path)
            .ToHashSet(PackagePath.Comparer);
        return plan.Findings.Where(finding => IsHosting(finding.Kind))
            .Concat(plan.Acknowledged.Select(key => (Key: key, Path: AcknowledgementKeys.LargePatchPath(key)))
                .Where(accepted => accepted.Path is not null && patched.Contains(accepted.Path))
                .Select(accepted => new PlanFinding(FindingKind.LargePatch, accepted.Path) { AcknowledgementKey = accepted.Key, IsAcknowledged = true }))
            .GroupBy(finding => $"{finding.Kind}:{finding.ToolId ?? finding.Path}", StringComparer.Ordinal);
    }

    private void ShowRules(PackagePlan plan)
    {
        List<RuleRow> rows = [.. RuleGroups(plan).Select(found => RuleOf(found.Key, [.. found]))];
        ReviewRows.Remember(_ruleRows, rows);
        Controls.CardGrid.Sync(OpenRules, [.. rows.Where(row => !row.IsAccepted || row.IsLibrary)]);
        Controls.CardGrid.Sync(AcceptedRules, [.. rows.Where(row => row.IsAccepted && !row.IsLibrary)]);
        foreach (var row in rows)
        {
            ShowNotice(row);
        }

        var (errors, questions, _, _) = Tally(plan);
        RulesMet = errors == 0 && questions == 0;
        RulesTone = errors > 0 ? Tone.Danger : questions > 0 ? Tone.Warning : Tone.Success;
        RulesState = RulesMet
            ? Strings.Creator_RulesMet
            : Library.GameWording.Join(
            [
                errors > 0 ? Text.Format(Strings.Creator_RulesErrors, ("count", errors)) : "",
                questions > 0 ? Text.Format(Strings.Creator_RulesQuestions, ("count", questions)) : "",
            ]);
    }

    private RuleRow RuleOf(string key, IReadOnlyList<PlanFinding> findings)
    {
        var finding = findings[0];
        var tool = finding.ToolId is { } id ? _page.Creator.Options.Rules.ThirdPartyTools.FirstOrDefault(rule => rule.Id == id) : null;
        var blocks = findings.Any(found => found.BlocksBuild);
        var kind = finding.Kind switch
        {
            FindingKind.ThirdPartyTool => RuleKind.Tool,
            FindingKind.AddedLibrary => RuleKind.Library,
            _ when finding.Severity == FindingSeverity.Error => RuleKind.Error,
            _ => RuleKind.Accept,
        };
        var notice = kind == RuleKind.Library ? _session.Project.LibraryNoticeOf(finding.Path!) : null;
        var row = _ruleRows.GetValueOrDefault(key) ?? new RuleRow(key);
        row.Findings = findings;
        row.Kind = kind;
        row.Title = CreatorWording.RuleTitle(finding, tool?.Name ?? "");
        row.Text = Text.Format(Strings.Creator_RuleText, ("kind", finding.Kind.ToString()));
        row.Tone = kind == RuleKind.Error ? Tone.Danger
            : blocks ? Tone.Warning
            : kind == RuleKind.Library && notice is null ? Tone.Neutral
            : Tone.Success;
        row.Icon = kind == RuleKind.Error ? Icons.CircleX
            : blocks || kind == RuleKind.Library ? Icons.FileCode
            : Icons.CircleCheck;
        row.CanExclude = kind == RuleKind.Error && findings.All(CanExclude);
        row.IsAccepted = !blocks;
        row.IsThirdParty = notice is { IsOwn: false };
        row.HasNotice = notice is not null;
        row.ToolId = finding.ToolId ?? "";
        row.Path = finding.Path ?? "";
        return row;
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
        row.KnownNotice = row.NeedsNotice && !complete ? _page.Creator.Library.KnownNotice(_session.Project.Id, row.IsTool ? row.ToolId : "", row.IsTool ? "" : row.Path) : null;
        row.UseNoticeText = row.KnownNotice is { } known ? Text.Format(Strings.Creator_UseNotice, ("project", ProjectCard.NameOf(known.Project))) : "";
        row.Edited = row.IsTool
            ? edited => _session.Change(project => project.WithToolNotice(new ToolNotice(edited.ToolId, edited.License, edited.Source)))
            : edited => _session.Change(project => project.WithLibraryNotice(
                new LibraryNotice(edited.Path, edited.License, edited.Source, edited.LibraryName, IsOwn: !edited.IsThirdParty)));
    }

    private bool CanExclude(PlanFinding finding) =>
        IsAdvanced ? finding.StepIndex is not null : RowOf(finding)?.Entry?.Choices.Contains(FileChoice.Skip) == true;

    private OperationRow? RowOf(PlanFinding finding) =>
        finding.OperationIndex is { } index && _session.Review is { } review && index < review.Plan.Operations.Count
            ? _operationRows.GetValueOrDefault(KeyOf(review.Plan.Operations[index])) as OperationRow
            : null;
}
