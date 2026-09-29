using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.State;

public sealed class VerificationScheduleTests
{
    private static readonly InstallRecord Install = Record();

    [Fact]
    public void An_install_never_checked_or_on_a_new_build_is_due_now()
    {
        Due(Installed(), last: null).Should().Be(Now);
        Due(Installed(build: "101"), new Verification(Now.AddMinutes(-5), "100", null)).Should().Be(Now);
    }

    [Fact]
    public void A_checked_install_is_due_again_a_day_later_or_when_the_player_asks()
    {
        var last = new Verification(Now.AddHours(-2), "100", null);

        Due(Installed(), last).Should().Be(last.At + VerificationSchedule.Interval);
        Due(Installed(), last, requested: true).Should().Be(Now);
    }

    [Fact]
    public void A_check_dated_far_ahead_by_a_wrong_clock_counts_as_not_done()
    {
        Due(Installed(), new Verification(Now.AddDays(2), "100", null)).Should().Be(Now);
    }

    [Fact]
    public void Waits_until_the_store_left_the_game_alone()
    {
        var touched = Now.AddSeconds(-10);

        Due(Installed() with { StoreActivityAt = touched }, last: null).Should().Be(touched + SafetyGate.QuietPeriod);
        Due(Installed() with { StoreActivityAt = Now.AddDays(1) }, last: null).Should().Be(Now, "a clock far ahead is ignored");
    }

    [Fact]
    public void Never_checks_while_a_job_or_the_store_works_on_the_game_nor_elsewhere_than_the_install()
    {
        Due(Installed(), null, hasJob: true).Should().BeNull();
        Due(Installed(state: GameInstallState.Updating), null).Should().BeNull();
        Due(Installed() with { IsStale = true }, null).Should().BeNull();
        Due(Installed(folder: Path.Join("elsewhere", "Example")), null).Should().BeNull();
        Due(null, null).Should().BeNull();
        VerificationSchedule.DueAt(Record(isTest: true), Installed(), false, null, false, Now).Should().BeNull();
    }

    [Fact]
    public void Names_the_most_urgent_problem_of_the_files()
    {
        Verification.RepairOf(Result(FileIntegrity.Missing, FileIntegrity.OriginalRestored)).Should().Be(RepairReason.OriginalsRestored);
        Verification.RepairOf(Result(FileIntegrity.Missing, FileIntegrity.Modified)).Should().Be(RepairReason.FilesModified);
        Verification.RepairOf(Result(FileIntegrity.Intact, FileIntegrity.Missing)).Should().Be(RepairReason.FilesMissing);
        Verification.RepairOf(Result(FileIntegrity.Intact)).Should().BeNull();
    }

    private static DateTimeOffset? Due(InstalledGame? game, Verification? last, bool hasJob = false, bool requested = false) =>
        VerificationSchedule.DueAt(Install, game, hasJob, last, requested, Now);

    private static VerificationResult Result(params FileIntegrity[] files) =>
        new([.. files.Select((integrity, index) => new FileVerification($"file-{index}", FileChange.Replaced, integrity))]);
}
