using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>Where the project's build stands.</summary>
public enum PackageState
{
    /// <summary>Before a build, or after one that failed or stopped.</summary>
    Ready,
    Building,
    Built,
}

/// <summary>A package a run made.</summary>
/// <param name="Name">Its file's name.</param>
/// <param name="Details">Its part and the stores of the versions it serves.</param>
/// <param name="Size">Its size, as words.</param>
/// <param name="Path">Its file.</param>
/// <param name="Versions">The versions of the game it serves.</param>
/// <param name="Part">The name of its part; empty for the base.</param>
public sealed record BuiltRow(string Name, string Details, string Size, string Path, IReadOnlyList<GameKey> Versions, string Part);

/// <summary>
/// The build of every part and version on the thread pool, with its package, stage, file and bytes and a way to cancel it, why
/// it failed, and the packages it made with the site's warnings about them.
/// </summary>
public sealed partial class BuildViewModel : ObservableObject, IDisposable
{
    private readonly ProjectPageViewModel _page;
    private readonly CreatorSession _session;
    private readonly PackageDescriptionViewModel _description;
    private readonly TestInstallViewModel _test;

    private CancellationTokenSource? _building;
    private Task? _build;
    private bool _asksForFolder;
    private IReadOnlyList<(PlanFinding Finding, PlannedOperation? Operation)> _buildFindings = [];
    private DateTimeOffset _builtAt;

