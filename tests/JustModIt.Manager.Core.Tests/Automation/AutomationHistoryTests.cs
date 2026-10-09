using System.Text.Json;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Automation;

public sealed class AutomationHistoryTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void Counts_failures_in_a_row_from_when_the_try_started_and_a_success_starts_over()
    {
        var history = AutomationHistory.Empty.RecordAttempt(Game, ReleaseOne, AutomationAction.Update, Now)
            .RecordFailure(Game, ReleaseOne, AutomationAction.Update, JobFailureReason.Offline, "100", Now.AddMinutes(5), out var first)
            .RecordAttempt(Game, ReleaseOne, AutomationAction.Update, Now.AddHours(7))
            .RecordFailure(Game, ReleaseOne, AutomationAction.Update, JobFailureReason.Offline, "100", Now.AddHours(7), out var second);

        first.Should().Be(1);
        second.Should().Be(2);
        history.Attempts[new AttemptKey(Game, ReleaseOne, AutomationAction.Update)].At.Should().Be(Now.AddHours(7));
        history.RecordSuccess(Game, ReleaseOne, AutomationAction.Update, Now.AddHours(8))
            .Attempts[new AttemptKey(Game, ReleaseOne, AutomationAction.Update)].Failures.Should().Be(0);
    }

    [Fact]
    public void Forgetting_and_resetting_touch_only_what_they_name()
    {
        var history = AutomationHistory.Empty.RecordAttempt(Game, ReleaseOne, AutomationAction.Install, Now)
            .RecordAttempt(Game, ReleaseTwo, AutomationAction.Update, Now)
            .RecordAttempt(OtherGame, ReleaseOne, AutomationAction.Install, Now)
            .RecordAnnouncement(Game, ReleaseOne)
            .RecordRepairAnnouncement(Game, "100")
            .RecordRepairAnnouncement(OtherGame, null);

        history.ForgetAttempt(Game, ReleaseOne, AutomationAction.Install).Attempts.Should().HaveCount(2);
        var reset = history.Reset(Game);
        reset.Attempts.Keys.Should().Equal(new AttemptKey(OtherGame, ReleaseOne, AutomationAction.Install));
        reset.WasAnnounced(Game, ReleaseOne).Should().BeTrue("a player's action does not bring an announcement back");
        reset.WasRepairAnnounced(Game, "100").Should().BeTrue();
        history.ForgetRepairAnnouncements(Game).RepairAnnouncements.Should().Equal(new RepairAnnouncement(OtherGame, null));
    }

    [Fact]
    public void Drops_old_attempts_and_restarts_those_dated_ahead_by_a_wrong_clock()
    {
        var history = AutomationHistory.Empty.RecordAttempt(Game, ReleaseOne, AutomationAction.Install, Now.AddDays(-31))
            .RecordAttempt(Game, ReleaseTwo, AutomationAction.Install, Now.AddDays(-1))
            .RecordAttempt(OtherGame, ReleaseOne, AutomationAction.Install, Now.AddDays(2));

        var tidy = history.Tidy(Now);

        tidy.Attempts.Should().HaveCount(2);
        tidy.Attempts[new AttemptKey(OtherGame, ReleaseOne, AutomationAction.Install)].At.Should().Be(Now);
        tidy.CanTry(OtherGame, ReleaseOne, AutomationAction.Install, null, Now.AddHours(1)).Should().BeFalse();
    }

    [Fact]
    public void An_unchanged_history_stays_the_same_object()
    {
        var history = AutomationHistory.Empty.RecordAnnouncement(Game, ReleaseOne);

        history.RecordAnnouncement(Game, ReleaseOne).Should().BeSameAs(history);
        history.ForgetAttempt(Game, ReleaseOne, AutomationAction.Install).Should().BeSameAs(history);
    }

    [Fact]
    public async Task Survives_a_round_trip()
    {
        var history = AutomationHistory.Empty.RecordAttempt(Game, ReleaseOne, AutomationAction.Repair, Now)
            .RecordFailure(Game, ReleaseOne, AutomationAction.Repair, JobFailureReason.UnsupportedGameVersion, "100", Now, out _)
            .RecordAttempt(OtherGame, ReleaseTwo, AutomationAction.Install, Now.AddHours(-1))
            .RecordAnnouncement(OtherGame, ReleaseOne)
            .RecordRepairAnnouncement(Game, "100")
            .RecordRepairAnnouncement(OtherGame, null)
            .WithBaseline(Now.AddDays(-3));

        await Store().SaveAsync(history, TestContext.Current.CancellationToken);
        var loaded = (await Store().LoadAsync(TestContext.Current.CancellationToken))!;

        loaded.Attempts.Should().BeEquivalentTo(history.Attempts);
        loaded.Announcements.Should().BeEquivalentTo(history.Announcements);
        loaded.RepairAnnouncements.Should().BeEquivalentTo(history.RepairAnnouncements);
        loaded.BaselineAt.Should().Be(Now.AddDays(-3));
    }

    [Fact]
    public void Leaves_out_entries_that_cannot_be_read()
    {
        using var json = JsonDocument.Parse("""
            {
              "attempts": [
                { "game": "steam:1000", "release": "22222222-0000-4000-8000-000000000001", "action": "Install", "at": "2026-09-23T12:00:00+00:00", "failures": 2 },
                { "game": "steam:1000", "release": "not a guid", "action": "Install", "at": "2026-09-23T12:00:00+00:00" },
                { "game": "steam:1000", "release": "22222222-0000-4000-8000-000000000002", "action": "Explode", "at": "2026-09-23T12:00:00+00:00" }
              ],
              "announcements": [ { "game": "steam:2000", "release": "22222222-0000-4000-8000-000000000001" }, { "game": "?" } ]
            }
            """);

        var history = AutomationHistoryFormat.Instance.Read(json.RootElement, 1);

        history.Attempts.Should().ContainSingle().Which.Value.Failures.Should().Be(2);
        history.Announcements.Should().Equal(new Announcement(OtherGame, ReleaseOne));
        history.BaselineAt.Should().BeNull();
    }

    private JsonDocumentStore<AutomationHistory> Store() =>
        new(_directory.Combine("automation.json"), AutomationHistoryFormat.Instance, TimeProvider.System, NullLogger.Instance);
}
