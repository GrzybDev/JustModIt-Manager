using System.Text.Json;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class RecoveryTests
{
    [Fact]
    public async Task Undoes_an_install_and_an_unfinished_journal_written_by_the_previous_version()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The saved files hold Windows paths.");
        await using var harness = new InstallHarness();
        var before = harness.Snapshot();
        var saved = Path.Join(AppContext.BaseDirectory, "Installation", "Fixtures", "v1");
        TreeCopy.Copy(Path.Join(saved, "Game"), harness.GameRoot);
        TreeCopy.Copy(Path.Join(saved, "AppData"), harness.AppData);
        var root = JsonEncodedText.Encode(Path.GetDirectoryName(harness.GameRoot)!).ToString();
        foreach (var file in Directory.EnumerateFiles(harness.InstallsDirectory, "*", SearchOption.AllDirectories))
        {
            await File.WriteAllTextAsync(file, (await File.ReadAllTextAsync(file, InstallHarness.Token)).Replace("{root}", root,
                StringComparison.Ordinal), InstallHarness.Token);
        }

        var engine = harness.CreateEngine();
        var recovered = await engine.RecoverAsync(InstallHarness.Token);
        var record = (await engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which;
        var uninstalled = await engine.UninstallAsync(record.Id, null, null, InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.RolledBack);
        uninstalled.KeptFiles.Should().BeEmpty();
        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Undoes_an_install_of_several_packages_and_an_unfinished_one_written_by_the_previous_version()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The saved files hold Windows paths.");
        await using var harness = new InstallHarness();
        var before = harness.Snapshot();
        var saved = Path.Join(AppContext.BaseDirectory, "Installation", "Fixtures", "v1-parts");
        TreeCopy.Copy(Path.Join(saved, "Game"), harness.GameRoot);
        TreeCopy.Copy(Path.Join(saved, "AppData"), harness.AppData);
        // Git keeps no empty folder, and the cut install had backed up nothing into its own.
        Directory.CreateDirectory(Path.Join(harness.AppData, "backups", "steam-1000", "1f8801ab1a784f2dbd9b1fc0e7e9e942"));
        var root = JsonEncodedText.Encode(Path.GetDirectoryName(harness.GameRoot)!).ToString();
        foreach (var file in Directory.EnumerateFiles(harness.InstallsDirectory, "*", SearchOption.AllDirectories))
        {
            await File.WriteAllTextAsync(file, (await File.ReadAllTextAsync(file, InstallHarness.Token)).Replace("{root}", root,
                StringComparison.Ordinal), InstallHarness.Token);
        }

        var engine = harness.CreateEngine();
        var recovered = await engine.RecoverAsync(InstallHarness.Token);
        var record = (await engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which;
        var uninstalled = await engine.UninstallAsync(record.Id, null, null, InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.RolledBack);
        record.Parts!.Select(part => (part.Package.Kind, part.FeatureKey, part.ReleaseFileId)).Should().Equal(
            (PackageKind.Zip, null, "file-1"), (PackageKind.Manifest, "voices", "file-voices"));
        record.Parts![1].Package.ThirdPartyNotices.Should().ContainSingle().Which.Name.Should().Be("Example Loader");
        uninstalled.KeptFiles.Should().BeEmpty();
        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Recovers_and_undoes_installs_whose_software_changes_files_written_by_the_previous_version()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The saved files hold Windows paths.");
        // A, committed and inheriting from an earlier install taken on build 41; B, an uninstall cut before its commit after
        // writing its kept-files record; C, one cut after its commit; D, a kept-files record with a held-back file and a marker.
        var (a, b, c, d) = (Guid.Parse("545ea5ba4b974d59bf22dcb013db2f51"), Guid.Parse("3e03be63bc8d4e2f8770672b7474298e"),
            Guid.Parse("9f5a101cfbd84598beb77d00c9f09f81"), Guid.Parse("02b5a9f89ca64bb3b182c6bdbf40c881"));
        await using var harness = new InstallHarness();
        var saved = Path.Join(AppContext.BaseDirectory, "Installation", "Fixtures", "v2");
        TreeCopy.Copy(Path.Join(saved, "Before"), harness.GameRoot);
        var before = harness.Snapshot();
        TreeCopy.Copy(Path.Join(saved, "Game"), harness.GameRoot);
        TreeCopy.Copy(Path.Join(saved, "AppData"), harness.AppData);
        var root = JsonEncodedText.Encode(Path.GetDirectoryName(harness.GameRoot)!).ToString();
        foreach (var file in Directory.EnumerateFiles(harness.AppData, "*.json*", SearchOption.AllDirectories))
        {
            await File.WriteAllTextAsync(file, (await File.ReadAllTextAsync(file, InstallHarness.Token)).Replace("{root}", root,
                StringComparison.Ordinal), InstallHarness.Token);
        }

        // Git keeps no empty folder, and the cut uninstall had moved B's only backup out of its own.
        Directory.CreateDirectory(Path.Join(harness.AppData, "backups", "steam-1000", b.ToString("N")));
        var engine = harness.CreateEngine();

        var recovered = await engine.RecoverAsync(InstallHarness.Token);

        recovered.Should().BeEquivalentTo([new RecoveredOperation(b, RecoveryOutcome.RolledBack, harness.GameRoot),
            new RecoveredOperation(c, RecoveryOutcome.CleanedUp, harness.GameRoot)]);
        (await harness.KeptFilesAsync()).Keys.Should().BeEquivalentTo([c, d], "B never committed, so it keeps nothing");
        File.Exists(Path.Join(harness.AppData, "backups", "steam-1000", c.ToString("N"), "0")).Should().BeTrue("C's copy was kept");
        (await engine.LoadRecordsAsync(InstallHarness.Token)).Select(record => record.Id).Should().BeEquivalentTo([a, b]);

        (await engine.UninstallAsync(b, null, null, InstallHarness.Token)).KeptFiles.Should()
            .Equal(new KeptFile("b/list.dat", KeptReason.OriginalUnknown));
        (await engine.UninstallAsync(a, null, null, InstallHarness.Token)).KeptFiles.Should().BeEquivalentTo([
            new KeptFile("a/list.dat", KeptReason.GameUpdatedSinceInstall), new KeptFile("a/data.dat", KeptReason.OriginalUnknown)],
            "the list was taken on build 41, and the earlier install never listed the data");
        var inheriting = await engine.InstallAsync(harness.Request(harness.ManifestPackage(new TestPackage().Copy("d/loader.dll", "loader")
            .Declare("d/state.dat"))), null, InstallHarness.Token);
        var state = (await engine.LoadFilesAsync(inheriting.Record.Id, InstallHarness.Token)).Files.Single(file => file.Path == "d/state.dat");
        (await engine.UninstallAsync(inheriting.Record.Id, null, null, InstallHarness.Token)).KeptFiles.Should()
            .Equal(new KeptFile("d/state.dat", KeptReason.OriginalUnknown));

        state.InheritedFrom.Should().Be(d);
        state.OriginalUncertain.Should().BeTrue();
        // The store's check brings back the files kept after the update, and their copies can go.
        foreach (var path in new[] { "a/list.dat", "c/list.dat", "d/list.dat" })
        {
            File.Copy(Path.Join(saved, "Before", path), harness.GameFile(path), overwrite: true);
        }

        foreach (var kept in new[] { a, c, d })
        {
            (await engine.RemoveLeftoverBackupsAsync(kept, InstallHarness.Token)).Should().BeTrue();
        }

        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().OnlyContain(path => path.StartsWith(harness.LeftoversDirectory, StringComparison.Ordinal),
            "only the markers of uncertain originals stay, for a later install to take over");
    }

    [Fact]
    public async Task A_file_list_from_a_newer_app_makes_its_install_unreadable()
    {
        await using var harness = new InstallHarness();
        var result = await harness.InstallAsync(harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        var list = Path.Join(harness.InstallsDirectory, result.Record.Id.ToString("N"), "files.json");
        await File.WriteAllTextAsync(list, (await File.ReadAllTextAsync(list, InstallHarness.Token))
            .Replace("\"formatVersion\":1", "\"formatVersion\":3", StringComparison.Ordinal), InstallHarness.Token);

        var uninstall = () => harness.UninstallAsync(result.Record.Id);

        (await uninstall.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should()
            .Equal(new UnreadableInstallIssue(result.Record.Id));
        harness.ReadGameFile("readme.txt").Should().Be("Changed");
    }

    [Fact]
    public async Task Leaves_an_original_the_store_put_back_while_an_uninstall_was_cut_short()
    {
        await using var harness = new InstallHarness();
        var result = await harness.InstallAsync(harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        harness.CrashBefore(ChangeKind.Restore);
        await harness.UninstallAsync(result.Record.Id);
        harness.RestoreCrash();
        harness.WriteGameFile("readme.txt", "Read me");

        var engine = harness.CreateEngine();
        var recovered = await engine.RecoverAsync(InstallHarness.Token);

        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.RolledBack);
        harness.ReadGameFile("readme.txt").Should().Be("Read me");
        (await engine.VerifyAsync(result.Record.Id, null, InstallHarness.Token)).Files.Should().ContainSingle()
            .Which.Integrity.Should().Be(FileIntegrity.OriginalRestored);
    }

    [Fact]
    public async Task Waits_for_the_game_drive_to_tidy_up_after_an_uninstall_that_committed()
    {
        await using var harness = new InstallHarness();
        var before = harness.Snapshot();
        var result = await harness.InstallAsync(harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        harness.CrashBefore(ChangeKind.Tidy);
        await harness.UninstallAsync(result.Record.Id);
        harness.RestoreCrash();
        var unplugged = Path.Join(harness.Crashes, "WholeGame");
        TreeCopy.Copy(harness.GameRoot, unplugged);
        TreeCopy.Delete(harness.GameRoot);
        harness.Volumes.Detached.Add(InstallHarness.GameVolume);

        var waiting = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);
        TreeCopy.Copy(unplugged, harness.GameRoot);
        harness.Volumes.Detached.Remove(InstallHarness.GameVolume);
        var recovered = await harness.CreateEngine().RecoverAsync(InstallHarness.Token);

        waiting.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.Unavailable);
        recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.CleanedUp);
        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().BeEmpty();
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

    internal static InstallHarness Harness(string location)
    {
        var harness = InstallHarness.At(location);
        File.SetAttributes(harness.GameFile("readme.txt"), FileAttributes.ReadOnly);
        return harness;
    }

    internal static TestPackage FullPackage(TestPatch patch) => new TestPackage()
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