    /// <summary>Creates the build of a project's package.</summary>
    /// <param name="page">The project's page.</param>
    /// <param name="session">The project.</param>
    /// <param name="description">The metadata, whose problems hold the build back.</param>
    /// <param name="test">The test install, which a new package starts over.</param>
    public BuildViewModel(ProjectPageViewModel page, CreatorSession session, PackageDescriptionViewModel description, TestInstallViewModel test)
    {
        _page = page;
        _session = session;
        _description = description;
        _test = test;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsBuilding), nameof(IsBuilt), nameof(ShowsBuild), nameof(HasEarlierPackage),
        nameof(ShowsPackage), nameof(EarlierText))]
    public partial PackageState State { get; private set; }

    public bool IsReady => State == PackageState.Ready;

    public bool IsBuilding => State == PackageState.Building;

    public bool IsBuilt => State == PackageState.Built;

    /// <summary>Why the last build failed; empty when it did not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string ProblemTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string ProblemText { get; private set; } = "";

    public bool HasProblem => ProblemTitle.Length > 0;

    /// <summary>The files changed since the comparison, which runs again by itself before the next build.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBuild))]
    public partial bool NeedsComparison { get; private set; }

    /// <summary>Warnings only the build found, which the author accepts before building again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBuildFindings), nameof(AcceptedAll), nameof(ShowsBuild))]
    public partial IReadOnlyList<FindingLine> BuildFindings { get; private set; } = [];

    public bool HasBuildFindings => BuildFindings.Count > 0;

    /// <summary>The author accepted every warning of the last build, which the next build takes.</summary>
    public bool AcceptedAll => HasBuildFindings && BuildFindings.All(line => line.IsAccepted);

    /// <summary>
    /// The build's button, which a failed build replaces with its own way on: the warnings to accept, or comparing again.
    /// </summary>
    public bool ShowsBuild => IsReady && !HasBuildFindings && !NeedsComparison;

    [ObservableProperty]
    public partial string Stage { get; private set; } = "";

    [ObservableProperty]
    public partial string Percent { get; private set; } = "";

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string FileLine { get; private set; } = "";

    /// <summary>Which package of the run is being built; empty for a run of one.</summary>
    [ObservableProperty]
    public partial string PackageLine { get; private set; } = "";

    /// <summary>The packages the last run made, each with the versions it serves.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSeveral), nameof(PublishTitle), nameof(BuiltOverline))]
    public partial IReadOnlyList<BuiltRow> Packages { get; private set; } = [];

    public bool HasSeveral => Packages.Count > 1;

    public string PublishTitle => Text.Format(Strings.Creator_PublishTitle, ("several", HasSeveral ? "true" : "false"));

    /// <summary>That the packages of the last run are ready, over them.</summary>
    public string BuiltOverline => Text.Format(Strings.Creator_BuiltHeading, ("count", Math.Max(Packages.Count, 1))).ToUpper(Text.Culture);

    /// <summary>The package built first; empty before a build.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEarlierPackage), nameof(ShowsPackage))]
    public partial string OutputPath { get; private set; } = "";

    /// <summary>A build failed or stopped after an earlier one, whose package is still there but is not this build's.</summary>
    public bool HasEarlierPackage => IsReady && OutputPath.Length > 0;

    public bool ShowsPackage => IsBuilt || HasEarlierPackage;

    /// <summary>That the package shown is an earlier build's, and when it was built.</summary>
    public string EarlierText => GameWording.Join(
        [Strings.Creator_EarlierPackage, HasEarlierPackage ? Formats.RelativeTime(_builtAt, _page.Services.Clock) : ""]).ToUpper(Text.Culture);

    [ObservableProperty]
    public partial string ResultName { get; private set; } = "";

    [ObservableProperty]
    public partial string ResultSize { get; private set; } = "";

    [ObservableProperty]
    public partial string ResultDetails { get; private set; } = "";

    [ObservableProperty]
    public partial string ResultFolder { get; private set; } = "";

    /// <summary>Why the site would not take the package as it is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSiteWarnings))]
    public partial IReadOnlyList<string> SiteWarnings { get; private set; } = [];

    public bool HasSiteWarnings => SiteWarnings.Count > 0;

    [ObservableProperty]
    public partial string PublishText { get; private set; } = "";

    /// <summary>Asks whether a running build may stop, and waits until it removed its partial package.</summary>
    /// <returns>Whether nothing keeps the project from closing any more.</returns>
    internal async Task<bool> CanStopAsync()
    {
        if (_building is null)
        {
            return true;
        }

        if (!await _page.Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(Strings.Creator_StopBuildTitle, Strings.Creator_StopBuild,
                Strings.Creator_StopBuildConfirm, Icons.CircleX, DialogTone.Danger)
            {
                ConfirmIcon = Icons.CircleX,
            }).ConfigureAwait(true))
        {
            return false;
        }

        // The window may close as soon as this returns, and a stopped build still removes the package it was writing, unless its
        // drive stopped answering, which the window does not wait for.
        if (_build is { IsCompleted: false } build)
        {
            CancelBuild();
            await Task.WhenAny(build, Task.Delay(LibraryAccess.TimeLimit, _page.Services.Clock)).ConfigureAwait(true);
        }

        return true;
    }

    public void Dispose() => _building?.Cancel();

    [RelayCommand]
    private async Task BuildAsync()
    {
        // A place the last build refused or could not write to is asked for again rather than taken by this build.
        if (_asksForFolder)
        {
            if (!await _page.PickOutputAsync().ConfigureAwait(true))
            {
                return;
            }

            _asksForFolder = false;
        }

        // A name or version typed a moment ago may not be planned yet, and the packages take them from the plans.
        await _session.SettledAsync().ConfigureAwait(true);

        if (_page.CanBuild)
        {
            await (_build = BuildAsync(_page.OutputFolder)).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void CancelBuild() => _building?.Cancel();

    [RelayCommand]
    private void Accept(string key) => _session.Change(project => project.Acknowledge(key));

    [RelayCommand]
    private void Reveal()
    {
        if (ResultFolder.Length > 0)
        {
            _page.Services.Links.OpenFolder(ResultFolder);
        }
    }

    [RelayCommand]
    private void OpenSite() => _page.Services.Links.Open(PageLinks.Dashboard(_page.Services.Context.Site.SiteAddress));

    /// <summary>Shows what the next build does with the plan as it is now.</summary>
    internal void Show()
    {
        ShowBuildFindings();
        PublishText = _description.IsManifest ? Strings.Creator_PublishManifest : Strings.Creator_PublishZip;
    }

    /// <summary>Takes back a failure that asked for the files to be compared again, now that they were.</summary>
    internal void FilesCompared()
    {
        if (NeedsComparison)
        {
            (NeedsComparison, ProblemTitle, ProblemText) = (false, "", "");
        }
    }

    /// <summary>Forgets the packages and the last build's failure once a version they were made for changed or left.</summary>
    /// <param name="versions">The versions whose game or folder changed or that left the project.</param>
    internal void Forget(IReadOnlyList<GameKey> versions)
    {
        if (versions.Count == 0 || (Packages.Count > 0 && !Packages.Any(row => row.Versions.Intersect(versions).Any())))
        {
            return;
        }

        (OutputPath, ResultName, ResultSize, ResultDetails, ResultFolder, SiteWarnings, Packages) = ("", "", "", "", "", [], []);
        (ProblemTitle, ProblemText, NeedsComparison, _buildFindings, BuildFindings) = ("", "", false, [], []);
        State = PackageState.Ready;
    }

    private async Task BuildAsync(string folder)
    {
        using var building = _building = new CancellationTokenSource();
        (ProblemTitle, ProblemText, NeedsComparison, _buildFindings, BuildFindings) = ("", "", false, [], []);
        (Stage, Percent, Progress, FileLine, PackageLine) = (Text.Format(Strings.Creator_BuildStage, ("stage", nameof(BuildStage.Checking))), "", 0, "", "");
        State = PackageState.Building;
        var project = _session.Project;
        var done = 0;
        var plans = project.Parts.SelectMany(part => project.Versions.Select(version => _session.ReviewOf(new CellKey(part.Name, version.Game.Key))!.Plan)).ToList();
        try
        {
            // The games' builds may have changed since the comparisons, and a package names the ones it was made against.
            var games = await CurrentGamesAsync().ConfigureAwait(true);
            var request = new ProjectBuildRequest
            {
                ProjectId = project.Id,
                OutputFolder = folder,
                Parts = [.. project.Parts.Select(part => (part.Name, (IReadOnlyList<BuildVersion>)[.. project.Versions.Select(version => new BuildVersion(
                    games[version.Game.Key], _session.ReviewOf(new CellKey(part.Name, version.Game.Key))!.Plan))]))],
                PackageName = (part, versions) => part.Length > 0
                    ? Text.Format(Strings.Creator_PartPackageName, ("name", SuggestedName(versions)), ("part", part))
                    : SuggestedName(versions),
                FallbackName = Strings.Creator_ProjectFallbackName,
                Built = _ => done++,
                // The run asks on its own thread, and the dialogs live on the window's.
                ReplaceFiles = names => Dispatcher.UIThread.InvokeAsync(() => _page.Services.Dialogs.ShowAsync(new ConfirmDialogViewModel(
                    Strings.Creator_ReplaceFilesTitle, Text.Format(Strings.Creator_ReplaceFilesMessage, ("names", CreatorWording.List(names))),
                    Strings.Creator_ReplaceFilesConfirm, Icons.TriangleAlert, DialogTone.Danger))),
                Installs = [.. _page.Agent.State.Games.SelectMany(view => view.Installs)
                    .Select(install => new ExistingInstall(install.Id, install.InstallDirectory))],
                Site = _page.Agent.State.Catalogue.ReleaseFiles,
            };
            var finished = await new ProjectBuilder(new XDelta3Patcher(), _page.Options, _page.Library, _page.Access, _page.Services.Clock, _page.FileSystem)
                .BuildAsync(request, new LatestProgress<ProjectBuildProgress>(Show), building.Token)
                .ConfigureAwait(true);
            if (finished.Count > 0)
            {
                Built(finished);
            }
            else
            {
                State = PackageState.Ready;
            }
        }
        catch (OperationCanceledException) when (building.IsCancellationRequested)
        {
            Fail(Strings.Creator_BuildCancelledTitle, Text.Format(Strings.Creator_BuildCancelled, ("finished", done > 0 ? "true" : "false")));
        }
        catch (PackageBuildException failed)
        {
            NeedsComparison = failed.Failure is BuildFailure.FilesChanged or BuildFailure.OtherGame;
            var plan = failed.Versions is [var first, ..] ? _session.ReviewOf(new CellKey(failed.Part ?? "", first))?.Plan ?? plans[0] : plans[0];
            _buildFindings = [.. failed.Findings.Where(finding => finding.BlocksBuild)
                .Select(finding => (finding, finding.OperationIndex is { } index ? plan.Operations[index] : null))];
            _asksForFolder = failed.Failure is BuildFailure.OutputInGameFolder or BuildFailure.OutputInAuthorFolder;
            // A folder that does not answer is named as the author knows it, a file of the package by its path in the game.
            var text = Text.Format(Strings.Creator_BuildFailed, ("failure", failed.Failure.ToString()),
                ("files", failed.Paths.Count), ("file", failed.Paths.Count == 0 ? ""
                    : CreatorWording.Wrappable(failed.Failure == BuildFailure.FolderUnavailable ? failed.Paths[0] : InFolder(plan, failed.Paths[0]))));
            // A run of several packages says which one failed.
            Fail(Strings.Creator_BuildFailedTitle, failed.Part is { } part && plans.Count > 1
                ? Text.Format(Strings.Creator_BlockedCell, ("part", part.Length > 0 ? part : Strings.Creator_PartBase), ("store", Stores(failed.Versions)), ("text", text))
                : text);
            if (NeedsComparison)
            {
                _ = _page.CheckAsync(anyway: true);
            }
        }
        catch (CreatorBlockedException blocked)
        {
            Fail(CreatorWording.BlockTitle(blocked.Blocks[0], plans[0].GameDirectory),
                string.Join(' ', blocked.Blocks.Select(block => CreatorWording.Block(block, plans[0].GameDirectory))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _asksForFolder = true;
            Fail(Strings.Creator_BuildFailedTitle, Strings.Creator_BuildWriteFailed);
        }
        // Anything else, such as a patch the encoder could not make, ends the build like the failures above rather than the
        // window.
        catch (Exception exception)
        {
            LogBuildFailed(_page.Log, exception);
            Fail(Strings.Creator_BuildFailedTitle, Text.Format(Strings.Creator_Unexpected, ("error", exception.GetType().Name)));
        }
        finally
        {
            if (_building == building)
            {
                _building = null;
            }

            Show();
        }

        // The run wrote the record of what it finished, which the page and the library's card tell.
        await _page.ReadRecordAsync().ConfigureAwait(true);
    }

    private async Task<Dictionary<GameKey, InstalledGame>> CurrentGamesAsync()
    {
        var (_, answer) = await AgentRequests.AskAsync(_page.Services, new GetDetectedGames()).ConfigureAwait(true);
        return _session.Project.Versions.Select(version => (answer as DetectedGames)?.Games.FirstOrDefault(entry => entry.Key == version.Game.Key)
                ?? _page.GameOf(version.Game.Key)!)
            .ToDictionary(current => current.Key, current => new InstalledGame
            {
                Key = current.Key,
                Name = current.Name,
                InstallDirectory = current.Game.InstallDirectory,
                LibraryId = current.Game.LibraryId,
                BuildId = current.Game.BuildId,
                State = current.Game.State,
                Reason = current.Game.Reason,
                Platform = current.Game.Platform,
                IsStale = current.Game.IsStale,
            });
    }

    private static string Stores(IEnumerable<GameKey> versions) => CreatorWording.List([.. versions.Select(version => GameWording.StoreName(version.Store))]);

    private void Built(IReadOnlyList<FinishedPackage> finished)
    {
        Packages = [.. finished.Select(built => new BuiltRow(Path.GetFileName(built.Result.OutputPath),
            GameWording.Join([built.Part.Length > 0 ? built.Part : Strings.Creator_PartBase, Stores(built.Package.Versions)]),
            Formats.FileSize(built.Result.PackageSize), built.Result.OutputPath, built.Package.Versions, built.Part))];
        var package = finished[0];
        var result = package.Result;
        OutputPath = result.OutputPath;
        ResultName = Path.GetFileName(result.OutputPath);
        ResultSize = Formats.FileSize(result.PackageSize);
        var metadata = _session.Project.Metadata;
        ResultDetails = GameWording.Join(
        [
            result.Kind == PackageKind.Zip ? Strings.Creator_ZipKind : Strings.Creator_ManifestKind,
            metadata.Version.Trim().Length > 0 ? GameWording.ShortVersion(metadata.Version.Trim()) : "",
            result.Kind == PackageKind.Manifest ? string.Join(", ", metadata.Authors) : "",
        ]);
        ResultFolder = Path.GetDirectoryName(result.OutputPath) ?? "";
        _builtAt = package.Package.BuiltAt;
        SiteWarnings = [.. finished.SelectMany(built => built.Result.Findings)
            .Where(finding => finding.Kind is FindingKind.LargerThanSiteTakes or FindingKind.FormatNotOnSite)
            .Select(finding => CreatorWording.Finding(finding)).Distinct()];
        _test.Built();
        State = PackageState.Built;
    }

    private void Fail(string title, string text)
    {
        (ProblemTitle, ProblemText) = (title, text);
        State = PackageState.Ready;
    }

    // The author's folder is laid out like the game's, so a file of either reads by its path in the game, without the folders
    // above it, which may name the user's profile.
    private static string InFolder(PackagePlan plan, string path) =>
        PathComparison.IsWithin(plan.AuthorDirectory, path) ? PackagePath.FromFullPath(plan.AuthorDirectory, path)
        : PathComparison.IsWithin(plan.GameDirectory, path) ? PackagePath.FromFullPath(plan.GameDirectory, path)
        : path;

    private void Show(ProjectBuildProgress run)
    {
        if (State != PackageState.Building)
        {
            return;
        }

        var progress = run.Build;
        PackageLine = run.PackageCount > 1 ? Text.Format(Strings.Creator_BuildingOf, ("number", run.Package), ("count", run.PackageCount)) : "";
        Progress = progress.BytesTotal > 0 ? 100.0 * progress.BytesDone / progress.BytesTotal : 0;
        Percent = Formats.Percent(progress.BytesDone, progress.BytesTotal);
        Stage = GameWording.Join(
        [
            Text.Format(Strings.Creator_BuildStage, ("stage", progress.Stage.ToString())),
            progress.BytesTotal > 0 ? Text.Format(Strings.Creator_BytesOf, ("done", Formats.FileSize(progress.BytesDone)),
                ("total", Formats.FileSize(progress.BytesTotal))) : "",
        ]);
        FileLine = progress.FileNumber == 0 ? ""
            : GameWording.Join(
            [
                Text.Format(Strings.Creator_FileOf, ("number", progress.FileNumber), ("count", progress.FileCount)),
                progress.CurrentFile ?? "",
            ]);
    }

    // A warning the author accepted stays in the list, marked, so the click shows what it did and the build can go on. One
    // about a patch the plan no longer makes, e.g. of a file left out or changed since, goes, and with the last the failure.
    private void ShowBuildFindings()
    {
        var plans = _session.Cells.Select(_session.ReviewOf).OfType<CreatorReview>().Select(review => review.Plan).ToList();
        if (plans.Count > 0 && _buildFindings.Any(found => !plans.Any(plan => Makes(plan, found.Operation))))
        {
            _buildFindings = [.. _buildFindings.Where(found => plans.Any(plan => Makes(plan, found.Operation)))];
            if (_buildFindings.Count == 0)
            {
                (ProblemTitle, ProblemText) = ("", "");
            }
        }

        var acknowledged = _session.Project.Acknowledged;
        List<FindingLine> lines = [.. _buildFindings.Select(found => new FindingLine(ReviewLine(found.Finding), RowTone.Warning,
            found.Finding.AcknowledgementKey, found.Finding.AcknowledgementKey is { } key && acknowledged.Contains(key)))];
        if (!lines.SequenceEqual(BuildFindings))
        {
            BuildFindings = lines;
        }
    }

    // A package for some of the versions names their stores, since another package serves the others and would take its name.
    private string SuggestedName(IReadOnlyList<BuildVersion> versions)
    {
        var game = versions[0].Game.Name;
        var name = versions.Count < _session.Project.Versions.Count
            ? Text.Format(Strings.Creator_PackageGameOnStore, ("game", game), ("store", Stores(versions.Select(version => version.Game.Key))))
            : game;
        return _session.Project.Metadata.Version.Trim() is { Length: > 0 } version
            ? Text.Format(Strings.Creator_PackageName, ("game", name), ("version", version))
            : name;
    }

    private static string ReviewLine(PlanFinding finding) =>
        ReviewViewModel.IsHosting(finding.Kind) ? CreatorWording.RuleTitle(finding) : CreatorWording.Finding(finding);

    private static bool Makes(PackagePlan plan, PlannedOperation? operation) =>
        operation is null || plan.Operations.Any(planned => planned.Kind == operation.Kind
            && PackagePath.Comparer.Equals(planned.Path, operation.Path) && planned.Original == operation.Original
            && planned.SourceFile == operation.SourceFile);

    [LoggerMessage(Level = LogLevel.Error, Message = "The package build failed unexpectedly")]
    private static partial void LogBuildFailed(ILogger logger, Exception exception);
}
