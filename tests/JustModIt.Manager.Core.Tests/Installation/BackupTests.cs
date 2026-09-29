using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class BackupTests
{
    public static TheoryData<string, string> Moves => new()
    {
        { "game folder", "app data" },
        { "game folder", "custom folder on another drive" },
        { "app data on another drive", "game folder" },
        { "custom folder", "app data" },
    };

    [Theory]
    [MemberData(nameof(Moves))]
    public async Task Moves_backups_to_another_location_and_uninstalls_from_there(string from, string to)
    {
        await using var harness = Harness(from, to);
        var before = harness.Snapshot();
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));

        var moved = await harness.Engine.MoveBackupsAsync(result.Record.Id, Choice(harness, to), null, InstallHarness.Token);

        moved.Backup.Location.Should().Be(Choice(harness, to).Location);
        Directory.Exists(result.Record.Backup.Directory).Should().BeFalse();
        (await harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which.Should().Be(moved);
        await BackupsShouldHoldTheOriginalsAsync(harness);
        (await harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().BeEmpty();
        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Moves))]
    public async Task Survives_a_crash_at_any_step_of_a_move(string from, string to)
    {
        int changes;
        await using (var counting = Harness(from, to))
        {
            var installed = await counting.InstallAsync(counting.ManifestPackage(Package()));
            var count = counting.CountChanges();
            await counting.Engine.MoveBackupsAsync(installed.Record.Id, Choice(counting, to), null, InstallHarness.Token);
            changes = count();
        }

        changes.Should().BeGreaterThan(3);
        for (var change = 0; change < changes; change++)
        {
            await using var harness = Harness(from, to);
            var before = harness.Snapshot();
            var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
            harness.CrashAt(change);
            await harness.Engine.MoveBackupsAsync(result.Record.Id, Choice(harness, to), null, InstallHarness.Token);
            harness.RestoreCrash().Should().BeTrue();

            var engine = harness.CreateEngine();
            var recovered = await engine.RecoverAsync(InstallHarness.Token);

            recovered.Should().ContainSingle().Which.Outcome.Should().Be(RecoveryOutcome.CleanedUp, "crash at change {0}", change);
            await BackupsShouldHoldTheOriginalsAsync(harness);
            (await engine.UninstallAsync(result.Record.Id, null, InstallHarness.Token)).KeptFiles.Should()
                .BeEmpty("crash at change {0}", change);
            harness.Snapshot().Should().BeEquivalentTo(before, "crash at change {0}", change);
            harness.AppFiles().Should().BeEmpty("crash at change {0}", change);
        }
    }

    [Fact]
    public async Task Refuses_to_move_backups_from_a_folder_that_is_not_there()
    {
        await using var harness = Harness("custom folder", "app data");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        var unplugged = harness.CustomFolder + "-unplugged";
        Directory.Move(harness.CustomFolder, unplugged);

        var move = () => harness.Engine.MoveBackupsAsync(result.Record.Id, InstallHarness.InAppData, null, InstallHarness.Token);

        (await move.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should()
            .Equal(new BackupFolderUnavailableIssue(result.Record.Backup.Directory));
        Directory.Move(unplugged, harness.CustomFolder);
    }

    [Fact]
    public async Task Lists_backups_that_outlived_their_game_and_removes_only_what_the_record_lists()
    {
        await using var harness = Harness("app data on another drive", "game folder");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        var stray = Path.Join(result.Record.Backup.Directory, "notes.txt");
        await File.WriteAllTextAsync(stray, "the player's own", InstallHarness.Token);
        TreeCopy.Delete(harness.GameRoot);

        var none = await harness.Engine.FindLeftoverBackupsAsync(_ => false, InstallHarness.Token);
        var leftovers = await harness.Engine.FindLeftoverBackupsAsync(record => record.Game == InstallHarness.Key, InstallHarness.Token);
        await harness.Engine.ForgetAsync(result.Record.Id, InstallHarness.Token);

        none.Should().BeEmpty();
        leftovers.Should().Equal(new LeftoverBackup(result.Record.Id, InstallHarness.Key, "Example Game",
            result.Record.Backup.Directory, "original library".Length + "Read me".Length + "video".Length));
        File.Exists(stray).Should().BeTrue();
        Directory.EnumerateFiles(result.Record.Backup.Directory).Should().Equal(stray);
        (await harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task Skips_leftover_backups_whose_file_list_cannot_be_read()
    {
        await using var harness = Harness("app data on another drive", "game folder");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        TreeCopy.Delete(harness.GameRoot);
        await File.WriteAllTextAsync(Path.Join(harness.InstallsDirectory, result.Record.Id.ToString("N"), "files.json"), "{",
            InstallHarness.Token);

        var leftovers = await harness.Engine.FindLeftoverBackupsAsync(_ => true, InstallHarness.Token);

        leftovers.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_list_backups_kept_in_a_game_folder_that_still_exists()
    {
        await using var harness = Harness("app data", "game folder");
        await harness.InstallAsync(harness.ManifestPackage(Package()));

        var leftovers = await harness.Engine.FindLeftoverBackupsAsync(_ => true, InstallHarness.Token);

        leftovers.Should().BeEmpty();
    }

    private static async Task BackupsShouldHoldTheOriginalsAsync(InstallHarness harness)
    {
        var engine = harness.CreateEngine();
        var record = (await engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which;
        var files = await engine.LoadFilesAsync(record.Id, InstallHarness.Token);
        foreach (var file in files.Files.Where(file => file.Backup is not null))
        {
            var backup = await File.ReadAllBytesAsync(Path.Join(record.Backup.Directory, file.Backup), InstallHarness.Token);
            TestPackage.Sha256(backup).Should().Be(file.Original!.Sha256);
        }

        harness.AppFiles().Where(path => path.StartsWith(Path.Join(harness.AppData, "backups"), StringComparison.Ordinal)
                || path.StartsWith(harness.CustomFolder, StringComparison.Ordinal))
            .Should().OnlyContain(path => path.StartsWith(record.Backup.Directory, StringComparison.Ordinal)
                || record.Backup.Directory.StartsWith(path, StringComparison.Ordinal));
    }

    private static TestPackage Package() => new TestPackage()
        .Rename("bin/library.dll", "bin/library_original.dll")
        .Copy("bin/library.dll", "proxy")
        .Copy("readme.txt", "Changed")
        .Delete("movies/intro.bik");

    private static InstallHarness Harness(string from, string to)
    {
        var harness = new InstallHarness();
        if (from.EndsWith("another drive", StringComparison.Ordinal) || to.EndsWith("another drive", StringComparison.Ordinal))
        {
            harness.PutBackupsOnAnotherVolume();
        }

        harness.Backup = Choice(harness, from);
        return harness;
    }

    private static BackupChoice Choice(InstallHarness harness, string location) => location switch
    {
        _ when location.StartsWith("app data", StringComparison.Ordinal) => InstallHarness.InAppData,
        _ when location.StartsWith("custom", StringComparison.Ordinal) => harness.InCustomFolder,
        _ => BackupChoice.GameFolder,
    };
}
