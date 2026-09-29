using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class RecoveryTests
{
    public static TheoryData<string> Locations => ["game folder", "app data", "app data on another drive", "custom folder on another drive"];

    [Theory]
    [MemberData(nameof(Locations))]
    public async Task Recovers_an_install_that_a_crash_cut_short_at_any_change(string location)
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        Dictionary<string, string> installed;
        int changes;
        await using (var counting = Harness(location))
        {
            var count = counting.CountChanges();
            await counting.InstallAsync(counting.ManifestPackage(FullPackage(patch)));
            changes = count();
            installed = counting.SnapshotOfGameFiles();
        }

        changes.Should().BeGreaterThan(6, "every step changes the game at least once");
        for (var change = 0; change < changes; change++)
        {
            await using var harness = Harness(location);
            var before = harness.Snapshot();
            harness.CrashAt(change);
            await harness.InstallAsync(harness.ManifestPackage(FullPackage(patch)));
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

    [Fact]
    public async Task Leaves_files_a_game_update_wrote_after_the_crash()
    {
        await using var harness = new InstallHarness();
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        harness.CrashBefore(ChangeKind.Record);
        await harness.InstallAsync(harness.ManifestPackage(FullPackage(patch)));
        harness.RestoreCrash();
        harness.WriteGameFile("movies/intro.bik", "video from the new build");
        harness.WriteGameFile("bin/library_original.dll", "library from the new build");

        var recovered = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.RolledBack);
        harness.ReadGameFile("movies/intro.bik").Should().Be("video from the new build");
        harness.ReadGameFile("bin/library_original.dll").Should().Be("library from the new build");
        harness.ReadGameFile("bin/library.dll").Should().Be("original library");
        File.Exists(harness.GameFile("data/lang/text.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"type":"absent","path":"bin/gam""")]
    [InlineData("\0\0\0\0garbage\n")]
    public async Task Ignores_a_last_journal_line_the_crash_cut_or_damaged(string tail)
    {
        await using var harness = new InstallHarness();
        var before = harness.Snapshot();
        var journal = await CrashMidInstallAsync(harness);
        await File.AppendAllTextAsync(journal, tail, InstallHarness.Token);

        var recovered = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.RolledBack);
        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Keeps_blocking_a_game_whose_journal_is_damaged_further_on()
    {
        await using var harness = new InstallHarness();
        var journal = await CrashMidInstallAsync(harness);
        var content = await File.ReadAllTextAsync(journal, InstallHarness.Token);
        await File.WriteAllTextAsync(journal, content.Insert(content.IndexOf('\n', StringComparison.Ordinal) + 1, "garbage\n"),
            InstallHarness.Token);
        var engine = harness.CreateEngine();

        var recovered = await engine.RecoverAsync(InstallHarness.Token);
        var install = () => engine.InstallAsync(harness.Request(harness.ManifestPackage(new TestPackage().Copy("a.txt", "a"))), null,
            InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.Failed);
        (await install.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should().ContainSingle()
            .Which.Should().BeOfType<RecoveryPendingIssue>();
        File.Exists(harness.GameFile("a.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Blocks_every_install_while_a_journal_header_cannot_be_read()
    {
        await using var harness = new InstallHarness();
        var folder = Directory.CreateDirectory(Path.Join(harness.InstallsDirectory, Guid.NewGuid().ToString("N")));
        await File.WriteAllTextAsync(Path.Join(folder.FullName, "journal.jsonl"), "garbage\n{\"type\":\"absent\",\"path\":\"a.txt\"}\n",
            InstallHarness.Token);

        var blocked = await harness.InstallBlockedAsync(harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));

        blocked.Issues.Should().ContainSingle().Which.Should().BeOfType<RecoveryPendingIssue>();
    }

    [Fact]
    public async Task Finishes_a_rollback_that_was_itself_interrupted()
    {
        await using var harness = new InstallHarness();
        var before = harness.Snapshot();
        await CrashMidInstallAsync(harness);
        var restores = 0;
        harness.BeforeChange = (kind, _) =>
        {
            if (kind == ChangeKind.Restore && ++restores == 2)
            {
                throw new IOException("The power went out.");
            }
        };

        var first = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);
        harness.BeforeChange = null;
        var second = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        first.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.Failed);
        second.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.RolledBack);
        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Keeps_an_install_that_committed_before_the_crash()
    {
        await using var harness = new InstallHarness();
        var result = await harness.InstallAsync(harness.ManifestPackage(new TestPackage().Copy("bin/library.dll", "proxy")));
        var installed = harness.Snapshot();
        var folder = Path.Join(harness.InstallsDirectory, result.Record.Id.ToString("N"));
        await File.WriteAllTextAsync(Path.Join(folder, "journal.jsonl"),
            $$"""{"type":"started","formatVersion":1,"kind":"Install","recordId":"{{result.Record.Id}}","gameDirectory":{{Json(harness.GameRoot)}},"stagingDirectory":{{Json(Path.Join(harness.HiddenDirectory, "staging", result.Record.Id.ToString("N")))}},"backupDirectory":{{Json(result.Record.Backup.Directory)}}}""" + "\n",
            InstallHarness.Token);

        var recovered = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.CleanedUp);
        harness.Snapshot().Should().BeEquivalentTo(installed);
        File.Exists(Path.Join(folder, "journal.jsonl")).Should().BeFalse();
        (await harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle();
    }

    [Fact]
    public async Task Tidies_up_after_an_install_that_committed_right_before_the_crash()
    {
        await using var harness = new InstallHarness();
        harness.CrashBefore(ChangeKind.Tidy);
        await harness.InstallAsync(harness.ManifestPackage(new TestPackage().Copy("data/lang/text.txt", "Text")));
        var installed = harness.Snapshot();
        harness.RestoreCrash();

        var recovered = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.CleanedUp);
        harness.Snapshot().Should().BeEquivalentTo(installed, "the app's empty folders in the game go as well");
    }

    [Fact]
    public async Task Removes_install_folders_left_without_a_record_or_journal()
    {
        await using var harness = new InstallHarness();
        var result = await harness.InstallAsync(harness.ManifestPackage(new TestPackage().Copy("bin/library.dll", "proxy")));
        var leftover = Directory.CreateDirectory(Path.Join(harness.InstallsDirectory, Guid.NewGuid().ToString("N")));
        await File.WriteAllTextAsync(Path.Join(leftover.FullName, "files.json"), "{}", InstallHarness.Token);

        var recovered = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        recovered.Should().BeEmpty();
        leftover.Exists.Should().BeFalse();
        (await harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which.Id.Should().Be(result.Record.Id);
    }

    [Theory]
    [InlineData("app data")]
    [InlineData("custom folder on another drive")]
    public async Task Discards_the_journal_and_backups_of_a_game_folder_that_is_gone(string location)
    {
        await using var harness = Harness(location);
        await CrashMidInstallAsync(harness);
        TreeCopy.Delete(harness.GameRoot);

        var recovered = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.Discarded);
        harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Waits_for_a_drive_that_is_not_attached()
    {
        await using var harness = Harness("app data");
        var journal = await CrashMidInstallAsync(harness);
        var game = Path.Join(harness.Crashes, "WholeGame");
        TreeCopy.Copy(harness.GameRoot, game);
        TreeCopy.Delete(harness.GameRoot);
        harness.Volumes.Detached.Add(InstallHarness.GameVolume);

        var recovered = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.Unavailable);
        File.Exists(journal).Should().BeTrue();
        harness.AppFiles().Should().Contain(file => file.StartsWith(Path.Join(harness.AppData, "backups"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Waits_for_backups_on_a_folder_that_is_not_there()
    {
        await using var harness = Harness("custom folder on another drive");
        var before = harness.Snapshot();
        await CrashMidInstallAsync(harness);
        var unplugged = harness.CustomFolder + "-unplugged";
        Directory.Move(harness.CustomFolder, unplugged);

        var waiting = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);
        Directory.Move(unplugged, harness.CustomFolder);
        var recovered = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        waiting.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.Unavailable);
        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.RolledBack);
        harness.Snapshot().Should().BeEquivalentTo(before);
    }

    private static InstallHarness Harness(string location)
    {
        var harness = new InstallHarness();
        if (location.EndsWith("another drive", StringComparison.Ordinal))
        {
            harness.PutBackupsOnAnotherVolume();
        }

        harness.Backup = location switch
        {
            _ when location.StartsWith("app data", StringComparison.Ordinal) => InstallHarness.InAppData,
            _ when location.StartsWith("custom", StringComparison.Ordinal) => harness.InCustomFolder,
            _ => BackupChoice.GameFolder,
        };
        File.SetAttributes(harness.GameFile("readme.txt"), FileAttributes.ReadOnly);
        return harness;
    }

    private static TestPackage FullPackage(TestPatch patch) => new TestPackage()
        .Rename("bin/library.dll", "bin/library_original.dll")
        .Copy("bin/library.dll", "proxy library")
        .Patch("data/strings.pak", patch)
        .Delete("movies/intro.bik")
        .Copy("readme.txt", "Changed")
        .Copy("data/lang/text.txt", "Text");

    // Leaves the folders as an install that died right before its commit left them, with no rollback; returns the journal's path.
    private static async Task<string> CrashMidInstallAsync(InstallHarness harness)
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        harness.CrashBefore(ChangeKind.Record);
        var result = await harness.InstallAsync(harness.ManifestPackage(FullPackage(patch)));
        harness.RestoreCrash().Should().BeTrue();
        return Path.Join(harness.InstallsDirectory, result.Record.Id.ToString("N"), "journal.jsonl");
    }

    private static string Json(string text) => "\"" + System.Text.Json.JsonEncodedText.Encode(text) + "\"";
}
