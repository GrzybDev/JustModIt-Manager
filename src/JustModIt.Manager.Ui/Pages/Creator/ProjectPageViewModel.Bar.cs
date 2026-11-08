using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>A line of the bar's list of what a build needs.</summary>
/// <param name="Text">What it is about.</param>
/// <param name="IsMet">The build has it.</param>
/// <param name="IsReminder">Only a question for the author, which holds nothing back.</param>
public sealed record BeforeItem(string Text, bool IsMet, bool IsReminder = false)
{
    /// <summary>Gets the item done, e.g. by choosing a folder; null for an item it cannot do here.</summary>
    public IRelayCommand? Command { get; init; }

    public bool IsMissing => !IsMet && !IsReminder;
}

public sealed partial class ProjectPageViewModel
{
    // Beyond a few numbers, the marks on the rows tell more than a list of them.
    private const int NamedPlaces = 5;

    /// <summary>The format of the package with its size where it is known, and the first reason for the format.</summary>
    [ObservableProperty]
    public partial string FormatLine { get; private set; } = "";

    [ObservableProperty]
    public partial string FormatReason { get; private set; } = "";

    /// <summary>What a build needs, met or not.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<BeforeItem> BeforeItems { get; private set; } = [];

    /// <summary>What has to happen before a build, as one sentence; empty when nothing holds it back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlockedText))]
    public partial string BlockedText { get; private set; } = "";

    public bool HasBlockedText => BlockedText.Length > 0;

    [ObservableProperty]
    public partial bool CanBuild { get; private set; }

