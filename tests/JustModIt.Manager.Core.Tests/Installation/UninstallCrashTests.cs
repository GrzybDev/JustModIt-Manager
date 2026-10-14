using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
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
                (await engine.UninstallAsync(result.Record.Id, null, null, InstallHarness.Token)).KeptFiles.Should().BeEmpty();
            }

            harness.Snapshot().Should().BeEquivalentTo(before, "crash at change {0}", change);
            harness.AppFiles().Should().BeEmpty("crash at change {0}", change);
        }
    }

    [Fact]
    public async Task Recovers_an_uninstall_of_files_the_software_changed_that_a_crash_cut_short_at_any_change()
    {
        // An uncertain original comes back and leaves a marker in a kept-files record.
        var package = RuntimeFileTests.Software("Store list").Copy("readme.txt", "Changed")
            .Targets(new PackageTarget(StoreId.Steam, TestProjects.Game.StoreGameId, "42"));
        int changes;
        await using (var counting = RuntimeFileTests.WithList(RecoveryTests.Harness(location)))
        {
            var installed = await counting.InstallAsync(counting.ManifestPackage(package));
            RuntimeFileTests.Play(counting);
            var count = counting.CountChanges();
            await counting.UninstallAsync(installed.Record.Id);
            changes = count();
        }

        changes.Should().BeGreaterThan(5, "the originals come back, the software's copy goes and the kept-files record is written");
        for (var change = 0; change < changes; change++)
        {
            await using var harness = RuntimeFileTests.WithList(RecoveryTests.Harness(location));
            var before = harness.Snapshot();
            var result = await harness.InstallAsync(harness.ManifestPackage(package));
            RuntimeFileTests.Play(harness);
            var played = harness.Snapshot();
            harness.CrashAt(change);
            await harness.UninstallAsync(result.Record.Id);
            harness.RestoreCrash().Should().BeTrue();

            var engine = harness.CreateEngine();
            var recovered = await engine.RecoverAsync(InstallHarness.Token);

            var outcome = recovered.Should().ContainSingle().Which.Outcome;
            outcome.Should().BeOneOf([RecoveryOutcome.RolledBack, RecoveryOutcome.CleanedUp], "crash at change {0}", change);
            if (outcome == RecoveryOutcome.RolledBack)
            {
                harness.Snapshot().Should().BeEquivalentTo(played, "an uninstall that did not commit leaves the install as it was");
                (await harness.KeptFilesAsync()).Should().BeEmpty("an uninstall that did not commit keeps nothing");
                (await engine.UninstallAsync(result.Record.Id, null, null, InstallHarness.Token)).KeptFiles.Should()
                    .Equal(new KeptFile("tool/list.dat", KeptReason.OriginalUnknown));
            }

            harness.Snapshot().Should().BeEquivalentTo(before, "crash at change {0}", change);
            (await harness.KeptFilesAsync()).Values.Should().ContainSingle("crash at change {0}", change).Which.Files.Should()
                .ContainSingle().Which.Kept.Should().Be(KeptReason.OriginalUnknown);
            harness.AppFiles().Should().OnlyContain(path => path.StartsWith(harness.LeftoversDirectory, StringComparison.Ordinal),
                "crash at change {0}", change);
        }
    }
}

public sealed class UninstallCrashWithBackupsInTheGameFolderTests() : UninstallCrashTests("game folder");

public sealed class UninstallCrashWithBackupsInAppDataTests() : UninstallCrashTests("app data");

public sealed class UninstallCrashWithBackupsOnAnotherDriveTests() : UninstallCrashTests("custom folder on another drive");
