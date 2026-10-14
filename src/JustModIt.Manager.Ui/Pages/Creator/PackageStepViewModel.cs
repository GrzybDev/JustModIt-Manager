using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>Where the last step stands.</summary>
public enum PackageState
{
    /// <summary>The format and the metadata, before a build.</summary>
    Ready,
    Building,
    Built,
}

/// <summary>Where the package's test install stands, as the agent reports it.</summary>
public enum TestInstallState
{
    None,
    Installing,
    Installed,
    Undoing,

    /// <summary>The test install was undone, and the game has its original files again.</summary>
    Undone,
    Failed,
}

/// <summary>A format in the table that explains how the creator picks it.</summary>
/// <param name="Extension">The format's extension.</param>
/// <param name="Text">What packages of it hold.</param>
/// <param name="IsChosen">The package takes this format.</param>
public sealed record FormatRow(string Extension, string Text, bool IsChosen);

/// <summary>
/// The last step: the format the plan needs with the reasons for it, the package's metadata, the build on the thread pool
/// with its stage, file and bytes and a way to cancel it, the site's warnings about the result, and a test install of the
/// package through the agent, which the agent also undoes.
/// </summary>
public sealed partial class PackageStepViewModel : ObservableObject, ICreatorStepView
{
    private readonly CreatorPageViewModel _page;
    private readonly CreatorSession _session;

    private readonly Dictionary<Guid, (JobOutcome Outcome, JobFailureView? Failure)> _ended = [];
    private GameKey? _hintsFor;
    private ProjectHints? _hints;
    private CancellationTokenSource? _building;
    private Task? _build;
    private (string Path, string Name)? _target;
    private IReadOnlyList<(PlanFinding Finding, PlannedOperation? Operation)> _buildFindings = [];
    private Guid? _testJob;
    private Guid? _undoJob;
    private bool _lostTrack;
    private string _testNote = "";
    private (GameKey? Key, string Folder) _game;

