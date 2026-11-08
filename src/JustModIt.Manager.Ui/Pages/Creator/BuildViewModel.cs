using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>Where the last step stands.</summary>
public enum PackageState
{
    /// <summary>The format and the metadata, before a build or after one that failed.</summary>
    Ready,
    Building,
    Built,
}

/// <summary>
/// The package's build on the thread pool, with its stage, file and bytes and a way to cancel it, why it failed, and the
/// package it made with the site's warnings about it.
/// </summary>
public sealed partial class BuildViewModel : ObservableObject, IDisposable
{
    private readonly CreatorPageViewModel _page;
    private readonly CreatorSession _session;
    private readonly PackageDescriptionViewModel _description;
    private readonly TestInstallViewModel _test;

    private CancellationTokenSource? _building;
    private Task? _build;
    private (string Path, string Name)? _target;
    private IReadOnlyList<(PlanFinding Finding, PlannedOperation? Operation)> _buildFindings = [];
    private DateTimeOffset _builtAt;

    /// <summary>Creates the build of a project's package.</summary>
    /// <param name="page">The creator.</param>
    /// <param name="session">The project.</param>
    /// <param name="description">The metadata, whose problems hold the build back.</param>
    /// <param name="test">The test install, which a new package starts over.</param>
    public BuildViewModel(CreatorPageViewModel page, CreatorSession session, PackageDescriptionViewModel description, TestInstallViewModel test)
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

    [ObservableProperty]
    public partial string GameName { get; private set; } = "";

    [ObservableProperty]
    public partial string GameStore { get; private set; } = "";

    [ObservableProperty]
    public partial string GameDetails { get; private set; } = "";

    [ObservableProperty]
    public partial string BuildHint { get; private set; } = "";

    /// <summary>The next build goes where the last one of the same name went, which the author may change.</summary>
    [ObservableProperty]
    public partial bool ReusesTarget { get; private set; }

    /// <summary>Why the last build failed; empty when it did not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string ProblemTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string ProblemText { get; private set; } = "";

    public bool HasProblem => ProblemTitle.Length > 0;

    /// <summary>The files changed since the comparison, which the author has to compare again before building.</summary>
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
    public partial string BuildTarget { get; private set; } = "";

    [ObservableProperty]
    public partial string Stage { get; private set; } = "";

    [ObservableProperty]
    public partial string Percent { get; private set; } = "";

    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string FileLine { get; private set; } = "";

    /// <summary>The package built last; empty before a build.</summary>
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

        // The window may close as soon as this returns, and a stopped build still removes the package it was writing.
        if (_build is { IsCompleted: false } build)
        {
            CancelBuild();
            await build.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        }

