using JustModIt.Manager.Core.Installation;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class BackupMoveCrashTests
{
    public static TheoryData<string, string> Moves => new()
    {
        { "game folder", "app data" },
        { "game folder", "custom folder on another drive" },
        { "app data on another drive", "game folder" },
    };

    [Theory]
    [MemberData(nameof(Moves))]
    public async Task Survives_a_crash_at_any_step_of_a_move(string from, string to)
    {
        int changes;
        await using (var counting = InstallHarness.At(from, to))
        {
            var installed = await counting.InstallAsync(counting.ManifestPackage(BackupTests.Package()));
            var count = counting.CountChanges();
            await counting.Engine.MoveBackupsAsync(installed.Record.Id, counting.Choice(to), null, InstallHarness.Token);
            changes = count();
        }

        changes.Should().BeGreaterThan(3);
        for (var change = 0; change < changes; change++)
        {
            await using var harness = InstallHarness.At(from, to);
            var before = harness.Snapshot();
            var result = await harness.InstallAsync(harness.ManifestPackage(BackupTests.Package()));
            harness.CrashAt(change);
            await harness.Engine.MoveBackupsAsync(result.Record.Id, harness.Choice(to), null, InstallHarness.Token);
            harness.RestoreCrash().Should().BeTrue();

            var engine = harness.CreateEngine();
            var recovered = await engine.RecoverAsync(InstallHarness.Token);

            recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.CleanedUp, "crash at change {0}", change);
            await BackupTests.BackupsShouldHoldTheOriginalsAsync(harness);
            (await engine.UninstallAsync(result.Record.Id, null, InstallHarness.Token)).KeptFiles.Should()
                .BeEmpty("crash at change {0}", change);
            harness.Snapshot().Should().BeEquivalentTo(before, "crash at change {0}", change);
            harness.AppFiles().Should().BeEmpty("crash at change {0}", change);
        }
    }
}
