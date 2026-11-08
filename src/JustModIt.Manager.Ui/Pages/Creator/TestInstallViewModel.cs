using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Creator;

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

/// <summary>An add-on a test installs after the package it goes with, unless the author turns it off.</summary>
/// <param name="part">The add-on's name.</param>
/// <param name="changed">Told when the author turns it on or off.</param>
public sealed partial class TestFeature(string part, Action changed) : ObservableObject
{
    public string Part { get; } = part;

    [ObservableProperty]
    public partial bool IsOn { get; set; } = true;

    partial void OnIsOnChanged(bool value) => changed();
}

/// <summary>A test install of the built packages through the agent, which the agent also undoes.</summary>
public sealed partial class TestInstallViewModel : ObservableObject, IDisposable
{
    private readonly ProjectPageViewModel _page;

    private readonly Dictionary<Guid, (JobOutcome Outcome, JobFailureView? Failure, IReadOnlyList<KeptFilesView>? Kept)> _ended = [];
    private Guid? _testJob;
    private Guid? _undoJob;
    private GameKey? _version;
    private bool _lostTrack;
    private string _testNote = "";
    private int _sent;
    private (Guid Job, ToastViewModel Toast)? _undone;

    /// <summary>Creates the test install of a project's package.</summary>
    /// <param name="page">The project's page.</param>
    public TestInstallViewModel(ProjectPageViewModel page)
    {
        _page = page;
        _page.Agent.PropertyChanged += OnAgentChanged;
        _page.Agent.JobsProgressed += OnJobsProgressed;
        _page.Agent.JobEnded += OnJobEnded;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffersTestInstall), nameof(IsTestInstalled), nameof(IsTestRunning), nameof(WasTested),
        nameof(CanRebuild))]
    public partial TestInstallState State { get; private set; }

    public bool OffersTestInstall => State is TestInstallState.None or TestInstallState.Failed;

    public bool IsTestInstalled => State == TestInstallState.Installed;

    public bool IsTestRunning => State is TestInstallState.Installing or TestInstallState.Undoing;

    public bool WasTested => State == TestInstallState.Undone;

    /// <summary>The test install, or undoing it, failed, which <see cref="TestText"/> explains.</summary>
    [ObservableProperty]
    public partial bool HasTestProblem { get; private set; }

    public bool CanRebuild => State is not (TestInstallState.Installed or TestInstallState.Installing or TestInstallState.Undoing);

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

    /// <summary>The add-ons the last run built, each of which the test installs unless the author turns it off.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTestFeatures))]
    public partial IReadOnlyList<TestFeature> TestFeatures { get; private set; } = [];

    public bool HasTestFeatures => TestFeatures.Count > 0;

    /// <summary>The installed test package of the game; null while there is none.</summary>
    private InstallView? TestInstall => TestOf(Game);

    /// <summary>
    /// The game of the version under test: the one tried last, else one that holds a test package, else the chosen cell's.
    /// </summary>
    private DetectedGameEntry? Game => _version is { } version ? _page.GameOf(version)
        : _page.Cells.Select(cell => cell.Game).FirstOrDefault(game => TestOf(game) is not null) ?? _page.Game;

    public void Dispose()
    {
        _page.Agent.PropertyChanged -= OnAgentChanged;
        _page.Agent.JobsProgressed -= OnJobsProgressed;
        _page.Agent.JobEnded -= OnJobEnded;
    }

    [RelayCommand]
    private async Task InstallTestAsync()
    {
        if (await ChooseVersionAsync().ConfigureAwait(true) is (true, var version))
        {
            await TestAsync(version, []).ConfigureAwait(true);
        }
    }

    // A run for several versions made a base for each, of which the author tries one in its own copy of the game; no version means
    // the chosen cell's.
    private async Task<(bool Chosen, GameKey? Version)> ChooseVersionAsync()
    {
        List<(GameKey Version, string Path)> choices = [.. _page.Build.Packages.Where(row => row.Part.Length == 0 && !row.IsMissing)
            .SelectMany(row => row.Versions.Select(version => (version, row.Path)))
            .Where(choice => _page.GameOf(choice.version) is not null)];
        var version = choices is [var only] ? only.Version : (GameKey?)null;
        if (choices.Count > 1)
        {
            var answer = await _page.Services.Dialogs.ShowAsync(new ChoiceDialogViewModel(Strings.Creator_TestInstall, "",
            [
                .. choices.Select(choice => new DialogChoice(choice.Version.ToString(), Icons.Download,
                    Text.Format(Strings.Creator_TestOn, ("store", GameWording.StoreName(choice.Version.Store))), Path.GetFileName(choice.Path))),
            ])).ConfigureAwait(true);
            if (choices.FirstOrDefault(choice => choice.Version.ToString() == answer.Key) is not { Path: not null } chosen)
            {
                return (false, null);
            }

            version = chosen.Version;
        }

        return (true, version);
    }

    // An add-on is tried together with the package it goes with, which installs first, so a clash between the two shows
    // before players meet it.
    [RelayCommand]
    private async Task InstallTestTogetherAsync()
    {
        if (_page.Build.OutputPath.Length == 0)
        {
            return;
        }

        var first = await _page.Services.Pickers.PickFileAsync(Strings.Creator_TestTogetherTitle, startIn: _page.Build.ResultFolder).ConfigureAwait(true);
        if (first is not null && !_page.Build.Packages.Any(row => string.Equals(first, row.Path, StringComparison.OrdinalIgnoreCase))
            && await ChooseVersionAsync().ConfigureAwait(true) is (true, var version))
        {
            await TestAsync(version, [first]).ConfigureAwait(true);
        }
    }

    // The version's base, then each add-on left on, in the order a player's install takes them.
    private async Task TestAsync(GameKey? version, IReadOnlyList<string> first)
    {
        if ((version ?? _page.Cell?.Key.Version) is not { } tested || _page.GameOf(tested) is not { } game)
        {
            return;
        }

        List<string> packages = [.. first, .. _page.Build.Packages
            .Where(row => !row.IsMissing && row.Versions.Contains(tested)
                && (row.Part.Length == 0 || TestFeatures.Any(feature => feature.IsOn && feature.Part == row.Part)))
            .Select(row => row.Path)];
        if (packages.Count == first.Count)
        {
            return;
        }

        (_testNote, _undoJob, _version) = ("", null, tested);
        _ended.Clear();
        _sent = packages.Count;
        var (sent, answer) = await AgentRequests.AskAsync(_page.Services, new TestInstallPackage(game.Key, packages)).ConfigureAwait(true);
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
        var (sent, answer) = await AgentRequests.AskAsync(_page.Services, new UninstallProject(install.Id)).ConfigureAwait(true);
        if (sent && answer is JobStarted started)
        {
            _undoJob = started.JobId;
            ShowTest();
        }
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (Game is { } game)
        {
            await AgentRequests.AskAsync(_page.Services, new LaunchGame(game.Key)).ConfigureAwait(true);
        }
    }

    /// <summary>Starts over for new packages, unless a test is under way or its package is in the game.</summary>
    internal void Built()
    {
        if (State is TestInstallState.Undone or TestInstallState.Failed)
        {
            State = TestInstallState.None;
        }

        TestFeatures = [.. _page.Build.Packages.Where(row => row.Part.Length > 0 && !row.IsMissing).Select(row => row.Part).Distinct()
            .Select(part => new TestFeature(part, ShowTest))];

        ShowTest();
    }

    /// <summary>Forgets the test once the version it was made on changed or left, since it belongs to that copy of the game.</summary>
    /// <param name="versions">The versions whose game or folder changed or that left the project.</param>
    internal void Forget(IReadOnlyList<GameKey> versions)
    {
        if (versions.Count == 0 || (_version is { } tested && !versions.Contains(tested)))
        {
            return;
        }

        (_testJob, _undoJob, _undone, _lostTrack, _testNote, HasTestProblem, _version) = (null, null, null, false, "", false, null);
        _ended.Clear();
        State = TestInstallState.None;
    }

    /// <summary>Shows where the test install stands.</summary>
    internal void ShowTest()
    {
        // The agent says how each job ended, which the state cannot for a job that came and went between two states, or for
        // the failure of a game the player's list leaves out; the state tells when the install the job left is there or gone.
        var installed = TestInstall;
        FillLostEnds(installed);
        var running = RunningTestJob(installed);
        if (_testJob is { } test)
        {
            State = EndTest(test, installed);
        }
        else if (_undoJob is { } undo)
        {
            State = EndUndo(undo, installed);
        }
        else
        {
            State = running is not null ? (running.Kind == JobKind.Uninstall ? TestInstallState.Undoing : TestInstallState.Installing)
                : installed is not null ? TestInstallState.Installed
                : State is TestInstallState.Undone or TestInstallState.Failed ? State
                : TestInstallState.None;
        }

        if ((_page.Agent.State.Jobs.FirstOrDefault(job => job.Id == _testJob || job.Id == _undoJob) ?? running) is { } job)
        {
            ShowJob(job);
        }

        var name = Game?.Name ?? "";
        CanLaunch = Game?.Game.CanLaunch == true;
        // The test installs, or installed, the packages it sent, else those the pills leave on.
        var several = (installed?.PackageFileNames.Count ?? (OffersTestInstall ? 1 + TestFeatures.Count(feature => feature.IsOn) : _sent)) > 1 ? "true" : "false";
        TestTitle = State switch
        {
            TestInstallState.Installed => Text.Format(Strings.Creator_TestInstalledTitle, ("game", name), ("several", several)),
            TestInstallState.Undone => Strings.Creator_TestedTitle,
            TestInstallState.Failed => Strings.Creator_TestFailedTitle,
            _ => Text.Format(Strings.Creator_TestTitle, ("several", several)),
        };
        HasTestProblem = State is TestInstallState.Failed || (State is TestInstallState.Installed && _testNote.Length > 0);
        TestText = State switch
        {
            TestInstallState.Failed or TestInstallState.Undone => _testNote,
            TestInstallState.Installed when _testNote.Length > 0 => _testNote,
            TestInstallState.Installed => Strings.Creator_TestInstalled,
            _ => Text.Format(Strings.Creator_TestText, ("game", name), ("several", several)),
        };
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
        if (ended.Job.Game == Game?.Key)
        {
            _ended[ended.Job.Id] = (ended.Outcome, ended.Failure, ended.KeptFiles);
            // The state may have ended the undo first, before the agent told which files it left.
            if (_undone is { } undone && undone.Job == ended.Job.Id && ended.KeptFiles is { Count: > 0 } kept)
            {
                _page.Services.Toasts.Dismiss(undone.Toast);
                _undone = (undone.Job, UndoneToast(kept));
                _page.Services.Toasts.Show(_undone.Value.Toast);
            }

            ShowTest();
            // An undone test may have left files as the package's software wrote them, which the plan has to know.
            if (ended.Job.Kind == JobKind.Uninstall)
            {
                _ = _page.ReadKeptPathsAsync();
            }
        }
    }

    private void OnJobsProgressed(object? sender, IReadOnlyList<JobView> jobs)
    {
        if (jobs.FirstOrDefault(job => job.Id == _testJob || job.Id == _undoJob) is { } job)
        {
            ShowJob(job);
        }
    }

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
        _undone = (job, UndoneToast(ended ? end.Kept : null));
        _page.Services.Toasts.Show(_undone.Value.Toast);
        return TestInstallState.Undone;
    }

    // Says which files the undo left as the package's software changed them, by the most pressing reason.
    private ToastViewModel UndoneToast(IReadOnlyList<KeptFilesView>? kept)
    {
        var game = Game?.Name ?? "";
        if (KeptReasons.MostPressing(kept?.Select(files => files.Reason) ?? []) is not { } reason)
        {
            return new ToastViewModel(ToastTone.Success, Icons.CircleCheck, Strings.Creator_TestUndoneTitle,
                Text.Format(Strings.Creator_TestUndone, ("game", game)));
        }

        var told = KeptReasons.CaseOf(reason);
        var text = Text.Format(Strings.Creator_TestUndoneKept, ("game", game), ("reason", told),
            ("count", kept!.Where(files => KeptReasons.CaseOf(files.Reason) == told).Sum(files => files.Count)),
            ("store", Game is { } detected ? GameWording.StoreName(detected.Key.Store) : ""));
        return reason == KeptReason.OriginalUnknown
            ? new ToastViewModel(ToastTone.Info, Icons.Info, Strings.Creator_TestUndoneTitle, text)
            : new ToastViewModel(ToastTone.Warning, Icons.TriangleAlert, Strings.Creator_TestUndoneTitle, text);
    }

    // The game's page can install a test package or undo it too, and the step shows that job as its own.
    private JobView? RunningTestJob(InstallView? installed) =>
        Game is { } game
            ? _page.Agent.State.Jobs.FirstOrDefault(job => job.Game == game.Key && !_ended.ContainsKey(job.Id)
                && (job.Kind == JobKind.TestInstall || (job.Kind == JobKind.Uninstall && job.InstallId == installed?.Id)))
            : null;

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
                _ended[id] = (succeeded ? JobOutcome.Succeeded : JobOutcome.Failed, null, null);
            }
        }

        _lostTrack = new[] { _testJob, _undoJob }.Any(job => job is { } id && !_ended.ContainsKey(id));
    }

    private bool IsListed(Guid job) => _page.Agent.State.Jobs.Any(listed => listed.Id == job);

    private InstallView? TestOf(DetectedGameEntry? game) =>
        game is null ? null : _page.Agent.State.Games.FirstOrDefault(view => view.Key == game.Key)?.Installs.FirstOrDefault(install => install.IsTest);

    private string FailureText(JobFailureView? failure) =>
        failure is null || Game is not { } game
            ? Strings.Creator_TestFailed
            : GameWording.FailureReason(failure, game.Key.Store, game.Name);

    private void ShowJob(JobView job)
    {
        var line = GameLines.Job(job);
        TestProgress = line.Label;
        TestFraction = job.Fraction;
    }
}
