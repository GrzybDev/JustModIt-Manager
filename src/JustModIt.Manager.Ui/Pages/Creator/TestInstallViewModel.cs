using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>A test install of the built package through the agent, which the agent also undoes.</summary>
public sealed partial class TestInstallViewModel : ObservableObject, IDisposable
{
    private readonly ProjectPageViewModel _page;

    private readonly Dictionary<Guid, (JobOutcome Outcome, JobFailureView? Failure, IReadOnlyList<KeptFilesView>? Kept)> _ended = [];
    private Guid? _testJob;
    private Guid? _undoJob;
    private bool _lostTrack;
    private string _testNote = "";
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

    /// <summary>The installed test package of the game; null while there is none.</summary>
    private InstallView? TestInstall =>
        _page.Game is { } game
            ? _page.Agent.State.Games.FirstOrDefault(view => view.Key == game.Key)?.Installs.FirstOrDefault(install => install.IsTest)
            : null;

    public void Dispose()
    {
        _page.Agent.PropertyChanged -= OnAgentChanged;
        _page.Agent.JobsProgressed -= OnJobsProgressed;
        _page.Agent.JobEnded -= OnJobEnded;
    }

    [RelayCommand]
    private Task InstallTestAsync(string package) => TestAsync([package]);

    // An add-on is tried together with the package it goes with, which installs first, so a clash between the two shows
    // before players meet it.
    [RelayCommand]
    private async Task InstallTestTogetherAsync(string package)
    {
        if (package.Length == 0)
        {
            return;
        }

        var first = await _page.Services.Pickers.PickFileAsync(Strings.Creator_TestTogetherTitle, startIn: Path.GetDirectoryName(package)).ConfigureAwait(true);
        if (first is not null && !string.Equals(first, package, StringComparison.OrdinalIgnoreCase))
        {
            await TestAsync([first, package]).ConfigureAwait(true);
        }
    }

    private async Task TestAsync(IReadOnlyList<string> packages)
    {
        if (_page.Game is not { } game || packages[^1].Length == 0)
        {
            return;
        }

        (_testNote, _undoJob) = ("", null);
        _ended.Clear();
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
        if (_page.Game is { } game)
        {
            await AgentRequests.AskAsync(_page.Services, new LaunchGame(game.Key)).ConfigureAwait(true);
        }
    }

    /// <summary>Starts over for a new package, unless a test is under way or its package is in the game.</summary>
    internal void Built()
    {
        if (State is TestInstallState.Undone or TestInstallState.Failed)
        {
            State = TestInstallState.None;
        }

        ShowTest();
    }

    /// <summary>Forgets the test, which belongs to another copy of the game.</summary>
    internal void Forget()
    {
        (_testJob, _undoJob, _undone, _lostTrack, _testNote, HasTestProblem) = (null, null, null, false, "", false);
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

        var name = _page.Game?.Name ?? "";
        CanLaunch = _page.Game?.Game.CanLaunch == true;
        TestTitle = State switch
        {
            TestInstallState.Installed => Text.Format(Strings.Creator_TestInstalledTitle, ("game", name)),
            TestInstallState.Undone => Strings.Creator_TestedTitle,
            TestInstallState.Failed => Strings.Creator_TestFailedTitle,
            _ => Strings.Creator_TestTitle,
        };
        HasTestProblem = State is TestInstallState.Failed || (State is TestInstallState.Installed && _testNote.Length > 0);
        TestText = State switch
        {
            TestInstallState.Failed or TestInstallState.Undone => _testNote,
            TestInstallState.Installed when _testNote.Length > 0 => _testNote,
            TestInstallState.Installed => Strings.Creator_TestInstalled,
            _ => Text.Format(Strings.Creator_TestText, ("game", name)),
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
        if (ended.Job.Game == _page.Game?.Key)
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
        var game = _page.Game?.Name ?? "";
        if (KeptReasons.MostPressing(kept?.Select(files => files.Reason) ?? []) is not { } reason)
        {
            return new ToastViewModel(ToastTone.Success, Icons.CircleCheck, Strings.Creator_TestUndoneTitle,
                Text.Format(Strings.Creator_TestUndone, ("game", game)));
        }

        var told = KeptReasons.CaseOf(reason);
        var text = Text.Format(Strings.Creator_TestUndoneKept, ("game", game), ("reason", told),
            ("count", kept!.Where(files => KeptReasons.CaseOf(files.Reason) == told).Sum(files => files.Count)),
            ("store", _page.Game is { } detected ? GameWording.StoreName(detected.Key.Store) : ""));
        return reason == KeptReason.OriginalUnknown
            ? new ToastViewModel(ToastTone.Info, Icons.Info, Strings.Creator_TestUndoneTitle, text)
            : new ToastViewModel(ToastTone.Warning, Icons.TriangleAlert, Strings.Creator_TestUndoneTitle, text);
    }

    // The game's page can install a test package or undo it too, and the step shows that job as its own.
    private JobView? RunningTestJob(InstallView? installed) =>
        _page.Game is { } game
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

    private string FailureText(JobFailureView? failure) =>
        failure is null || _page.Game is not { } game
            ? Strings.Creator_TestFailed
            : GameWording.FailureReason(failure, game.Key.Store, game.Name);

    private void ShowJob(JobView job)
    {
        var line = GameLines.Job(job);
        TestProgress = line.Label;
        TestFraction = job.Fraction;
    }
}
