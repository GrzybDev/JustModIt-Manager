using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Safety;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class SafetyGateTests
{
    [Fact]
    public void An_installed_idle_game_is_ready()
    {
        SafetyGate.Check(Probe(Installed()), JobKind.Install, Now).Should().Be(GateVerdict.Ready);
    }

    [Fact]
    public void A_game_the_store_no_longer_lists_fails()
    {
        SafetyGate.Check(new GameProbe(Game, null, []), JobKind.Install, Now).Failure!.Reason.Should().Be(JobFailureReason.GameNotDetected);
    }

    [Theory]
    [InlineData(GameInstallState.Updating)]
    [InlineData(GameInstallState.Validating)]
    [InlineData(GameInstallState.Installing)]
    public void Waits_while_the_store_works_on_the_game(GameInstallState state)
    {
        foreach (var kind in Enum.GetValues<JobKind>())
        {
            SafetyGate.Check(Probe(Installed(state: state)), kind, Now).WaitReason.Should().Be(JobWaitReason.StoreBusy);
        }
    }

    [Fact]
    public void Waits_while_the_stores_records_are_caught_mid_write()
    {
        var unreadable = Installed(state: GameInstallState.Unknown) with { Reason = GameStateReason.UnreadableStoreData };

        SafetyGate.Check(Probe(unreadable), JobKind.Install, Now).WaitReason.Should().Be(JobWaitReason.StoreBusy);
        SafetyGate.Check(Probe(Installed() with { IsStale = true }), JobKind.Install, Now).WaitReason.Should().Be(JobWaitReason.StoreBusy);
    }

    [Theory]
    [InlineData(GameInstallState.UpdatePending, JobKind.Install, false)]
    [InlineData(GameInstallState.Broken, JobKind.Repair, false)]
    [InlineData(GameInstallState.Offline, JobKind.Update, false)]
    [InlineData(GameInstallState.Unknown, JobKind.Install, false)]
    [InlineData(GameInstallState.UpdatePending, JobKind.Uninstall, true)]
    [InlineData(GameInstallState.Broken, JobKind.Uninstall, true)]
    [InlineData(GameInstallState.Offline, JobKind.Uninstall, false)]
    public void Only_an_uninstall_may_run_on_a_game_waiting_for_an_update_or_broken(GameInstallState state, JobKind kind, bool ready)
    {
        var verdict = SafetyGate.Check(Probe(Installed(state: state)), kind, Now);

        if (ready)
        {
            verdict.Should().Be(GateVerdict.Ready);
        }
        else
        {
            verdict.Failure.Should().BeEquivalentTo(new JobFailure(JobFailureReason.GameNotReady) { GameState = state, StateReason = GameStateReason.None });
        }
    }

    [Fact]
    public void Waits_while_the_game_runs_even_for_an_uninstall()
    {
        var process = new RunningProcess(10, "ExampleGame", null);
        var probe = new GameProbe(Game, Installed(state: GameInstallState.Running), [process]);

        var verdict = SafetyGate.Check(probe, JobKind.Uninstall, Now);

        verdict.WaitReason.Should().Be(JobWaitReason.GameRunning);
        verdict.Processes.Should().Equal(process);
    }

    [Fact]
    public void A_game_waiting_for_a_store_update_waits_for_its_exit_only_when_an_uninstall_may_run()
    {
        var probe = new GameProbe(Game, Installed(state: GameInstallState.UpdatePending), [new RunningProcess(10, "ExampleGame", null)]);

        SafetyGate.Check(probe, JobKind.Uninstall, Now).WaitReason.Should().Be(JobWaitReason.GameRunning);
        SafetyGate.Check(probe, JobKind.Update, Now).Failure!.Reason.Should().Be(JobFailureReason.GameNotReady);
    }

    [Theory]
    [InlineData(-29, true)]
    [InlineData(-30, false)]
    [InlineData(0, true)]
    [InlineData(60, true)]
    [InlineData(299, true)]
    [InlineData(301, false)]
    [InlineData(86_400, false)]
    public void Waits_for_a_quiet_period_after_the_store_touched_the_game_but_not_for_a_clock_far_ahead(int touchedSeconds, bool waits)
    {
        var touched = Now.AddSeconds(touchedSeconds);

        var verdict = SafetyGate.Check(Probe(Installed() with { StoreActivityAt = touched }), JobKind.Install, Now);

        if (waits)
        {
            verdict.WaitReason.Should().Be(JobWaitReason.QuietPeriod);
            verdict.WaitUntil.Should().Be(touched + SafetyGate.QuietPeriod);
        }
        else
        {
            verdict.Status.Should().Be(GateStatus.Ready);
            verdict.ClockSkewIgnored.Should().Be(touchedSeconds > 0);
        }
    }

    [Fact]
    public void Tells_a_game_that_came_into_use_from_other_refusals()
    {
        SafetyGate.IsOnlyInUse([new GameRunningIssue("ExampleGame")]).Should().BeTrue();
        SafetyGate.IsOnlyInUse([new GameNotReadyIssue(GameInstallState.Updating, GameStateReason.StoreBusy)]).Should().BeTrue();
        SafetyGate.IsOnlyInUse([new GameRunningIssue("ExampleGame"), new MissingFileIssue("data/strings.pak")]).Should().BeFalse();
        SafetyGate.IsOnlyInUse([new GameNotReadyIssue(GameInstallState.Broken, GameStateReason.FilesMissing)]).Should().BeFalse();
        SafetyGate.IsOnlyInUse([]).Should().BeFalse();
    }

    private static GameProbe Probe(InstalledGame game) => new(game.Key, game, []);
}
