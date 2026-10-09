using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

// One class per backup location, so xUnit runs the sweeps in parallel.
public abstract class UninstallCrashTests(string location)
{
    [Fact]
    public async Task Recovers_an_uninstall_that_a_crash_cut_short_at_any_change()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        int changes;
        await using (var counting = RecoveryTests.Harness(location))
        {
            var installed = await counting.InstallAsync(counting.ManifestPackage(RecoveryTests.FullPackage(patch)));
            var count = counting.CountChanges();
            await counting.UninstallAsync(installed.Record.Id);
            changes = count();
        }

        changes.Should().BeGreaterThan(5, "every file is put back or removed");

        for (var change = 0; change < changes; change++)
        {
            await using var harness = RecoveryTests.Harness(location);
            var before = harness.Snapshot();
            var result = await harness.InstallAsync(harness.ManifestPackage(RecoveryTests.FullPackage(patch)));
            var installed = harness.Snapshot();
            harness.CrashAt(change);
            await harness.UninstallAsync(result.Record.Id);
            harness.RestoreCrash().Should().BeTrue();

            var engine = harness.CreateEngine();
            var recovered = await engine.RecoverAsync(InstallHarness.Token);

            var outcome = recovered.Should().ContainSingle().Which.Outcome;
            outcome.Should().BeOneOf([RecoveryOutcome.RolledBack, RecoveryOutcome.CleanedUp], "crash at change {0}", change);
            if (outcome == RecoveryOutcome.RolledBack)
            {
                harness.Snapshot().Should().BeEquivalentTo(installed, "an uninstall that did not commit leaves the install as it was");
                (await engine.UninstallAsync(result.Record.Id, null, InstallHarness.Token)).KeptFiles.Should().BeEmpty();
            }

            harness.Snapshot().Should().BeEquivalentTo(before, "crash at change {0}", change);
            harness.AppFiles().Should().BeEmpty("crash at change {0}", change);
        }
    }
}

public sealed class UninstallCrashWithBackupsInTheGameFolderTests() : UninstallCrashTests("game folder");

public sealed class UninstallCrashWithBackupsInAppDataTests() : UninstallCrashTests("app data");

public sealed class UninstallCrashWithBackupsOnAnotherDriveTests() : UninstallCrashTests("custom folder on another drive");
