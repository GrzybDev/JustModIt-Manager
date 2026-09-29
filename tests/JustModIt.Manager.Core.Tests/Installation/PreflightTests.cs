using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class PreflightTests : IAsyncDisposable
{
    private readonly InstallHarness _harness = new();

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Refuses_a_patch_for_another_version_of_the_game()
    {
        var patch = await TestPatch.CreateAsync(TestPatch.RandomBytes(1000, seed: 5), TestPatch.RandomBytes(1000, seed: 6));

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Patch("data/strings.pak", patch)));

        blocked.Issues.Should().Equal(new FileMismatchIssue("data/strings.pak", AlreadyPatched: false));
    }

    [Fact]
    public async Task Tells_that_a_file_is_already_patched()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        _harness.WriteGameFile("data/strings.pak", InstallHarness.ChangedPak);

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Patch("data/strings.pak", patch)));

        blocked.Issues.Should().Equal(new FileMismatchIssue("data/strings.pak", AlreadyPatched: true));
    }

    [Fact]
    public async Task Checks_a_patch_against_the_content_an_earlier_step_moved_there()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var package = _harness.ManifestPackage(new TestPackage()
            .Rename("data/strings.pak", "data/strings_original.pak")
            .Patch("data/strings_original.pak", patch));

        var result = await _harness.InstallAsync(package);

        (await File.ReadAllBytesAsync(_harness.GameFile("data/strings_original.pak"), InstallHarness.Token))
            .Should().Equal(InstallHarness.ChangedPak);
        File.Exists(_harness.GameFile("data/strings.pak")).Should().BeFalse();
        result.Record.FileCount.Should().Be(2);
        await _harness.UninstallAsync(result.Record.Id);
        (await File.ReadAllBytesAsync(_harness.GameFile("data/strings.pak"), InstallHarness.Token))
            .Should().Equal(InstallHarness.OriginalPak);
    }

    [Fact]
    public async Task Refuses_to_patch_or_rename_a_missing_file()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage()
            .Patch("data/missing.pak", patch)
            .Rename("bin/missing.dll", "bin/other.dll")));

        blocked.Issues.Should().Equal(new MissingFileIssue("data/missing.pak"), new MissingFileIssue("bin/missing.dll"));
    }

    [Fact]
    public async Task Names_the_programs_holding_files_it_would_change()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows locks files.");
        await using var held = new FileStream(_harness.GameFile("bin/library.dll"), FileMode.Open, FileAccess.Read, FileShare.None);

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("bin/library.dll", "proxy")));

        blocked.Issues.Should().Equal(new FileLockedIssue("bin/library.dll", ["Example.exe"]));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Asks_for_permission_where_the_user_may_not_write()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The test denies access with a Windows ACL.");
        var data = new DirectoryInfo(_harness.GameFile("data"));
        using (DenyCreatingFiles(data))
        {
            var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("data/new.txt", "new")));

            blocked.Issues.Should().Equal(new PermissionRequiredIssue("data"));
        }
    }

    [Fact]
    public async Task Refuses_when_the_disk_is_too_full()
    {
        _harness.Volumes.Available[InstallHarness.GameVolume] = 1000;

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));

        blocked.Issues.Should().ContainSingle().Which.Should().BeOfType<NotEnoughSpaceIssue>()
            .Which.AvailableBytes.Should().Be(1000);
    }

    [Fact]
    public async Task Counts_the_space_for_copies_of_originals_on_their_own_drive()
    {
        _harness.PutBackupsOnAnotherVolume();
        _harness.Volumes.Available[InstallHarness.OtherVolume] = 1000;

        var blocked = await _harness.Engine.CheckInstallAsync(
            _harness.Request(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "new")), InstallHarness.InAppData), null,
            InstallHarness.Token);
        var added = await _harness.Engine.CheckInstallAsync(
            _harness.Request(_harness.ManifestPackage(new TestPackage().Copy("new.txt", "new")), InstallHarness.InAppData), null,
            InstallHarness.Token);

        blocked.Issues.Should().ContainSingle().Which.Should().BeOfType<NotEnoughSpaceIssue>()
            .Which.Location.Should().StartWith(_harness.AppData);
        added.CanProceed.Should().BeTrue("nothing is copied there");
    }

    [Fact]
    public async Task Counts_the_space_for_the_copy_a_renamed_original_leaves_at_its_new_name()
    {
        _harness.Volumes.Available[InstallHarness.GameVolume] = InstallPreflight.SpaceMargin + (InstallHarness.OriginalPak.Length / 2);

        var renamed = await _harness.Engine.CheckInstallAsync(
            _harness.Request(_harness.ManifestPackage(new TestPackage().Rename("data/strings.pak", "data/strings_original.pak"))), null,
            InstallHarness.Token);
        var added = await _harness.Engine.CheckInstallAsync(
            _harness.Request(_harness.ManifestPackage(new TestPackage().Copy("new.txt", "new"))), null, InstallHarness.Token);

        renamed.Issues.Should().ContainSingle().Which.Should().BeOfType<NotEnoughSpaceIssue>();
        added.CanProceed.Should().BeTrue();
    }

    [Fact]
    public async Task Refuses_when_the_store_starts_an_update_while_the_package_is_checked()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var package = _harness.ManifestPackage(new TestPackage().Patch("data/strings.pak", patch));
        var before = _harness.Snapshot();
        var progress = new InlineProgress<InstallProgress>(report =>
        {
            if (report is { Stage: InstallStage.Checking, BytesDone: > 0 })
            {
                _harness.Probe.State = GameInstallState.Updating;
            }
        });

        var install = () => _harness.Engine.InstallAsync(_harness.Request(package), progress, InstallHarness.Token);

        (await install.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should()
            .Equal(new GameNotReadyIssue(GameInstallState.Updating, GameStateReason.None));
        _harness.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Refuses_paths_that_a_link_takes_out_of_the_game()
    {
        using var outside = new TempDirectory();
        using var links = new TempDirectory();
        var link = links.CreateDirectoryLink("escape", outside.Path);
        Directory.Move(link, _harness.GameFile("escape"));

        try
        {
            var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("escape/evil.dll", "evil")));

            blocked.Issues.Should().Equal(new UnsafePathIssue("escape/evil.dll"));
            Directory.EnumerateFileSystemEntries(outside.Path).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(_harness.GameFile("escape"));
        }
    }

    [Fact]
    public async Task Refuses_paths_inside_the_apps_own_folder()
    {
        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy(".Example-App/backups/x", "x")));

        blocked.Issues.Should().Equal(new UnsafePathIssue(".Example-App/backups/x"));
    }

    [Fact]
    public async Task Refuses_a_file_where_a_folder_stands_and_a_folder_where_a_file_stands()
    {
        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage()
            .Copy("data", "file over a folder")
            .Copy("readme.txt/inside.txt", "folder over a file")
            .Copy("new/inner.txt", "creates a folder")
            .Copy("new", "file over that folder")
            .Delete("movies")));

        blocked.Issues.Should().Equal(new PathConflictIssue("data"), new PathConflictIssue("readme.txt/inside.txt"),
            new PathConflictIssue("new"), new PathConflictIssue("movies"));
    }

    [Fact]
    public async Task Refuses_files_another_projects_install_owns()
    {
        var first = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "first")));

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "second")));

        blocked.Issues.Should().Equal(new FileOwnedByOtherInstallIssue("readme.txt", first.Record.Id));
        _harness.ReadGameFile("readme.txt").Should().Be("first");
    }

    [Fact]
    public async Task Refuses_while_the_game_runs_or_its_store_works_on_it()
    {
        _harness.Probe.Runs("Example.exe", "Helper.exe");
        _harness.Probe.State = GameInstallState.Updating;
        _harness.Probe.Reason = GameStateReason.DownloadInProgress;

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));

        blocked.Issues.Should().Equal(new GameRunningIssue("Example.exe"), new GameRunningIssue("Helper.exe"),
            new GameNotReadyIssue(GameInstallState.Updating, GameStateReason.DownloadInProgress));
    }

    [Theory]
    [InlineData(GameInstallState.Unknown)]
    [InlineData(GameInstallState.UpdatePending)]
    [InlineData(GameInstallState.Validating)]
    [InlineData(GameInstallState.Broken)]
    [InlineData(GameInstallState.Offline)]
    public async Task Refuses_unless_the_store_reports_the_game_installed_and_idle(GameInstallState state)
    {
        _harness.Probe.State = state;

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));

        blocked.Issues.Should().Equal(new GameNotReadyIssue(state, GameStateReason.None));
    }

    [Fact]
    public async Task Refuses_a_game_the_store_no_longer_lists_or_could_not_read()
    {
        _harness.Probe.IsGone = true;
        var gone = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));
        _harness.Probe.IsGone = false;
        _harness.Probe.IsStale = true;
        var stale = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));

        gone.Issues.Should().Equal(new GameNotInstalledIssue());
        stale.Issues.Should().Equal(new GameNotReadyIssue(GameInstallState.Unknown, GameStateReason.UnreadableStoreData));
    }

    [Fact]
    public async Task Refuses_a_game_folder_that_is_gone()
    {
        var install = () => _harness.Engine.InstallAsync(
            _harness.Request(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a"))) with
            {
                Game = _harness.Game with { InstallDirectory = Path.Join(_harness.GameRoot, "missing") },
            }, null, InstallHarness.Token);

        (await install.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should()
            .Equal(new GameFolderMissingIssue(Path.Join(_harness.GameRoot, "missing")));
    }

    [Fact]
    public async Task Refuses_a_broken_package_before_looking_at_the_game()
    {
        var package = _harness.Zip(new TestPackage().Entry("../evil.dll", "evil"));

        var install = () => _harness.Engine.InstallAsync(_harness.Request(package) with
        {
            Game = _harness.Game with { InstallDirectory = Path.Join(_harness.GameRoot, "missing") },
        }, null, InstallHarness.Token);

        (await install.Should().ThrowAsync<InvalidPackageException>()).Which.Error.Should().Be(PackageError.UnsafeEntryName);
    }

    [Fact]
    public async Task Refuses_a_custom_backup_folder_that_is_not_there_and_never_creates_it()
    {
        var missing = Path.Join(_harness.CustomFolder, "unplugged");
        _harness.Backup = new BackupChoice(BackupLocation.CustomFolder, missing);

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));

        blocked.Issues.Should().Equal(new BackupFolderUnavailableIssue(missing));
        Directory.Exists(missing).Should().BeFalse();
    }

    [Fact]
    public async Task Refuses_while_another_copy_of_the_app_works_on_the_game()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows locks files.");
        Directory.CreateDirectory(_harness.HiddenDirectory);
        await using (new FileStream(Path.Join(_harness.HiddenDirectory, "lock"), FileMode.Create, FileAccess.ReadWrite,
                         FileShare.None, 1, FileOptions.DeleteOnClose))
        {
            var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));

            blocked.Issues.Should().Equal(new GameBusyIssue());
        }

        Directory.Delete(_harness.HiddenDirectory);
    }

    [Fact]
    public async Task Reports_every_problem_at_once()
    {
        _harness.Probe.Runs("Example.exe");
        var patch = await TestPatch.CreateAsync(TestPatch.RandomBytes(100, seed: 5), TestPatch.RandomBytes(100, seed: 6));

        var blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage()
            .Patch("data/strings.pak", patch)
            .Rename("bin/missing.dll", "bin/x.dll")
            .Copy("data", "conflict")));

        blocked.Issues.Should().HaveCount(4);
        _harness.AppFiles().Should().BeEmpty();
    }

    [SupportedOSPlatform("windows")]
    private static Restore DenyCreatingFiles(DirectoryInfo directory)
    {
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, AccessControlType.Deny);
        var security = directory.GetAccessControl();
        security.AddAccessRule(rule);
        directory.SetAccessControl(security);
        return new Restore(() =>
        {
            var restored = directory.GetAccessControl();
            restored.RemoveAccessRule(rule);
            directory.SetAccessControl(restored);
        });
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
