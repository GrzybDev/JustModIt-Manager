using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

// One class per backup location, so xUnit runs the sweeps in parallel.
public abstract class InstallCrashTests(string location)
{
    [Fact]
    public async Task Recovers_an_install_that_a_crash_cut_short_at_any_change()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);

        await SweepAsync(RecoveryTests.FullPackage(patch));
    }

    [Fact]
    public async Task Recovers_a_merged_package_that_a_crash_cut_short_at_any_change()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var other = await TestPatch.CreateAsync(TestPatch.RandomBytes(InstallHarness.OriginalPak.Length, seed: 5), InstallHarness.ChangedPak);

        await SweepAsync(new TestPackage()
            .Rename("binaries/library.dll", "binaries/library_original.dll")
            .Copy("binaries/library.dll", "proxy library")
            .Alternatives("assets/strings.pak", other, patch)
            .Copy("assets/lang/text.txt", "text").Only(TestProjects.Game.Store)
            .Copy("assets/lang/other.txt", "another store's text").Only(StoreId.Gog)
            .Delete("video/intro.bik")
            .Targets(new PackageTarget(TestProjects.Game.Store, TestProjects.Game.StoreGameId,
                Folders: [new("assets", "data"), new("binaries", "bin"), new("video", "movies")])));
    }

    [Fact]
    public async Task Recovers_an_install_that_takes_files_over_from_a_kept_files_record_and_was_cut_short_at_any_change()
    {
        // A step writes one of the handed-over files, so its original comes back from the kept copy before the step's backup.
        var second = RuntimeFileTests.Software().Copy("tool/list.dat", "List shipped by release two");
        Dictionary<string, string> installed;
        int changes;
        await using (var counting = RuntimeFileTests.WithList(RecoveryTests.Harness(location)))
        {
            var package = await HandOverAsync(counting, second);
            var count = counting.CountChanges();
            await counting.InstallAsync(package);
            changes = count();
            installed = counting.SnapshotOfGameFiles();
        }

        changes.Should().BeGreaterThan(5, "the taken-over files go aside, their originals come back and the steps run");
        for (var change = 0; change < changes; change++)
        {
            await using var harness = RuntimeFileTests.WithList(RecoveryTests.Harness(location));
            var before = harness.Snapshot();
            var package = await HandOverAsync(harness, second);
            var handedOver = harness.Snapshot();
            var kept = await harness.KeptFilesAsync();
            harness.CrashAt(change);
            await harness.InstallAsync(package);
            harness.RestoreCrash().Should().BeTrue();

            var engine = harness.CreateEngine();
            var recovered = await engine.RecoverAsync(InstallHarness.Token);

            var outcome = recovered.Should().ContainSingle().Which.Outcome;
            outcome.Should().BeOneOf([RecoveryOutcome.RolledBack, RecoveryOutcome.CleanedUp], "crash at change {0}", change);
            if (outcome == RecoveryOutcome.RolledBack)
            {
                harness.Snapshot().Should().BeEquivalentTo(handedOver, "crash at change {0}", change);
                (await harness.KeptFilesAsync()).Should().BeEquivalentTo(kept, "a kept-files record is only read until the commit");
                await engine.InstallAsync(harness.Request(package), null, InstallHarness.Token);
            }

            harness.SnapshotOfGameFiles().Should().BeEquivalentTo(installed, "crash at change {0}", change);
            (await harness.KeptFilesAsync()).Should().BeEmpty("crash at change {0}", change);
            var record = (await engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which;
            await engine.UninstallAsync(record.Id, null, null, InstallHarness.Token);
            harness.Snapshot().Should().BeEquivalentTo(before, "crash at change {0}", change);
        }
    }

    private async Task SweepAsync(TestPackage package)
    {
        Dictionary<string, string> installed;
        int changes;
        await using (var counting = RecoveryTests.Harness(location))
        {
            var count = counting.CountChanges();
            await counting.InstallAsync(counting.ManifestPackage(package));
            changes = count();
            installed = counting.SnapshotOfGameFiles();
        }

        changes.Should().BeGreaterThan(6, "every step changes the game at least once");
        for (var change = 0; change < changes; change++)
        {
            await using var harness = RecoveryTests.Harness(location);
            var before = harness.Snapshot();
            harness.CrashAt(change);
            await harness.InstallAsync(harness.ManifestPackage(package));
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

    // Installs the software, plays, and uninstalls it for a release that declares the same files; returns that release.
    private static async Task<string> HandOverAsync(InstallHarness harness, TestPackage second)
    {
        var first = await harness.InstallAsync(harness.ManifestPackage(RuntimeFileTests.Software()));
        RuntimeFileTests.Play(harness);
        var package = harness.ManifestPackage(second);
        await harness.Engine.UninstallAsync(first.Record.Id, [package], null, InstallHarness.Token);
        return package;
    }
}

public sealed class InstallCrashWithBackupsInTheGameFolderTests() : InstallCrashTests("game folder");

public sealed class InstallCrashWithBackupsInAppDataTests() : InstallCrashTests("app data");

public sealed class InstallCrashWithBackupsOnAnotherDriveTests() : InstallCrashTests("custom folder on another drive");