        return true;
    }

    public void Dispose() => _building?.Cancel();

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task BuildAsync() => BuildToAsync(askWhere: false);

    private bool CanBuild() => !_description.HasMetadataProblem;

    [RelayCommand]
    private Task BuildElsewhereAsync() => BuildToAsync(askWhere: true);

    private async Task BuildToAsync(bool askWhere)
    {
        // A name or version typed a moment ago may not be planned yet, and the package takes them from the plan.
        await _session.SettledAsync().ConfigureAwait(true);
        if (_session.Review?.Plan is not { } plan || _page.Game is not { } game || _description.HasMetadataProblem)
        {
            return;
        }

        if (!plan.CanBuild)
        {
            _page.GoTo(CreatorStep.Review);
            return;
        }

        // A package of another version or format goes to a file of its own, so building it keeps the earlier package.
        var extension = _page.Options.ExtensionFor(plan.Format.Kind);
        var name = SuggestedName(game, extension);
        var path = !askWhere && _target is { } target && target.Name == name
            ? target.Path
            : await _page.Services.Pickers.PickSaveFileAsync(Strings.Creator_BuildTitle, name,
                new FileKind(plan.Format.Kind == PackageKind.Zip ? Strings.Creator_ZipFiles : Strings.Creator_PackageFiles, extension),
                Path.GetDirectoryName(_target?.Path ?? _session.Path ?? _session.Part.AuthorFolder)).ConfigureAwait(true);
        if (path is not null)
        {
            await (_build = BuildAsync(plan, game, path, name)).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void CancelBuild() => _building?.Cancel();

    [RelayCommand]
    private void Accept(string key) => _session.Describe(project => project.Acknowledge(key));

    [RelayCommand]
    private Task CompareAgainAsync() => _page.CompareAgainAsync();

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
        BuildCommand.NotifyCanExecuteChanged();
        if (_session.Review?.Plan is not { } plan)
        {
            return;
        }

        if (_page.Game is { } game)
        {
            GameName = game.Name;
            GameStore = game.Key.Store.Value;
            GameDetails = GameWording.Join(
            [
                GameWording.StoreName(game.Key.Store),
                game.Game.BuildId is { Length: > 0 } build ? Text.Format(Strings.Creator_GameBuild, ("build", build)) : "",
            ]);
            var name = SuggestedName(game, _page.Options.ExtensionFor(plan.Format.Kind));
            ReusesTarget = _target is { } target && target.Name == name;
            BuildHint = ReusesTarget
                ? Text.Format(Strings.Creator_BuildTo, ("name", Path.GetFileName(_target!.Value.Path)))
                : Text.Format(Strings.Creator_BuildHint, ("name", name));
        }

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

    /// <summary>Forgets the package and the last build's failure, which belong to another copy of the game.</summary>
    internal void Forget()
    {
        (OutputPath, _target, ResultName, ResultSize, ResultDetails, ResultFolder, SiteWarnings) = ("", null, "", "", "", "", []);
        (ProblemTitle, ProblemText, NeedsComparison, _buildFindings, BuildFindings, BuildTarget) = ("", "", false, [], [], "");
        State = PackageState.Ready;
    }

    private async Task BuildAsync(PackagePlan plan, DetectedGameEntry game, string path, string name)
    {
        using var building = _building = new CancellationTokenSource();
        _target = (path, name);
        (ProblemTitle, ProblemText, NeedsComparison, _buildFindings, BuildFindings) = ("", "", false, [], []);
        (Stage, Percent, Progress, FileLine) = (Text.Format(Strings.Creator_BuildStage, ("stage", nameof(BuildStage.Checking))), "", 0, "");
        BuildTarget = Text.Format(Strings.Creator_BuildTarget, ("path", path));
        State = PackageState.Building;
        try
        {
            // The game's build may have changed since the comparison, and the package names the one it was made against.
            var detected = await CurrentGameAsync(game).ConfigureAwait(true);
            var request = new BuildRequest
            {
                OutputPath = path,
                Game = detected,
                Installs = [.. _page.Agent.State.Games.SelectMany(view => view.Installs)
                    .Select(install => new ExistingInstall(install.Id, install.InstallDirectory))],
                Site = _page.Agent.State.Catalogue.ReleaseFiles,
            };
            var result = await new PackageBuilder(new XDelta3Patcher(), _page.Options, fileSystem: _page.FileSystem)
                .BuildAsync(plan.Confirm(), request, new LatestProgress<BuildProgress>(Show), building.Token)
                .ConfigureAwait(true);
            Built(result);
        }
        catch (OperationCanceledException) when (building.IsCancellationRequested)
        {
            Fail(Strings.Creator_BuildCancelledTitle, Strings.Creator_BuildCancelled);
        }
        catch (PackageBuildException failed)
        {
            NeedsComparison = failed.Failure is BuildFailure.FilesChanged or BuildFailure.OtherGame;
            _buildFindings = [.. failed.Findings.Where(finding => finding.BlocksBuild)
                .Select(finding => (finding, finding.OperationIndex is { } index ? plan.Operations[index] : null))];
            // The next build asks for a place again rather than going back to one the build refused or could not write to.
            if (failed.Failure is BuildFailure.WrongExtension or BuildFailure.OutputInGameFolder or BuildFailure.OutputInAuthorFolder)
            {
                _target = null;
            }

            Fail(Strings.Creator_BuildFailedTitle, Text.Format(Strings.Creator_BuildFailed, ("failure", failed.Failure.ToString()),
                ("files", failed.Paths.Count), ("file", failed.Paths.Count > 0 ? InFolder(plan, failed.Paths[0]) : "")));
        }
        catch (CreatorBlockedException blocked)
        {
            Fail(CreatorWording.BlockTitle(blocked.Blocks[0], game.Game.InstallDirectory),
                string.Join(' ', blocked.Blocks.Select(block => CreatorWording.Block(block, game.Game.InstallDirectory))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _target = null;
            Fail(Strings.Creator_BuildFailedTitle, Strings.Creator_BuildWriteFailed);
        }
        // Anything else, such as a patch the encoder could not make, ends the build like the failures above rather than the
        // window, which would lose the author's unsaved work.
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
    }

    private async Task<InstalledGame?> CurrentGameAsync(DetectedGameEntry game)
    {
        var (_, answer) = await AgentRequests.AskAsync(_page.Services, new GetDetectedGames()).ConfigureAwait(true);
        var current = (answer as DetectedGames)?.Games.FirstOrDefault(entry => entry.Key == game.Key) ?? game;
        var view = current.Game;
        return new InstalledGame
        {
            Key = current.Key,
            Name = current.Name,
            InstallDirectory = view.InstallDirectory,
            LibraryId = view.LibraryId,
            BuildId = view.BuildId,
            State = view.State,
            Reason = view.Reason,
            Platform = view.Platform,
            IsStale = view.IsStale,
        };
    }

    private void Built(PackageBuildResult result)
    {
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
        _builtAt = _page.Services.Clock.GetUtcNow();
        SiteWarnings = [.. result.Findings.Where(finding => finding.Kind is FindingKind.LargerThanSiteTakes or FindingKind.FormatNotOnSite)
            .Select(finding => CreatorWording.Finding(finding))];
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

    private void Show(BuildProgress progress)
    {
        if (State != PackageState.Building)
        {
            return;
        }

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
        if (_session.Review?.Plan is { } plan && _buildFindings.Any(found => !Makes(plan, found.Operation)))
        {
            _buildFindings = [.. _buildFindings.Where(found => Makes(plan, found.Operation))];
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

    // A copy of the game from another store gets a package of its own, which a name without the store would overwrite.
    private string SuggestedName(DetectedGameEntry game, string extension)
    {
        var name = _page.IsGameOnOtherStores
            ? Text.Format(Strings.Creator_PackageGameOnStore, ("game", game.Name), ("store", GameWording.StoreName(game.Key.Store)))
            : game.Name;
        return FileNames.Suggest(_session.Project.Metadata.Version.Trim() is { Length: > 0 } version
                ? Text.Format(Strings.Creator_PackageName, ("game", name), ("version", version))
                : name,
            Strings.Creator_ProjectFallbackName, extension);
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
