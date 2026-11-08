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
/// <param name="IsPending">Under way, such as a comparison, which holds the build back without having failed.</param>
public sealed record BeforeItem(string Text, bool IsMet, bool IsReminder = false, bool IsPending = false)
{
    /// <summary>Gets the item done, e.g. by choosing a folder; null for an item it cannot do here.</summary>
    public IRelayCommand? Command { get; init; }

    public bool IsMissing => !IsMet && !IsReminder && !IsPending;

    /// <summary>The item with how it stands, which only its icon shows, as a screen reader says it.</summary>
    public string Spoken => JustModIt.Manager.Localization.Text.Format(Strings.Creator_BeforeItemState, ("item", Text),
        ("state", IsMet ? "Met" : IsMissing ? "Missing" : IsPending ? "Pending" : "Reminder"));
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
            var files = FileSystem ?? PhysicalFileSystem.Instance;
            var plans = Cells.Select(cell => cell.Review?.Plan).OfType<PackagePlan>().ToList();
            await Task.Run(() => plans.ForEach(plan => PackageBuilder.CheckOutputFolder(files.GetCanonicalPath(folder), plan)))
                .ConfigureAwait(true);

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

    /// <summary>The cell whose reasons <see cref="BlockedText"/> gives; null while nothing holds the build back.</summary>
    internal CellViewModel? BlockedCell { get; private set; }

    [RelayCommand]
    private void SelectBlocked()
    {
        if (BlockedCell is { } cell && cell != Cell)
        {
            SelectCell(cell);
        }
    }

    private void ShowBar()
    {
        if (Session is not { } session)
        {
            return;
        }

        var plan = session.Review?.Plan;
        var plans = Cells.Select(cell => cell.Review?.Plan).OfType<PackagePlan>().ToList();
        var metadata = session.Project.Metadata;
        var version = metadata.Version.Trim();
        var package = Record?.Parts.SelectMany(part => part.Packages).MaxBy(built => built.BuiltAt);
        var size = package?.Size ?? (plan?.Summary is { Patches: 0 } summary ? summary.CopiedBytes : null);
        // The cells of a table may differ in format, so only a single cell's bar names one.
        FormatLine = plan is null || IsTable ? "" : GameWording.Join([Description.FormatTitle, size is { } bytes ? Formats.FileSize(bytes) : ""]);
        FormatReason = !IsTable && plan?.Format.Reasons is [var reason, ..] ? Text.Format(Strings.Creator_FormatReason, ("reason", reason.ToString())) : "";
        List<BeforeItem> items = [new(Item(version.Length > 0 ? "Version" : "NoVersion", ("version", GameWording.ShortVersion(version))), version.Length > 0)];
        if (plans.Any(each => each.Findings.Any(finding => finding.Kind == FindingKind.MissingTitle)))
        {
            items.Add(new(Item("NoTitle"), false));
        }

        if (Description.IsManifest && metadata.Authors.Count > 0)
        {
            items.Add(new(Item(Description.AreAuthorsFromSite ? "AuthorsFromSite" : "Authors"), true));
        }

        foreach (var cells in Cells.GroupBy(cell => cell.Key.Version))
        {
            var (store, compared) = (cells.First().StoreName, cells.All(cell => cell.IsCompared));
            // A comparison running or waiting its turn, like a plan being made, has neither failed nor ended.
            items.Add(cells.Any(cell => cell.IsBlocked) ? new(Item("VersionBlocked", ("store", store)), false)
                : new(Item("Comparison", ("store", store)), compared,
                    IsPending: !compared && cells.All(cell => cell.State is CellState.Compared or CellState.Comparing)));
        }

        if (session.IsPlanning)
        {
            items.Add(new(Item("Plan"), false, IsPending: true));
        }
        else if (plans.Count > 0)
        {
            items.Add(new(Strings.Creator_RulesTitle, plans.All(each => ReviewViewModel.Tally(each) is { Errors: 0, Questions: 0 })));
            if (plans.Any(each => RuntimeProblems(each) > 0))
            {
                items.Add(new(Item("Runtime"), false));
            }
            else if (plans.Any(each => each.CarriesCode))
            {
                items.Add(new(Strings.Creator_BeforeRuntime, false, IsReminder: true));
            }
        }

        if (!HasOutput)
        {
            items.Add(new(Item("NoOutput"), false) { Command = ChangeOutputCommand });
        }

        BeforeItems = items;
        CanBuild = Cells.Count > 0 && Cells.All(cell => cell.Review?.Plan is { CanBuild: true } && cell.IsCompared && !cell.IsBlocked)
            && !session.IsPlanning && HasOutput && !Build.IsBuilding && Test.CanRebuild && !Description.HasMetadataProblem
            // A failed build's own way on: the patches it found to accept, or comparing the files again.
            && !Build.NeedsComparison && (!Build.HasBuildFindings || Build.AcceptedAll);
        // The chosen cell's reasons when it has any, else the first cell's that does.
        BlockedCell = CanBuild || Build.IsBuilding ? null : Cell is { } chosen && Blocks(chosen) ? chosen : Cells.FirstOrDefault(Blocks);
        BlockedText = BlockedCell is not { } blocked || Blocked(blocked) is not { Length: > 0 } text ? ""
            : IsTable ? Text.Format(Strings.Creator_BlockedCell, ("part", blocked.Part.Name.Length > 0 ? blocked.Part.Name : Strings.Creator_PartBase),
                ("store", blocked.StoreName), ("text", text))
            : text;

        string Item(string item, params (string Name, object? Value)[] values) => Text.Format(Strings.Creator_BeforeItem, [("item", item), .. values]);

        static bool Blocks(CellViewModel cell) => cell.IsBlocked || cell.Review?.Plan is { CanBuild: false };
    }

    private static int RuntimeProblems(PackagePlan plan) =>
        plan.Findings.Count(finding => finding.BlocksBuild && finding.RuntimeIndex is not null && !ReviewViewModel.IsHosting(finding.Kind));

    // The same clauses the check named before the build became its confirmation; a missing version is said by its item and its box.
    private static string Blocked(CellViewModel cell)
    {
        List<string> clauses = [];
        if (cell.IsBlocked)
        {
            clauses.Add(Text.Format(Strings.Creator_BlockedVersion, ("store", cell.StoreName), ("reason", cell.State switch
            {
                CellState.Installed => "Install",
                CellState.TestInstalled => "Test",
                CellState.GameGone => "Gone",
                _ => "NotReady",
            })));
        }

        if (cell.Review?.Plan is not { } plan)
        {
            return clauses.Count == 0 ? "" : Text.Format(Strings.Creator_ConfirmBlocked, ("what", CreatorWording.List(clauses)));
        }

        var (errors, questions) = ReviewViewModel.Tally(plan);
        if (errors + questions > 0)
        {
            clauses.Add(Text.Format(Strings.Creator_BlockedRules, ("case", errors == 0 ? "questions" : questions == 0 ? "errors" : "both"),
                ("errors", errors), ("questions", questions)));
        }

        var advanced = cell.Part.Mode == CreatorMode.Advanced;
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

        if (RuntimeProblems(plan) is var runtime and > 0)
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
