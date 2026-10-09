using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Core.Automation;

/// <summary>The jobs an evaluation started and what it has to tell the player.</summary>
/// <param name="Started">The automatic jobs now running.</param>
/// <param name="Notices">What to tell the player, at most one notice per evaluation.</param>
internal sealed record AutomationStep(IReadOnlyList<JobHandle> Started, IReadOnlyList<AutomationNotice> Notices);

/// <summary>
/// Carries out the automation's decisions and learns from how jobs end; it runs on the state coordinator's loop, like the
/// state it reads, so it needs no lock.
/// </summary>
internal sealed class AutomationDriver : IAsyncDisposable
{
    private readonly JobRunner _jobs;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;
    private readonly LatestDocumentWriter<AutomationHistory> _writer;
    private readonly Dictionary<Guid, Attempt> _running = [];
    private readonly HashSet<GameKey> _awaitingStore = [];

    /// <summary>Creates the driver with an empty history until <see cref="Load"/>.</summary>
    /// <param name="jobs">Starts the automatic jobs.</param>
    /// <param name="settings">Takes the per-game preferences learned from the player's own jobs.</param>
    /// <param name="history">Where the history is saved, by a single background writer.</param>
    /// <param name="time">The clock for attempts and retries.</param>
    /// <param name="logger">Logs a history that cannot be saved.</param>
    public AutomationDriver(JobRunner jobs, ISettingsService settings, IDocumentStore<AutomationHistory> history, TimeProvider time,
        ILogger logger)
    {
        _jobs = jobs;
        _settings = settings;
        _time = time;
        _writer = new LatestDocumentWriter<AutomationHistory>(history, "automation history", logger);
    }

    public AutomationHistory History { get; private set; } = AutomationHistory.Empty;

    /// <summary>Starts from the saved history.</summary>
    /// <param name="history">The saved history; null when there is none.</param>
    public void Load(AutomationHistory? history) => History = (history ?? AutomationHistory.Empty).Tidy(_time.GetUtcNow());

    /// <summary>A game whose automatic job met a busy store is looked at again once the library changes.</summary>
    public void OnLibraryChanged() => _awaitingStore.Clear();

    /// <summary>
    /// When the next attempt held back, after a failure or a success, may be tried again, so the app looks then even if
    /// nothing else changes; null when none waits for the clock.
    /// </summary>
    /// <param name="now">The current time.</param>
    public DateTimeOffset? NextRetryAt(DateTimeOffset now) =>
        History.Attempts.Values.Select(attempt => attempt.At + AutomationHistory.RetryAfter)
            .Where(retry => retry > now).Cast<DateTimeOffset?>().Min();

    /// <summary>Starts the jobs the automation decides on and gathers what to announce.</summary>
    /// <param name="games">The player's games as the state shows them.</param>
    /// <param name="settings">The app's settings.</param>
    public AutomationStep Evaluate(IReadOnlyList<MyGame> games, AppSettings settings)
    {
        var before = History;
        var now = _time.GetUtcNow();
        var started = new List<JobHandle>();
        var announced = new List<(MyGame Game, AutomationDecision Decision)>();
        History = History.KeepRepairAnnouncementsOf(
            games.SelectMany(game => game.Installs).Select(install => install.Record.Id).ToHashSet());
        foreach (var game in games.Where(game => !_awaitingStore.Contains(game.Key)))
        {
            // A game has one job at a time, so whatever else it needs waits for the next look.
            var tried = false;
            foreach (var decision in AutomationPolicy.Decide(game, settings, History, now))
            {
                if (decision.Action == AutomationAction.Announce)
                {
                    History = decision is { Reason: AutomationReason.ReapplyOff, Install: { } broken }
                        ? History.RecordRepairAnnouncement(broken.Record.Id, game.Game?.BuildId)
                        : decision.Announces.Aggregate(History, (history, release) => history.RecordAnnouncement(game.Key, release));
                    announced.Add((game, decision));
                }
                else if (decision.StartsJob && !tried && RequestFor(game, decision) is { } request)
                {
                    tried = true;
                    var handle = _jobs.Start(request);
                    // Another job runs on the game, or the app is exiting; the decision stands for the next look.
                    if (handle.Completion.IsCompleted)
                    {
                        continue;
                    }

                    History = History.RecordAttempt(game.Key, decision.ReleaseId, decision.Action, now);
                    var install = decision.Install?.Record.Id;
                    _running[handle.Job.Id] = new Attempt(game.Key, game.Name, decision.ReleaseId, decision.Action, game.Game?.BuildId,
                        install, install is { } id ? AutomationNotice.ProjectNameFor(game, id) : null);
                    started.Add(handle);
                }
            }
        }

        var notices = new List<AutomationNotice>();
        // What is on offer when the player first lets the app look is one piece of news, not one per game.
        if (History.BaselineAt is null)
        {
            History = History.WithBaseline(now);
            if (announced.Count > 0)
            {
                notices.Add(Summary(announced));
            }
        }
        else if (announced.Count == 1)
        {
            notices.Add(Announcement(announced[0].Game, announced[0].Decision));
        }
        else if (announced.Count > 1)
        {
            notices.Add(Summary(announced));
        }

        SaveIfChanged(before);
        return new AutomationStep(started, notices);
    }