    /// <summary>Where the packages go: the project's folder, else one beside the author's folder; empty when there is none.</summary>
    public string OutputFolder => Session?.Project is not { } project ? ""
        : project.OutputFolder.Length > 0 ? project.OutputFolder
        : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(project.Parts[0].AuthorFolder)) is { Length: > 0 } parent
            ? Path.Join(parent, Text.Format(Strings.Creator_OutputName, ("name", Path.GetFileName(Path.TrimEndingDirectorySeparator(project.Parts[0].AuthorFolder)))))
        : "";

    public string OutputText => Text.Format(Strings.Creator_OutputTo, ("path", OutputFolder));

    public bool HasOutput => OutputFolder.Length > 0;

    /// <summary>Asks for the folder the packages go to; one inside a game's or the author's folder is refused at once.</summary>
    /// <returns>Whether the project took a folder.</returns>
    internal async Task<bool> PickOutputAsync()
    {
        var folder = await Services.Pickers.PickFolderAsync(Strings.Creator_OutputTitle, HasOutput ? OutputFolder : null)
            .ConfigureAwait(true);
        if (folder is null || Session is not { } session)
        {
            return false;
        }

        try
        {
            if (session.Review?.Plan is { } plan)
            {
                var files = FileSystem ?? PhysicalFileSystem.Instance;
                await Task.Run(() => PackageBuilder.CheckOutputFolder(files.GetCanonicalPath(folder), plan))
                    .ConfigureAwait(true);
            }

            ClearProblem();
            session.Change(project => project with { OutputFolder = folder });
            return true;
        }
        catch (PackageBuildException failed)
        {
            ShowProblem(Strings.Creator_BuildFailedTitle, Text.Format(Strings.Creator_BuildFailed, ("failure", failed.Failure.ToString()), ("files", 0), ("file", "")));
            return false;
        }
    }

    [RelayCommand]
    private async Task ChangeOutputAsync() => await PickOutputAsync().ConfigureAwait(true);

    private void ShowBar()
    {
        if (Session is not { } session)
        {
            return;
        }

        var plan = session.Review?.Plan;
        var metadata = session.Project.Metadata;
        var version = metadata.Version.Trim();
        var store = GameStoreName;
        var package = Record?.Parts.SelectMany(part => part.Packages).MaxBy(built => built.BuiltAt);
        var size = package?.Size ?? (plan?.Summary is { Patches: 0 } summary ? summary.CopiedBytes : null);
        FormatLine = plan is null ? "" : GameWording.Join([Description.FormatTitle, size is { } bytes ? Formats.FileSize(bytes) : ""]);
        FormatReason = plan?.Format.Reasons is [var reason, ..] ? Text.Format(Strings.Creator_FormatReason, ("reason", reason.ToString())) : "";
        var runtime = plan?.Findings.Count(finding => finding.BlocksBuild && finding.RuntimeIndex is not null && !ReviewViewModel.IsHosting(finding.Kind)) ?? 0;
        var rules = Review.Rules.Count(row => row.IsError || row.Findings.Any(finding => finding.BlocksBuild));
        List<BeforeItem> items = [new(Item(version.Length > 0 ? "Version" : "NoVersion", ("version", GameWording.ShortVersion(version))), version.Length > 0)];
        if (plan?.Findings.Any(finding => finding.Kind == FindingKind.MissingTitle) == true)
        {
            items.Add(new(Item("NoTitle"), false));
        }

        if (Description.IsManifest && metadata.Authors.Count > 0)
        {
            items.Add(new(Item(Description.AreAuthorsFromSite ? "AuthorsFromSite" : "Authors"), true));
        }

        if (HasVersion)
        {
            items.Add(Cell.IsBlocked ? new(Item("VersionBlocked", ("store", store)), false) : new(Item("Comparison", ("store", store)), Cell.IsCompared));
        }

        if (session.IsPlanning)
        {
            items.Add(new(Item("Plan"), false));
        }
        else if (plan is not null)
        {
            items.Add(new(Strings.Creator_RulesTitle, rules == 0));
            if (runtime > 0)
            {
                items.Add(new(Item("Runtime"), false));
            }
            else if (plan.CarriesCode)
            {
                items.Add(new(Strings.Creator_BeforeRuntime, false, IsReminder: true));
            }
        }

        if (!HasOutput)
        {
            items.Add(new(Item("NoOutput"), false) { Command = ChangeOutputCommand });
        }

        BeforeItems = items;
        CanBuild = plan is { CanBuild: true } && Cell.IsCompared && !Cell.IsBlocked && !session.IsPlanning && HasOutput
            && !Build.IsBuilding && Test.CanRebuild && !Description.HasMetadataProblem
            // A failed build's own way on: the patches it found to accept, or comparing the files again.
            && !Build.NeedsComparison && (!Build.HasBuildFindings || Build.AcceptedAll);
        BlockedText = CanBuild || Build.IsBuilding || plan is null && !Cell.IsBlocked ? "" : Blocked(plan, rules, runtime);

        string Item(string item, params (string Name, object? Value)[] values) => Text.Format(Strings.Creator_BeforeItem, [("item", item), .. values]);
    }

    // The same clauses the check named before the build became its confirmation; a missing version is said by its item and its box.
    private string Blocked(PackagePlan? plan, int rules, int runtime)
    {
        List<string> clauses = [];
        if (Cell.IsBlocked)
        {
            clauses.Add(Text.Format(Strings.Creator_BlockedVersion, ("store", GameStoreName), ("reason", Cell.State switch
            {
                CellState.Installed => "Install",
                CellState.TestInstalled => "Test",
                CellState.GameGone => "Gone",
                _ => "NotReady",
            })));
        }

        if (plan is null)
        {
            return Text.Format(Strings.Creator_ConfirmBlocked, ("what", CreatorWording.List(clauses)));
        }

        var errors = Review.Rules.Count(row => row.IsError);
        if (rules > 0)
        {
            var questions = rules - errors;
            clauses.Add(Text.Format(Strings.Creator_BlockedRules, ("case", errors == 0 ? "questions" : questions == 0 ? "errors" : "both"),
                ("errors", errors), ("questions", questions)));
        }

        var advanced = Session!.Part.Mode == CreatorMode.Advanced;
        var found = plan.Findings.Where(finding => finding.BlocksBuild && finding.RuntimeIndex is null && !ReviewViewModel.IsHosting(finding.Kind)
            && !ReviewViewModel.IsMetadata(finding.Kind)).ToList();
        var atPlaces = found.Select(finding => advanced ? finding.StepIndex : finding.OperationIndex).OfType<int>().ToList();
        var marked = found.Count(finding => (advanced ? finding.StepIndex : finding.OperationIndex) is null);
        List<string> places = [.. atPlaces.Distinct().Order().Select(index => (index + 1).ToString(Text.Culture))];
        if (places.Count is > 0 and <= NamedPlaces)
        {
            clauses.Add(Text.Format(Strings.Creator_BlockedAt, ("count", atPlaces.Count), ("advanced", advanced ? "true" : "false"),
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

        if (plan.Findings.Any(finding => finding.Kind == FindingKind.MissingTitle))
        {
            clauses.Add(Strings.Creator_BlockedTitle);
        }

        if (marked > 0 || (!plan.CanBuild && clauses.Count == 0 && !plan.Findings.Any(finding => finding.Kind == FindingKind.MissingVersion)))
        {
            clauses.Add(Text.Format(Strings.Creator_BlockedMarked, ("count", Math.Max(marked, 1))));
        }

        return clauses.Count == 0 ? "" : Text.Format(Strings.Creator_ConfirmBlocked, ("what", CreatorWording.List(clauses)));
    }
}
