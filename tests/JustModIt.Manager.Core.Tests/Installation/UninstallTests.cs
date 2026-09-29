using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class UninstallTests : IAsyncDisposable
{
    private readonly InstallHarness _harness = new();
    private readonly Dictionary<string, string> _before;

    public UninstallTests() => _before = _harness.Snapshot();

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Never_overwrites_a_file_the_game_changed_since()
    {
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed").Copy("new.txt", "New"));
        _harness.WriteGameFile("readme.txt", "Updated by the store");
        _harness.WriteGameFile("new.txt", "Also updated");

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().BeEquivalentTo([
            new KeptFile("readme.txt", KeptReason.ChangedSinceInstall),
            new KeptFile("new.txt", KeptReason.ChangedSinceInstall)]);
        _harness.ReadGameFile("readme.txt").Should().Be("Updated by the store");
        _harness.ReadGameFile("new.txt").Should().Be("Also updated");
        _harness.AppFiles().Should().BeEmpty("the record and its now useless backups are gone");
    }

    [Fact]
    public async Task Notices_a_change_that_keeps_the_size_and_time()
    {
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        var path = _harness.GameFile("readme.txt");
        var time = File.GetLastWriteTimeUtc(path);
        await File.WriteAllTextAsync(path, "Chan9ed", InstallHarness.Token);
        File.SetLastWriteTimeUtc(path, time.AddSeconds(1));

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().Equal(new KeptFile("readme.txt", KeptReason.ChangedSinceInstall));
    }

    [Fact]
    public async Task Keeps_a_deleted_file_that_came_back_with_other_content()
    {
        var result = await InstallAsync(new TestPackage().Delete("movies/intro.bik"));
        _harness.WriteGameFile("movies/intro.bik", "a new video");

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().Equal(new KeptFile("movies/intro.bik", KeptReason.ChangedSinceInstall));
        _harness.ReadGameFile("movies/intro.bik").Should().Be("a new video");
    }

    [Fact]
    public async Task Leaves_originals_the_store_restored_and_puts_back_missing_ones()
    {
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed").Copy("bin/library.dll", "proxy"));
        _harness.WriteGameFile("readme.txt", "Read me");
        File.Delete(_harness.GameFile("bin/library.dll"));

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().BeEmpty();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task Keeps_a_created_folder_that_holds_the_players_files()
    {
        var result = await InstallAsync(new TestPackage().Copy("mods/pack/text.txt", "Text"));
        _harness.WriteGameFile("mods/pack/notes.txt", "mine");

        await _harness.UninstallAsync(result.Record.Id);

        File.Exists(_harness.GameFile("mods/pack/text.txt")).Should().BeFalse();
        _harness.ReadGameFile("mods/pack/notes.txt").Should().Be("mine");
    }

    [Theory]
    [InlineData(false, KeptReason.BackupMissing)]
    [InlineData(true, KeptReason.BackupDamaged)]
    public async Task Keeps_the_installed_file_when_its_backup_is_missing_or_damaged(bool damaged, KeptReason reason)
    {
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        var backup = Path.Join(result.Record.Backup.Directory, "0");
        if (damaged)
        {
            await File.WriteAllTextAsync(backup, "Read m3", InstallHarness.Token);
        }
        else
        {
            File.Delete(backup);
        }

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().Equal(new KeptFile("readme.txt", reason));
        _harness.ReadGameFile("readme.txt").Should().Be("Changed");
    }

    [Fact]
    public async Task Refuses_while_a_file_is_locked_or_the_game_runs()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows locks files.");
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));

        FileLockedIssue locked;
        await using (new FileStream(_harness.GameFile("readme.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            locked = (InstallBlockedException(await UninstallFailureAsync(result.Record.Id)).Issues.Should().ContainSingle().Which
                as FileLockedIssue)!;
        }

        _harness.Probe.Runs("Example.exe");
        _harness.Probe.State = GameInstallState.Running;
        var running = await UninstallFailureAsync(result.Record.Id);

        locked.Should().Be(new FileLockedIssue("readme.txt", ["Example.exe"]));
        InstallBlockedException(running).Issues.Should().Equal(new GameRunningIssue("Example.exe"));
        _harness.ReadGameFile("readme.txt").Should().Be("Changed");
    }

    [Fact]
    public async Task Puts_originals_back_while_an_update_waits()
    {
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        _harness.Probe.State = GameInstallState.UpdatePending;

        await _harness.UninstallAsync(result.Record.Id);

        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Theory]
    [InlineData(GameInstallState.Updating)]
    [InlineData(GameInstallState.Validating)]
    [InlineData(GameInstallState.Unknown)]
    public async Task Refuses_while_the_store_writes_or_does_not_say(GameInstallState state)
    {
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        _harness.Probe.State = state;

        var failure = InstallBlockedException(await UninstallFailureAsync(result.Record.Id));

        failure.Issues.Should().Equal(new GameNotReadyIssue(state, GameStateReason.None));
        _harness.ReadGameFile("readme.txt").Should().Be("Changed");
    }

    [Fact]
    public async Task Refuses_when_the_store_starts_an_update_while_the_files_are_checked()
    {
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        var progress = new InlineProgress<InstallProgress>(report =>
        {
            if (report.Stage == InstallStage.Checking)
            {
                _harness.Probe.State = GameInstallState.Updating;
            }
        });

        var uninstall = () => _harness.Engine.UninstallAsync(result.Record.Id, progress, InstallHarness.Token);

        (await uninstall.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should()
            .Equal(new GameNotReadyIssue(GameInstallState.Updating, GameStateReason.None));
        _harness.ReadGameFile("readme.txt").Should().Be("Changed");
    }

    [Fact]
    public async Task Refuses_to_copy_originals_back_onto_a_drive_without_room_for_them()
    {
        _harness.PutBackupsOnAnotherVolume();
        _harness.Backup = InstallHarness.InAppData;
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        _harness.Volumes.Available[InstallHarness.GameVolume] = InstallPreflight.SpaceMargin;

        var failure = InstallBlockedException(await UninstallFailureAsync(result.Record.Id));

        failure.Issues.Should().ContainSingle().Which.Should().BeOfType<NotEnoughSpaceIssue>();
        _harness.ReadGameFile("readme.txt").Should().Be("Changed");
    }

    [Fact]
    public async Task Moves_originals_back_onto_a_full_drive()
    {
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        _harness.Volumes.Available[InstallHarness.GameVolume] = 0;

        await _harness.UninstallAsync(result.Record.Id);

        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task Undoes_itself_when_cancelled_part_way()
    {
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed").Copy("bin/library.dll", "proxy").Copy("a.txt", "a"));
        var installed = _harness.Snapshot();
        using var cancellation = new CancellationTokenSource();
        var restores = 0;
        _harness.BeforeChange = (kind, _) =>
        {
            if (kind == ChangeKind.Restore && ++restores == 2)
            {
                cancellation.Cancel();
            }
        };

        var cancelled = () => _harness.UninstallAsync(result.Record.Id, cancellation.Token);

        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        _harness.Snapshot().Should().BeEquivalentTo(installed);
        _harness.BeforeChange = null;
        (await _harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().BeEmpty();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Undoes_itself_when_a_change_fails(bool otherDrive)
    {
        if (otherDrive)
        {
            _harness.PutBackupsOnAnotherVolume();
            _harness.Backup = InstallHarness.InAppData;
        }

        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed").Delete("movies/intro.bik")
            .Copy("mods/new.txt", "New"));
        var installed = _harness.Snapshot();
        _harness.BeforeChange = (kind, path) =>
        {
            if (kind == ChangeKind.Restore && path == "movies/intro.bik")
            {
                throw new IOException("The disk went away.");
            }
        };

        var uninstall = () => _harness.UninstallAsync(result.Record.Id);

        (await uninstall.Should().ThrowAsync<InstallFailedException>()).Which.RolledBack.Should().BeTrue();
        _harness.Snapshot().Should().BeEquivalentTo(installed);
        _harness.BeforeChange = null;
        await _harness.UninstallAsync(result.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task Refuses_a_missing_game_folder_and_lets_the_record_be_forgotten()
    {
        _harness.Backup = InstallHarness.InAppData;
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        TreeCopy.Delete(_harness.GameRoot);

        var failure = InstallBlockedException(await UninstallFailureAsync(result.Record.Id));
        await _harness.Engine.ForgetAsync(result.Record.Id, InstallHarness.Token);

        failure.Issues.Should().Equal(new GameFolderMissingIssue(result.Record.InstallDirectory));
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().BeEmpty();
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_while_the_backup_folder_is_not_there_and_never_recreates_it()
    {
        _harness.PutBackupsOnAnotherVolume();
        _harness.Backup = _harness.InCustomFolder;
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        var unplugged = _harness.CustomFolder + "-unplugged";
        Directory.Move(_harness.CustomFolder, unplugged);

        var failure = InstallBlockedException(await UninstallFailureAsync(result.Record.Id));
        var prediction = await _harness.Engine.PredictReplacementAsync(result.Record.Id,
            _harness.Request(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Newer"))), null, InstallHarness.Token);

        failure.Issues.Should().Equal(new BackupFolderUnavailableIssue(result.Record.Backup.Directory));
        prediction.Issues.Should().Contain(new BackupFolderUnavailableIssue(result.Record.Backup.Directory));
        Directory.Exists(_harness.CustomFolder).Should().BeFalse();
        Directory.Move(unplugged, _harness.CustomFolder);
        await _harness.UninstallAsync(result.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task Refuses_an_install_it_has_no_record_of()
    {
        var id = Guid.NewGuid();

        var failure = InstallBlockedException(await UninstallFailureAsync(id));

        failure.Issues.Should().Equal(new InstallNotFoundIssue(id));
    }

    [Fact]
    public async Task Refuses_a_record_written_by_a_newer_app()
    {
        var result = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        var record = Path.Join(_harness.InstallsDirectory, result.Record.Id.ToString("N"), "record.json");
        await File.WriteAllTextAsync(record, (await File.ReadAllTextAsync(record, InstallHarness.Token))
            .Replace("\"formatVersion\": 1", "\"formatVersion\": 2", StringComparison.Ordinal), InstallHarness.Token);

        var failure = InstallBlockedException(await UninstallFailureAsync(result.Record.Id));
        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("other.txt", "x")));

        failure.Issues.Should().Equal(new UnreadableInstallIssue(result.Record.Id));
        blocked.Issues.Should().Equal(new UnreadableInstallIssue(result.Record.Id));
    }

    private Task<InstallResult> InstallAsync(TestPackage package) => _harness.InstallAsync(_harness.ManifestPackage(package));

    private async Task<Exception> UninstallFailureAsync(Guid recordId)
    {
        var uninstall = () => _harness.UninstallAsync(recordId);
        return (await uninstall.Should().ThrowAsync<Exception>()).Which;
    }

    private static InstallBlockedException InstallBlockedException(Exception exception) =>
        exception.Should().BeOfType<InstallBlockedException>().Which;
}
