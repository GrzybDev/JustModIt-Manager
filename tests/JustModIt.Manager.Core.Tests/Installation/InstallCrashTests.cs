using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

// One class per backup location, so xUnit runs the sweeps in parallel.
public abstract class InstallCrashTests(string location)
{
    [Fact]
    public async Task Recovers_an_install_that_a_crash_cut_short_at_any_change()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        Dictionary<string, string> installed;
        int changes;
        await using (var counting = RecoveryTests.Harness(location))
        {
            var count = counting.CountChanges();
            await counting.InstallAsync(counting.ManifestPackage(RecoveryTests.FullPackage(patch)));
            changes = count();
            installed = counting.SnapshotOfGameFiles();
        }

        changes.Should().BeGreaterThan(6, "every step changes the game at least once");
        for (var change = 0; change < changes; change++)
        {
            await using var harness = RecoveryTests.Harness(location);
            var before = harness.Snapshot();
            harness.CrashAt(change);
            await harness.InstallAsync(harness.ManifestPackage(RecoveryTests.FullPackage(patch)));
            harness.RestoreCrash().Should().BeTrue();

            var engine = harness.CreateEngine();
            var recovered = await engine.RecoverAsync(InstallHarness.Token);

            var outcome = recovered.Should().ContainSingle().Which.Outcome;
            if (outcome == RecoveryOutcome.CleanedUp)
            {
                // The crash came after the record committed the install.
                harness.SnapshotOfGameFiles().Should().BeEquivalentTo(installed, "crash at change {0}", change);
                (await engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle();
                continue;
            }

            outcome.Should().Be(RecoveryOutcome.RolledBack, "crash at change {0}", change);
            harness.Snapshot().Should().BeEquivalentTo(before, "crash at change {0}", change);
            harness.AppFiles().Should().BeEmpty("crash at change {0}", change);
        }
    }
}

public sealed class InstallCrashWithBackupsInTheGameFolderTests() : InstallCrashTests("game folder");

public sealed class InstallCrashWithBackupsInAppDataTests() : InstallCrashTests("app data");

public sealed class InstallCrashWithBackupsOnAnotherDriveTests() : InstallCrashTests("custom folder on another drive");