    /// <summary>Creates the step for a project.</summary>
    /// <param name="page">The creator.</param>
    /// <param name="session">The project.</param>
    public PackageStepViewModel(CreatorPageViewModel page, CreatorSession session)
    {
        _page = page;
        _session = session;
        _game = (session.Project.Game?.Key, session.Project.GameFolder);
        _session.PropertyChanged += OnSessionChanged;
        _page.Agent.PropertyChanged += OnAgentChanged;
        _page.Agent.JobsProgressed += OnJobsProgressed;
        _page.Agent.JobEnded += OnJobEnded;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsBuilding), nameof(IsBuilt), nameof(ShowsBuild))]
    public partial PackageState State { get; private set; }

    public bool IsReady => State == PackageState.Ready;

    public bool IsBuilding => State == PackageState.Building;

    public bool IsBuilt => State == PackageState.Built;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsZip))]
    public partial bool IsManifest { get; private set; }

    public bool IsZip => !IsManifest;

    [ObservableProperty]
    public partial string FormatTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string FormatText { get; private set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<FormatRow> FormatRows { get; private set; } = [];

    [ObservableProperty]
    public partial string Lead { get; private set; } = "";

    [ObservableProperty]
    public partial string PackageTitle { get; set; } = "";

    [ObservableProperty]
    public partial string Version { get; set; } = "";

    public ObservableCollection<string> Authors { get; } = [];

    [ObservableProperty]
    public partial string NewAuthor { get; set; } = "";

    /// <summary>The site's last release of the game's project, under the version; empty when it is not known.</summary>
    [ObservableProperty]
    public partial string LastRelease { get; private set; } = "";

    /// <summary>Where the authors go, or that they were filled in from the site's project while they are its own.</summary>
    [ObservableProperty]
    public partial string AuthorsNote { get; private set; } = Strings.Creator_AuthorsHint;

    /// <summary>Why the metadata keeps the package from being built; empty when nothing does.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadataProblem))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    public partial string MetadataProblem { get; private set; } = "";

    public bool HasMetadataProblem => MetadataProblem.Length > 0;

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
    public partial bool NeedsComparison { get; private set; }

    /// <summary>Warnings only the build found, which the author accepts before building again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBuildFindings), nameof(AcceptedAll), nameof(ShowsBuild))]
    public partial IReadOnlyList<FindingLine> BuildFindings { get; private set; } = [];

    public bool HasBuildFindings => BuildFindings.Count > 0;

    /// <summary>The author accepted every warning of the last build, which the next build takes.</summary>
    public bool AcceptedAll => HasBuildFindings && BuildFindings.All(line => line.IsAccepted);

    /// <summary>The build's button, which a failure with warnings to accept replaces with the way through them.</summary>
    public bool ShowsBuild => IsReady && !HasBuildFindings;

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
    public partial string OutputPath { get; private set; } = "";

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
    [NotifyPropertyChangedFor(nameof(OffersTestInstall), nameof(IsTestInstalled), nameof(IsTestRunning), nameof(WasTested),
        nameof(CanRebuild))]
    public partial TestInstallState Test { get; private set; }

    public bool OffersTestInstall => Test is TestInstallState.None or TestInstallState.Failed;

    public bool IsTestInstalled => Test == TestInstallState.Installed;

    public bool IsTestRunning => Test is TestInstallState.Installing or TestInstallState.Undoing;

    public bool WasTested => Test == TestInstallState.Undone;

    /// <summary>The test install, or undoing it, failed, which <see cref="TestText"/> explains.</summary>
    [ObservableProperty]
    public partial bool HasTestProblem { get; private set; }

    public bool CanRebuild => Test is not (TestInstallState.Installed or TestInstallState.Installing or TestInstallState.Undoing);

    [ObservableProperty]
    public partial string TestTitle { get; private set; } = "";

    [ObservableProperty]
    public partial string TestText { get; private set; } = "";

    [ObservableProperty]
    public partial string TestProgress { get; private set; } = "";

    [ObservableProperty]
    public partial double? TestFraction { get; private set; }

    [ObservableProperty]
    public partial bool CanLaunch { get; private set; }

    [ObservableProperty]
    public partial string PublishText { get; private set; } = "";

    /// <summary>The project has changes the author has not saved, which the next steps remind of.</summary>
    [ObservableProperty]
    public partial bool ShowsSaveHint { get; private set; }

    /// <summary>The installed test package of the game; null while there is none.</summary>
    private InstallView? TestInstall =>
        _page.Game is { } game
            ? _page.Agent.State.Games.FirstOrDefault(view => view.Key == game.Key)?.Installs.FirstOrDefault(install => install.IsTest)
            : null;

    public void OnShown()
    {
        if (_session.Review?.Plan is { Format.Kind: PackageKind.Manifest } && _session.Project.Metadata.Title.Length == 0
            && _session.Project.Game is { } game)
        {
            _session.Describe(project => project with { Metadata = project.Metadata with { Title = game.Name } });
        }

        _ = HintAsync();
        Show();
        ShowTest();
    }

    public async Task<bool> CanLeaveAsync()
    {
        if (_building is null)
        {
            return true;
        }

        if (!await _page.Services.Dialogs.ConfirmAsync(new ConfirmDialogViewModel(Strings.Creator_StopBuildTitle, Strings.Creator_StopBuild,
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

    public void Dispose()
    {
        _building?.Cancel();
        _session.PropertyChanged -= OnSessionChanged;
        _page.Agent.PropertyChanged -= OnAgentChanged;
        _page.Agent.JobsProgressed -= OnJobsProgressed;
        _page.Agent.JobEnded -= OnJobEnded;
    }

    [RelayCommand]
    private void AddAuthor()
    {
        if (NewAuthor.Trim() is { Length: > 0 } author)
        {
            _session.Describe(project => project with { Metadata = project.Metadata with { Authors = project.Metadata.Authors.Add(author) } });
        }

        NewAuthor = "";
    }

    [RelayCommand]
    private void RemoveAuthor(string author) =>
        _session.Describe(project => project with
        {
            Metadata = project.Metadata with { Authors = project.Metadata.Authors.RemoveAll(existing => existing == author) },
        });

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private Task BuildAsync() => BuildToAsync(askWhere: false);

    private bool CanBuild() => MetadataProblem.Length == 0;

    [RelayCommand]
    private Task BuildElsewhereAsync() => BuildToAsync(askWhere: true);

    private async Task BuildToAsync(bool askWhere)
    {
        // A name or version typed a moment ago may not be planned yet, and the package takes them from the plan.
        await _session.SettledAsync().ConfigureAwait(true);
        if (_session.Review?.Plan is not { } plan || _page.Game is not { } game || HasMetadataProblem)
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
                OutputFolder()).ConfigureAwait(true);
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
    private async Task CompareAgainAsync()
    {
        _page.GoTo(CreatorStep.Files);
        if (_page.Content is FilesStepViewModel files)
        {
            await files.CompareAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void Reveal()
    {
        if (ResultFolder.Length > 0)
        {
            _page.Services.Links.OpenFolder(ResultFolder);
        }
    }

    [RelayCommand]
    private async Task InstallTestAsync()
    {
        if (_page.Game is not { } game || OutputPath.Length == 0)
        {
            return;
        }

        (_testNote, _undoJob) = ("", null);
        _ended.Clear();
        var (sent, answer) = await _page.AskAsync(new TestInstallPackage(game.Key, OutputPath)).ConfigureAwait(true);
        if (sent && answer is JobStarted started)
        {
            _testJob = started.JobId;
            ShowTest();
        }
    }

    [RelayCommand]
    private async Task UndoTestAsync()
    {
        if (TestInstall is not { } install)
        {
            return;
        }

        (_testNote, _testJob) = ("", null);
        _ended.Clear();
        var (sent, answer) = await _page.AskAsync(new UninstallProject(install.Id)).ConfigureAwait(true);
        if (sent && answer is JobStarted started)
        {
            _undoJob = started.JobId;
            ShowTest();
        }
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (_page.Game is { } game)
        {
            await _page.AskAsync(new LaunchGame(game.Key)).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void OpenSite()
    {
        if (PageLinks.Dashboard(_page.Services.Context.Site.SiteAddress) is { } dashboard)
        {
            _page.Services.Links.Open(dashboard);
        }
    }

    [RelayCommand]
    private void Back() => _page.GoTo(CreatorStep.Review);

    partial void OnPackageTitleChanged(string value) =>
        _session.Describe(project => project with { Metadata = project.Metadata with { Title = value } });

    partial void OnVersionChanged(string value) =>
        _session.Describe(project => project with { Metadata = project.Metadata with { Version = value } });

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
            if (failed.Failure is BuildFailure.WrongExtension or BuildFailure.OutputInGameFolder or BuildFailure.OutputInAuthorFolder)
            {
                ForgetTarget();
            }

            Fail(Strings.Creator_BuildFailedTitle, Text.Format(Strings.Creator_BuildFailed, ("failure", failed.Failure.ToString()),
                ("files", failed.Paths.Count), ("file", failed.Paths.Count > 0 ? failed.Paths[0] : "")));
        }
        catch (CreatorBlockedException blocked)
        {
            Fail(CreatorWording.BlockTitle(blocked.Blocks[0], game.Game.InstallDirectory),
                string.Join(' ', blocked.Blocks.Select(block => CreatorWording.Block(block, game.Game.InstallDirectory))));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ForgetTarget();
            Fail(Strings.Creator_BuildFailedTitle, Strings.Creator_BuildWriteFailed);
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
        var (_, answer) = await _page.AskAsync(new GetDetectedGames()).ConfigureAwait(true);
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
        SiteWarnings = [.. result.Findings.Where(finding => finding.Kind is FindingKind.LargerThanSiteTakes or FindingKind.FormatNotOnSite)
            .Select(finding => CreatorWording.Finding(finding))];
        if (Test is TestInstallState.Undone or TestInstallState.Failed)
        {
            Test = TestInstallState.None;
        }

        State = PackageState.Built;
        ShowTest();
    }

    private void Fail(string title, string text)
    {
        (ProblemTitle, ProblemText) = (title, text);
        State = OutputPath.Length > 0 ? PackageState.Built : PackageState.Ready;
    }

    // The next build asks for a place again rather than going back to one the build refused or could not write to.
    private void ForgetTarget() => _target = null;

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

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        var project = _session.Project;
        if (_game != (project.Game?.Key, project.GameFolder))
        {
            _game = (project.Game?.Key, project.GameFolder);
            Forget();
        }

        Show();
    }

    // A package and its test belong to the game it was built for: another copy of the game, e.g. from another store, has
    // other files, so its package is built anew, and a test install never takes the other copy's package.
    private void Forget()
    {
        (OutputPath, _target, ResultName, ResultSize, ResultDetails, ResultFolder, SiteWarnings) = ("", null, "", "", "", "", []);
        (ProblemTitle, ProblemText, NeedsComparison, _buildFindings, BuildFindings, BuildTarget) = ("", "", false, [], [], "");
        (_testJob, _undoJob, _lostTrack, _testNote, HasTestProblem) = (null, null, false, "", false);
        _ended.Clear();
        Test = TestInstallState.None;
        State = PackageState.Ready;
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IAgent.Link) when _page.Agent.Link != AgentLink.Connected:
                _lostTrack |= _testJob is not null || _undoJob is not null;
                break;
            case nameof(IAgent.Link) or nameof(IAgent.State):
                ShowTest();
                break;
        }
    }

    private void OnJobEnded(object? sender, JobEnded ended)
    {
        if (ended.Job.Game == _page.Game?.Key)
        {
            _ended[ended.Job.Id] = (ended.Outcome, ended.Failure);
            ShowTest();
        }
    }

    private void OnJobsProgressed(object? sender, IReadOnlyList<JobView> jobs)
    {
        if (jobs.FirstOrDefault(job => job.Id == _testJob || job.Id == _undoJob) is { } job)
        {
            ShowJob(job);
        }
    }

    private void Show()
    {
        ShowBuildFindings();
        if (_session.Review?.Plan is not { } plan)
        {
            return;
        }

        var project = _session.Project;
        var format = plan.Format;
        IsManifest = format.Kind == PackageKind.Manifest;
        var extension = _page.Options.PackageExtension;
        FormatTitle = IsManifest ? Text.Format(Strings.Creator_ManifestTitle, ("extension", extension)) : Strings.Creator_ZipTitle;
        FormatText = string.Join(' ',
            [.. format.Reasons.Select(reason => Text.Format(Strings.Creator_FormatReason, ("reason", reason.ToString()))),
            IsManifest ? Strings.Creator_ManifestOutcome : Strings.Creator_ZipOutcome]);
        FormatRows =
        [
            new FormatRow(PackageLayout.ZipExtension, Strings.Creator_ZipRule, !IsManifest),
            new FormatRow(extension, Strings.Creator_ManifestRule, IsManifest),
        ];
        Lead = IsManifest ? Strings.Creator_PackageLead : Strings.Creator_ZipLead;
        ShowMetadata(project.Metadata);
        MetadataProblem = string.Join(' ', plan.Findings.Where(finding => ReviewStepViewModel.IsMetadata(finding.Kind))
            .Select(finding => CreatorWording.Finding(finding)));
        if (_page.Game is { } game)
        {
            GameName = game.Name;
            GameStore = game.Key.Store.Value;
            GameDetails = GameWording.Join(
            [
                GameWording.StoreName(game.Key.Store),
                game.Game.BuildId is { Length: > 0 } build ? Text.Format(Strings.Creator_GameBuild, ("build", build)) : "",
            ]);
            var name = SuggestedName(game, _page.Options.ExtensionFor(format.Kind));
            ReusesTarget = _target is { } target && target.Name == name;
            BuildHint = ReusesTarget
                ? Text.Format(Strings.Creator_BuildTo, ("name", Path.GetFileName(_target!.Value.Path)))
                : Text.Format(Strings.Creator_BuildHint, ("name", name));
        }

        ShowsSaveHint = _session.IsDirty || _session.Path is null;
        PublishText = IsManifest ? Strings.Creator_PublishManifest : Strings.Creator_PublishZip;
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

    private void ShowMetadata(PackageMetadata metadata)
    {
        if (PackageTitle != metadata.Title)
        {
            PackageTitle = metadata.Title;
        }

        if (Version != metadata.Version)
        {
            Version = metadata.Version;
        }

        if (!Authors.SequenceEqual(metadata.Authors))
        {
            Authors.Clear();
            foreach (var author in metadata.Authors)
            {
                Authors.Add(author);
            }
        }

        LastRelease = _hints is { Release.Length: > 0 } hints
            ? Text.Format(Strings.Creator_LastRelease, ("version", GameWording.ShortVersion(hints.Release)))
            : "";
        AuthorsNote = _hints is { Authors.Count: > 0 } project && Authors.SequenceEqual(project.Authors)
            ? Text.Format(Strings.Creator_AuthorsFromProject, ("project", project.Project))
            : Strings.Creator_AuthorsHint;
    }

    // The site's project gives the hints only when the creator can tell it is the author's without guessing: the project
    // installed on the game, else the game's only one. They fill in only what the author left empty.
    private async Task HintAsync()
    {
        if (_page.Game is not { } game || _hintsFor == game.Key)
        {
            return;
        }

        (_hintsFor, _hints) = (game.Key, null);
        var installed = _page.Agent.State.Games.FirstOrDefault(view => view.Key == game.Key)?.Installs
            .Select(install => install.Project).OfType<ProjectOfferView>().FirstOrDefault();
        var project = installed is not null ? new SiteProjectView(installed.ProjectId, installed.GameSlug, installed.Team.Slug)
            : game.Projects is [var only] ? only
            : null;
        if (project is null)
        {
            return;
        }

        var answer = await _page.AskQuietlyAsync(new GetProjectPage(project.GameSlug, project.TeamSlug, project.ProjectId))
            .ConfigureAwait(true);
        var hints = answer is ProjectPageResult { Page: { } page } ? await Task.Run(() => ProjectHints.Read(page)).ConfigureAwait(true) : null;
        if (_page.Game?.Key != game.Key)
        {
            return;
        }

        if (hints is null)
        {
            _hintsFor = null;
            return;
        }

        _hints = hints;
        _session.Describe(current => current with
        {
            Metadata = current.Metadata with
            {
                Version = current.Metadata.Version.Trim().Length == 0 ? hints.NextVersion : current.Metadata.Version,
                Authors = current.Metadata.Authors.Count == 0 ? [.. hints.Authors] : current.Metadata.Authors,
            },
        });
        Show();
    }

    // The agent says how each job ended, which the state cannot for a job that came and went between two states, or for
    // the failure of a game the player's list leaves out; the state tells when the install the job left is there or gone.
    private void ShowTest()
    {
        var installed = TestInstall;
        FillLostEnds(installed);
        var running = RunningTestJob(installed);
        if (_testJob is { } test)
        {
            Test = EndTest(test, installed);
        }
        else if (_undoJob is { } undo)
        {
            Test = EndUndo(undo, installed);
        }
        else
        {
            Test = running is not null ? (running.Kind == JobKind.Uninstall ? TestInstallState.Undoing : TestInstallState.Installing)
                : installed is not null ? TestInstallState.Installed
                : Test is TestInstallState.Undone or TestInstallState.Failed ? Test
                : TestInstallState.None;
        }

        if ((_page.Agent.State.Jobs.FirstOrDefault(job => job.Id == _testJob || job.Id == _undoJob) ?? running) is { } job)
        {
            ShowJob(job);
        }

        var name = _page.Game?.Name ?? "";
        CanLaunch = _page.Game?.Game.CanLaunch == true;
        TestTitle = Test switch
        {
            TestInstallState.Installed => Text.Format(Strings.Creator_TestInstalledTitle, ("game", name)),
            TestInstallState.Undone => Strings.Creator_TestedTitle,
            TestInstallState.Failed => Strings.Creator_TestFailedTitle,
            _ => Strings.Creator_TestTitle,
        };
        HasTestProblem = Test is TestInstallState.Failed || (Test is TestInstallState.Installed && _testNote.Length > 0);
        TestText = Test switch
        {
            TestInstallState.Failed or TestInstallState.Undone => _testNote,
            TestInstallState.Installed when _testNote.Length > 0 => _testNote,
            TestInstallState.Installed => Strings.Creator_TestInstalled,
            _ => Text.Format(Strings.Creator_TestText, ("game", name)),
        };
    }

    // The game's page can install a test package or undo it too, and the step shows that job as its own.
    private JobView? RunningTestJob(InstallView? installed) =>
        _page.Game is { } game
            ? _page.Agent.State.Jobs.FirstOrDefault(job => job.Game == game.Key && !_ended.ContainsKey(job.Id)
                && (job.Kind == JobKind.TestInstall || (job.Kind == JobKind.Uninstall && job.InstallId == installed?.Id)))
            : null;

    // The job's end may come before the state that lists its install, which undoing it needs, so a job that worked waits
    // for that state.
    private TestInstallState EndTest(Guid job, InstallView? installed)
    {
        var ended = _ended.TryGetValue(job, out var end);
        if (ended && end.Outcome != JobOutcome.Succeeded)
        {
            _testJob = null;
            _testNote = end.Outcome == JobOutcome.Failed ? FailureText(end.Failure) : "";
            return end.Outcome == JobOutcome.Failed ? TestInstallState.Failed : TestInstallState.None;
        }

        if (installed is null || (!ended && IsListed(job)))
        {
            return TestInstallState.Installing;
        }

        _testJob = null;
        return TestInstallState.Installed;
    }

    // The job's end may come before the state without the install, which would offer to undo it again, so a job that
    // worked waits for that state.
    private TestInstallState EndUndo(Guid job, InstallView? installed)
    {
        var ended = _ended.TryGetValue(job, out var end);
        if (ended && end.Outcome != JobOutcome.Succeeded)
        {
            _undoJob = null;
            _testNote = end.Outcome == JobOutcome.Failed
                ? Text.Format(Strings.Creator_UndoTestFailed, ("reason", FailureText(end.Failure)))
                : "";
            return installed is not null ? TestInstallState.Installed : TestInstallState.None;
        }

        if (installed is not null || (!ended && IsListed(job)))
        {
            return TestInstallState.Undoing;
        }

        _undoJob = null;
        _testNote = Text.Format(Strings.Creator_TestUndoneAt, ("when", Formats.RelativeTime(_page.Services.Clock.GetUtcNow(),
            _page.Services.Clock)));
        _page.Services.Toasts.Show(new ToastViewModel(ToastTone.Success, Icons.CircleCheck, Strings.Creator_TestUndoneTitle,
            Text.Format(Strings.Creator_TestUndone, ("game", _page.Game?.Name ?? ""))));
        return TestInstallState.Undone;
    }

    // A job that ended while the agent was out of reach was never reported; once a state no longer lists it, what the job
    // left tells how it went, though not why it failed.
    private void FillLostEnds(InstallView? installed)
    {
        if (!_lostTrack || _page.Agent.Link != AgentLink.Connected)
        {
            return;
        }

        foreach (var (job, succeeded) in new[] { (_testJob, installed is not null), (_undoJob, installed is null) })
        {
            if (job is { } id && !_ended.ContainsKey(id) && !IsListed(id))
            {
                _ended[id] = (succeeded ? JobOutcome.Succeeded : JobOutcome.Failed, null);
            }
        }

        _lostTrack = new[] { _testJob, _undoJob }.Any(job => job is { } id && !_ended.ContainsKey(id));
    }

    private bool IsListed(Guid job) => _page.Agent.State.Jobs.Any(listed => listed.Id == job);

    private string FailureText(JobFailureView? failure) =>
        failure is null || _page.Game is not { } game
            ? Strings.Creator_TestFailed
            : GameWording.FailureReason(failure, game.Key.Store, game.Name);

    private void ShowJob(JobView job)
    {
        var line = GameLines.Job(job, job.Game.Store);
        TestProgress = line.Label;
        TestFraction = job.Fraction;
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

    private string? OutputFolder() =>
        _target is { } target ? Path.GetDirectoryName(target.Path)
        : _session.Path is { } project ? Path.GetDirectoryName(project)
        : Path.GetDirectoryName(_session.Project.AuthorFolder);

    private static bool Makes(PackagePlan plan, PlannedOperation? operation) =>
        operation is null || plan.Operations.Any(planned => planned.Kind == operation.Kind
            && PackagePath.Comparer.Equals(planned.Path, operation.Path) && planned.Original == operation.Original
            && planned.SourceFile == operation.SourceFile);

    private static string ReviewLine(PlanFinding finding) =>
        ReviewStepViewModel.IsHosting(finding.Kind) ? CreatorWording.RuleTitle(finding) : CreatorWording.Finding(finding);
}
