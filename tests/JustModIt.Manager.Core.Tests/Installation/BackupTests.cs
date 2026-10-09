using System.Runtime.Versioning;
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
    [SupportedOSPlatform("windows")]
    public async Task Undoes_a_move_at_once_when_its_journal_cannot_be_written()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses writes to a locked range.");
        await using var harness = Harness("game folder", "app data");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        using (harness.FailJournalWritesFrom(ChangeKind.Commit))
        {
            var move = () => harness.Engine.MoveBackupsAsync(result.Record.Id, InstallHarness.InAppData, null, InstallHarness.Token);

            await move.Should().ThrowAsync<IOException>();
        }

        harness.BeforeChange = null;
        await BackupsShouldHoldTheOriginalsAsync(harness);
        var moved = await harness.Engine.MoveBackupsAsync(result.Record.Id, InstallHarness.InAppData, null, InstallHarness.Token);
        moved.Backup.Location.Should().Be(BackupLocation.AppData);
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
    public async Task Refuses_to_move_backups_to_a_drive_without_room_for_them_and_the_margin()
    {
        await using var harness = Harness("game folder", "custom folder on another drive");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        harness.Volumes.Available[InstallHarness.OtherVolume] = InstallPreflight.SpaceMargin;

        var move = () => harness.Engine.MoveBackupsAsync(result.Record.Id, harness.InCustomFolder, null, InstallHarness.Token);

        (await move.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should().ContainSingle()
            .Which.Should().BeOfType<NotEnoughSpaceIssue>();
        (await harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which.Should().Be(result.Record);
    }

    [Fact]
    public async Task Keeps_the_originals_of_an_install_forgotten_with_its_game_until_the_player_removes_them()
    {
        await using var harness = Harness("app data on another drive", "game folder");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        var stray = Path.Join(result.Record.Backup.Directory, "notes.txt");
        await File.WriteAllTextAsync(stray, "the player's own", InstallHarness.Token);
        TreeCopy.Delete(harness.GameRoot);

        var outcome = await harness.Engine.ForgetIfGoneAsync(result.Record.Id, keepBackups: true, InstallHarness.Token);
        var restarted = harness.CreateEngine();
        await restarted.RecoverAsync(InstallHarness.Token);
        var leftovers = await restarted.FindLeftoverBackupsAsync(InstallHarness.Token);
        var removed = await restarted.RemoveLeftoverBackupsAsync(result.Record.Id, InstallHarness.Token);

        outcome.Should().Be(ForgetOutcome.ForgottenKeepingBackups);
        (await restarted.LoadRecordsAsync(InstallHarness.Token)).Should().BeEmpty();
        leftovers.Should().Equal(new LeftoverBackup(result.Record.Id, InstallHarness.Key, "Example Game",
            result.Record.Backup.Directory, "original library".Length + "Read me".Length + "video".Length));
        removed.Should().BeTrue();
        Directory.EnumerateFiles(result.Record.Backup.Directory).Should().Equal(stray);
        (await restarted.FindLeftoverBackupsAsync(InstallHarness.Token)).Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(harness.LeftoversDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task Lists_and_removes_leftover_originals_only_while_their_folder_is_there()
    {
        await using var harness = Harness("custom folder", "app data");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        TreeCopy.Delete(harness.GameRoot);
        await harness.Engine.ForgetIfGoneAsync(result.Record.Id, keepBackups: true, InstallHarness.Token);
        var unplugged = harness.CustomFolder + "-unplugged";
        Directory.Move(harness.CustomFolder, unplugged);

        var whileUnplugged = await harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token);
        var removedWhileUnplugged = await harness.Engine.RemoveLeftoverBackupsAsync(result.Record.Id, InstallHarness.Token);
        Directory.Move(unplugged, harness.CustomFolder);

        whileUnplugged.Should().BeEmpty();
        removedWhileUnplugged.Should().BeFalse();
        (await harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token)).Should().ContainSingle()
            .Which.RecordId.Should().Be(result.Record.Id);
    }

    [Fact]
    public async Task Skips_leftover_originals_whose_file_list_cannot_be_read()
    {
        await using var harness = Harness("app data on another drive", "game folder");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        TreeCopy.Delete(harness.GameRoot);
        await harness.Engine.ForgetIfGoneAsync(result.Record.Id, keepBackups: true, InstallHarness.Token);
        await File.WriteAllTextAsync(Path.Join(harness.LeftoversDirectory, result.Record.Id.ToString("N"), "files.json"), "{",
            InstallHarness.Token);

        var leftovers = await harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token);

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