    /// <summary>Learns from how a job ended, and returns what to tell the player about it.</summary>
    /// <param name="result">How the job ended.</param>
    /// <param name="game">The game as it was before the job's outcome was applied.</param>
    public IReadOnlyList<AutomationNotice> OnFinished(JobResult result, MyGame? game)
    {
        ArgumentNullException.ThrowIfNull(result);
        var before = History;
        var notices = new List<AutomationNotice>();
        if (_running.Remove(result.Job.Id, out var attempt))
        {
            Follow(result, attempt, notices);
        }
        else if (result.Job is { Origin: JobOrigin.User, Kind: JobKind.Install or JobKind.Uninstall or JobKind.Update or JobKind.Repair })
        {
            Learn(result, game);
        }

        SaveIfChanged(before);
        return notices;
    }

    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    private static JobRequest? RequestFor(MyGame game, AutomationDecision decision) => decision.Action switch
    {
        AutomationAction.Install when game.Project is { } project =>
            new InstallJobRequest(game.Key, game.Name, project.Project.Locator, decision.Variant?.Name, JobOrigin.Automatic),
        AutomationAction.Update when decision.Install is { } install => new UpdateJobRequest(install.Record, JobOrigin.Automatic),
        AutomationAction.Repair when decision.Install is { } install => new RepairJobRequest(install.Record, JobOrigin.Automatic),
        _ => null,
    };

    private static AutomationNotice Summary(List<(MyGame Game, AutomationDecision Decision)> announced) =>
        new(NoticeKind.AvailableSummary)
        {
            Games = [.. announced.Select(item => item.Game.Key).Distinct()],
            Updates = [.. GamesWith(announced, NoticeKind.UpdateAvailable)],
            Repairs = [.. GamesWith(announced, NoticeKind.RepairNeeded)],
        };

    private static IEnumerable<GameKey> GamesWith(List<(MyGame Game, AutomationDecision Decision)> announced, NoticeKind kind) =>
        announced.Where(item => KindOf(item.Decision) == kind).Select(item => item.Game.Key).Distinct();

    private static NoticeKind KindOf(AutomationDecision announcement) => announcement.Reason switch
    {
        AutomationReason.AutoUpdateOff => NoticeKind.UpdateAvailable,
        AutomationReason.ReapplyOff => NoticeKind.RepairNeeded,
        AutomationReason.NoProjectChosen => NoticeKind.ChoiceNeeded,
        _ => NoticeKind.Available,
    };

    private static AutomationNotice Announcement(MyGame game, AutomationDecision decision) => new(KindOf(decision))
    {
        Game = game.Key,
        GameName = game.Name,
        ProjectName = decision.Install is { } install ? AutomationNotice.ProjectNameFor(game, install.Record.Id) : null,
        TeamName = (decision.Install?.Project ?? game.Project)?.Project.Team.Name,
        ReleaseLabel = decision.Release is { NameRaw.Length: > 0 } release ? release.NameRaw : null,
        ReleaseVersion = decision.Release is { Version: > 0 } numbered ? numbered.Version : null,
        OffersTargetLanguage = decision.Reason == AutomationReason.TargetLanguageOffered,
    };

