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
        await using var harness = InstallHarness.At(from, to);
        var before = harness.Snapshot();
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));

        var moved = await harness.Engine.MoveBackupsAsync(result.Record.Id, harness.Choice(to), null, InstallHarness.Token);

        moved.Backup.Location.Should().Be(harness.Choice(to).Location);
        Directory.Exists(result.Record.Backup.Directory).Should().BeFalse();
        (await harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which.Should().Be(moved);
        await BackupsShouldHoldTheOriginalsAsync(harness);
        (await harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().BeEmpty();
        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Moves))]
    public async Task Moving_backups_carries_the_copies_of_declared_files(string from, string to)
    {
        await using var harness = RuntimeFileTests.WithList(InstallHarness.At(from, to));
        var before = harness.Snapshot();
        var result = await harness.InstallAsync(harness.ManifestPackage(RuntimeFileTests.Software()));

        await harness.Engine.MoveBackupsAsync(result.Record.Id, harness.Choice(to), null, InstallHarness.Token);
        RuntimeFileTests.Play(harness);

        await BackupsShouldHoldTheOriginalsAsync(harness);
        (await harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().BeEmpty();
        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Forgetting_an_install_keeps_or_removes_the_copies_of_declared_files(bool keepBackups)
    {
        await using var harness = RuntimeFileTests.WithList(InstallHarness.At("app data"));
        var result = await harness.InstallAsync(harness.ManifestPackage(RuntimeFileTests.Software()));
        TreeCopy.Delete(harness.GameRoot);

        var outcome = await harness.Engine.ForgetIfGoneAsync(result.Record.Id, keepBackups, InstallHarness.Token);

        if (keepBackups)
        {
            outcome.Should().Be(ForgetOutcome.ForgottenKeepingBackups);
            (await harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token)).Should().ContainSingle()
                .Which.Bytes.Should().Be("Original list".Length);
        }
        else
        {
            outcome.Should().Be(ForgetOutcome.Forgotten);
            harness.AppFiles().Should().BeEmpty();
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Undoes_a_move_at_once_when_its_journal_cannot_be_written()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses writes to a locked range.");
        await using var harness = InstallHarness.At("game folder", "app data");
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
        await using var harness = InstallHarness.At("custom folder", "app data");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        var unplugged = harness.CustomFolder + "-unplugged";
        Directory.Move(harness.CustomFolder, unplugged);

        var move = () => harness.Engine.MoveBackupsAsync(result.Record.Id, InstallHarness.InAppData, null, InstallHarness.Token);

        (await move.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should()
            .Equal(new BackupFolderUnavailableIssue(result.Record.Backup.Directory));
        Directory.Move(unplugged, harness.CustomFolder);
    }

    [Fact]
    public async Task Refuses_to_move_backups_onto_a_drive_without_room_or_while_recovery_waits()
    {
        await using var harness = InstallHarness.At("game folder", "custom folder on another drive");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        harness.Volumes.Available[InstallHarness.OtherVolume] = InstallPreflight.SpaceMargin;
        var move = () => harness.Engine.MoveBackupsAsync(result.Record.Id, harness.InCustomFolder, null, InstallHarness.Token);

        (await move.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should().ContainSingle()
            .Which.Should().BeOfType<NotEnoughSpaceIssue>("the backups and the margin do not fit");
        harness.Volumes.Available.Clear();
        harness.CrashBefore(ChangeKind.Record);
        await harness.InstallAsync(harness.ManifestPackage(new TestPackage().Copy("data/new.txt", "New")));
        harness.RestoreCrash().Should().BeTrue();
        (await move.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should().ContainSingle()
            .Which.Should().BeOfType<RecoveryPendingIssue>();

        (await harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which.Should().Be(result.Record);
        Directory.EnumerateFileSystemEntries(harness.CustomFolder).Should().BeEmpty();
    }

    [Fact]
    public async Task Keeps_the_originals_of_an_install_forgotten_with_its_game_until_the_player_removes_them()
    {
        await using var harness = InstallHarness.At("app data on another drive", "game folder");
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
        leftovers.Should().Equal(new LeftoverBackup(result.Record.Id, TestProjects.Game, "Example Game",
            result.Record.Backup.Directory, "original library".Length + "Read me".Length + "video".Length));
        removed.Should().BeTrue();
        Directory.EnumerateFiles(result.Record.Backup.Directory).Should().Equal(stray);
        (await restarted.FindLeftoverBackupsAsync(InstallHarness.Token)).Should().BeEmpty();
        Directory.EnumerateFileSystemEntries(harness.LeftoversDirectory).Should().BeEmpty();
    }

    [Fact]
    public async Task Lists_and_removes_leftover_originals_only_while_their_folder_is_there()
    {
        await using var harness = InstallHarness.At("custom folder", "app data");
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
        await using var harness = InstallHarness.At("app data on another drive", "game folder");
        var result = await harness.InstallAsync(harness.ManifestPackage(Package()));
        TreeCopy.Delete(harness.GameRoot);
        await harness.Engine.ForgetIfGoneAsync(result.Record.Id, keepBackups: true, InstallHarness.Token);
        await File.WriteAllTextAsync(Path.Join(harness.LeftoversDirectory, result.Record.Id.ToString("N"), "files.json"), "{",
            InstallHarness.Token);

        var leftovers = await harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token);

        leftovers.Should().BeEmpty();
    }

    internal static async Task BackupsShouldHoldTheOriginalsAsync(InstallHarness harness)
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

    internal static TestPackage Package() => new TestPackage()
        .Rename("bin/library.dll", "bin/library_original.dll")
        .Copy("bin/library.dll", "proxy")
        .Copy("readme.txt", "Changed")
        .Delete("movies/intro.bik");
}
