using System.ComponentModel;
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

// How the jobs of the test and its undoing ended, as the agent's events and states tell it.
public sealed partial class TestInstallViewModel
{
    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IAgent.Link) when _page.Creator.Agent.Link != AgentLink.Connected:
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
            ? _page.Creator.Agent.State.Jobs.FirstOrDefault(job => job.Game == game.Key && !_ended.ContainsKey(job.Id)
                && (job.Kind == JobKind.TestInstall || (job.Kind == JobKind.Uninstall && job.InstallId == installed?.Id)))
            : null;

    // A job that ended while the agent was out of reach was never reported; once a state no longer lists it, what the job
    // left tells how it went, though not why it failed.
    private void FillLostEnds(InstallView? installed)
    {
        if (!_lostTrack || _page.Creator.Agent.Link != AgentLink.Connected)
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

    private bool IsListed(Guid job) => _page.Creator.Agent.State.Jobs.Any(listed => listed.Id == job);

    private InstallView? TestOf(DetectedGameEntry? game) =>
        game is null ? null : _page.Creator.Agent.State.Games.FirstOrDefault(view => view.Key == game.Key)?.Installs.FirstOrDefault(install => install.IsTest);

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