    private void Follow(JobResult result, Attempt attempt, List<AutomationNotice> notices)
    {
        var now = _time.GetUtcNow();
        if (result.LostPrevious)
        {
            // The install is gone now; announcing or installing that release again at once would only repeat what just
            // failed, so it waits like any failed install.
            History = History.RecordAnnouncement(attempt.Game, attempt.Release)
                .RecordFailure(attempt.Game, attempt.Release, AutomationAction.Install, result.Failure?.Reason ?? JobFailureReason.InstallFailed,
                    attempt.Build, now, out _);
            notices.Add(Notice(NoticeKind.PreviousReleaseLost, attempt) with { Job = result.Job.Kind, Failure = result.Failure });
        }

        switch (result.Outcome)
        {
            case JobOutcome.Succeeded:
                History = History.RecordSuccess(attempt.Game, attempt.Release, attempt.Action, now);
                var source = result.Installed?.Source;
                notices.Add(Notice(attempt.Action switch
                {
                    AutomationAction.Install => NoticeKind.Installed,
                    AutomationAction.Update => NoticeKind.Updated,
                    _ => NoticeKind.Repaired,
                }, attempt) with
                {
                    TeamName = source?.TeamName,
                    ReleaseLabel = source?.ReleaseLabel,
                    ReleaseVersion = source?.ReleaseVersion,
                });
                break;
            case JobOutcome.Cancelled when result.CancelledByPlayer:
                // Starting again what the player cancelled would overrule the player, and announcing it would only repeat
                // what the player saw.
                History = History.ForgetAttempt(attempt.Game, attempt.Release, attempt.Action);
                // A repair cancelled after its uninstall put the install back as a new one.
                History = attempt.Action == AutomationAction.Repair && (result.Installed?.Id ?? attempt.Install) is { } broken
                    ? History.RecordRepairAnnouncement(broken, attempt.Build)
                    : History.RecordAnnouncement(attempt.Game, attempt.Release);
                _settings.Update(settings => settings.WithGame(attempt.Game, preferences => attempt.Action == AutomationAction.Install
                    ? preferences with { AutoInstall = false }
                    : preferences with { AutoUpdate = false }));
                break;
            case JobOutcome.Cancelled:
            case JobOutcome.Deferred:
            case JobOutcome.Failed when result.Failure?.Reason == JobFailureReason.AlreadyRunning:
                // It never ran, or the app exited; it is tried again at the next look.
                History = History.ForgetAttempt(attempt.Game, attempt.Release, attempt.Action);
                if (result.DeferredBy == JobWaitReason.StoreBusy)
                {
                    _awaitingStore.Add(attempt.Game);
                }

                break;
            case JobOutcome.Failed:
                History = History.RecordFailure(attempt.Game, attempt.Release, attempt.Action, result.Failure!.Reason, attempt.Build, now,
                    out var failures);
                // Retries come every few hours at most, and the player hears of the first failure only.
                if (failures == 1 && !result.LostPrevious)
                {
                    notices.Add(Notice(NoticeKind.Failed, attempt) with { Job = result.Job.Kind, Failure = result.Failure });
                }

                break;
        }
    }

    // What the player did by hand is what the automation does from then on.
    private void Learn(JobResult result, MyGame? game)
    {
        var key = result.Job.Game;
        History = History.Reset(key);
        if (!result.IsSuccess)
        {
            return;
        }

        if (result.Job.Kind == JobKind.Uninstall)
        {
            // Otherwise the next look would announce, or install again, what the player just removed.
            var removed = result.Job.InstallId is { } install ? game?.FindInstall(install) : null;
            Guid?[] releases = [removed?.Project?.Selection.Release?.Uuid, InstalledRelease(removed?.Record)];
            foreach (var release in releases.OfType<Guid>())
            {
                History = History.RecordAnnouncement(key, release);
            }

            _settings.Update(settings => settings.WithGame(key, preferences => preferences with { AutoInstall = false }));
        }
        else if (result.Installed is { IsTest: false, Source: { } source })
        {
            _settings.Update(settings => settings.WithGame(key, preferences =>
                preferences with { ProjectId = source.ProjectId, VariantName = source.VariantName }));
        }
    }

    private static Guid? InstalledRelease(InstallRecord? record) =>
        Guid.TryParse(record?.Source?.ReleaseId, out var release) ? release : null;

    private static AutomationNotice Notice(NoticeKind kind, Attempt attempt) =>
        new(kind) { Game = attempt.Game, GameName = attempt.GameName, ProjectName = attempt.ProjectName };

    private void SaveIfChanged(AutomationHistory before)
    {
        if (!ReferenceEquals(before, History))
        {
            _writer.Save(History.Tidy(_time.GetUtcNow()));
        }
    }

    /// <summary>An automatic job that runs, as the history and the notices need it.</summary>
    /// <param name="Game">The game the job works on.</param>
    /// <param name="GameName">The game's name for notices.</param>
    /// <param name="Release">The release the history tracks the action by.</param>
    /// <param name="Action">What the job does.</param>
    /// <param name="Build">The game's build when the job started, which a failure is bound to.</param>
    /// <param name="Install">The install an update or a repair works on.</param>
    /// <param name="ProjectName">Its project as notices about it name it.</param>
    private sealed record Attempt(GameKey Game, string GameName, Guid Release, AutomationAction Action, string? Build, Guid? Install,
        string? ProjectName);
}
