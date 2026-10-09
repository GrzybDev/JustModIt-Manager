using System.Diagnostics;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class InstallTests : IAsyncDisposable
{
    private readonly InstallHarness _harness = new();

    public static TheoryData<string> Locations => ["game folder", "app data", "custom folder", "app data on another drive",
        "custom folder on another drive"];

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Theory]
    [MemberData(nameof(Locations))]
    public async Task Installs_and_uninstalls_every_kind_of_change_wherever_backups_live(string location)
    {
        await using var harness = InstallHarness.At(location);
        var before = harness.Snapshot();
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var package = harness.ManifestPackage(new TestPackage()
            .Rename("bin/library.dll", "bin/library_original.dll")
            .Copy("bin/library.dll", "proxy library")
            .Patch("data/strings.pak", patch)
            .Delete("movies/intro.bik")
            .Copy("data/lang/extra/text.txt", "Text"));

        var result = await harness.InstallAsync(package);

        harness.ReadGameFile("bin/library.dll").Should().Be("proxy library");
        harness.ReadGameFile("bin/library_original.dll").Should().Be("original library");
        (await File.ReadAllBytesAsync(harness.GameFile("data/strings.pak"), InstallHarness.Token)).Should().Equal(InstallHarness.ChangedPak);
        File.Exists(harness.GameFile("movies/intro.bik")).Should().BeFalse();
        var files = await harness.Engine.LoadFilesAsync(result.Record.Id, InstallHarness.Token);
        files.Files.Select(file => (file.Path, file.Change)).Should().Equal(
            ("bin/library.dll", FileChange.Replaced),
            ("bin/library_original.dll", FileChange.Added),
            ("data/strings.pak", FileChange.Replaced),
            ("movies/intro.bik", FileChange.Deleted),
            ("data/lang/extra/text.txt", FileChange.Added));
        files.CreatedDirectories.Should().Equal("data/lang", "data/lang/extra");
        Directory.Exists(Path.Join(harness.HiddenDirectory, "staging")).Should().BeFalse();
        File.Exists(Path.Join(harness.InstallsDirectory, result.Record.Id.ToString("N"), "journal.jsonl")).Should().BeFalse();
        (await harness.Engine.VerifyAsync(result.Record.Id, null, InstallHarness.Token)).IsIntact.Should().BeTrue();

        (await harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().BeEmpty();

        harness.Snapshot().Should().BeEquivalentTo(before);
        harness.AppFiles().Should().BeEmpty();
    }

    [Theory]
    [InlineData(BackupLocation.GameFolder, ".example-app/backups")]
    [InlineData(BackupLocation.AppData, "backups/steam-1000")]
    [InlineData(BackupLocation.CustomFolder, "ExampleApp/steam-1000")]
    public async Task Keeps_originals_in_a_folder_of_the_install_in_the_chosen_location(BackupLocation location, string folder)
    {
        _harness.Backup = location == BackupLocation.CustomFolder ? _harness.InCustomFolder : new BackupChoice(location);

        var result = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));

        var root = location switch
        {
            BackupLocation.GameFolder => _harness.GameRoot,
            BackupLocation.AppData => _harness.AppData,
            _ => _harness.CustomFolder,
        };
        result.Record.Backup.Should().Be(new BackupFolder(location,
            Path.Join(PackagePath.ToFullPath(root, folder), result.Record.Id.ToString("N"))));
        File.ReadAllText(Path.Join(result.Record.Backup.Directory, "0")).Should().Be("Read me");
        (File.GetAttributes(_harness.HiddenDirectory) & FileAttributes.Hidden).Should().Be(FileAttributes.Hidden);
        if (location != BackupLocation.GameFolder)
        {
            Directory.EnumerateFileSystemEntries(_harness.HiddenDirectory).Select(Path.GetFileName).Should().Equal(
                [AppFolders.OwnerFileName], "staging files are removed once the install commits, while the owner file stays");
        }
    }

    [Theory]
    [InlineData("app data")]
    [InlineData("app data on another drive")]
    public async Task Moves_originals_on_their_drive_copies_them_to_another_and_restores_their_time(string location)
    {
        await using var harness = InstallHarness.At(location);
        var written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(harness.GameFile("readme.txt"), written);
        File.SetLastWriteTimeUtc(harness.GameFile("readme.txt"), written);

        var result = await harness.InstallAsync(harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        var backup = Path.Join(result.Record.Backup.Directory, "0");

        File.ReadAllText(backup).Should().Be("Read me");
        // A move keeps the file itself, while a copy is a new file.
        (File.GetCreationTimeUtc(backup) == written).Should().Be(location == "app data");
        await harness.UninstallAsync(result.Record.Id);
        File.GetLastWriteTimeUtc(harness.GameFile("readme.txt")).Should().Be(written);
    }

    [Fact]
    public async Task Installs_a_plain_zip_over_the_game_folder()
    {
        var before = _harness.Snapshot();

        var result = await _harness.InstallAsync(_harness.Zip(new TestPackage().Entry("readme.txt", "Changed").Entry("data/new.txt", "New")));

        result.Record.Package.Kind.Should().Be(PackageKind.Zip);
        _harness.ReadGameFile("readme.txt").Should().Be("Changed");
        _harness.ReadGameFile("data/new.txt").Should().Be("New");
        await _harness.UninstallAsync(result.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Keeps_everything_needed_offline_in_the_record()
    {
        var notice = new ThirdPartyNotice { Name = "Example Loader", License = "MIT", SourceUrl = "https://example.com/loader/source" };
        var package = _harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed").Notices(notice));

        var result = await _harness.InstallAsync(package);

        var record = (await _harness.CreateEngine().LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which;
        record.Should().BeEquivalentTo(result.Record);
        record.Game.Should().Be(TestProjects.Game);
        record.GameName.Should().Be("Example Game");
        record.GameBuildId.Should().Be("42");
        record.GameVolume.Should().Be(InstallHarness.GameVolume);
        record.InstalledAt.Should().Be(TestProjects.Now);
        record.Source.Should().Be(InstallHarness.Source);
        record.Package.Should().BeEquivalentTo(new InstalledPackage(PackageKind.Manifest, Path.GetFileName(package), TestPackage.Id,
            "Example package", "1.0", [notice]));
        record.FileCount.Should().Be(1);
        var file = (await _harness.Engine.LoadFilesAsync(record.Id, InstallHarness.Token)).Files.Should().ContainSingle().Which;
        file.Installed!.LastWriteTimeUtc.Should().Be(File.GetLastWriteTimeUtc(_harness.GameFile("readme.txt")));
        file.Original!.Sha256.Should().Be(TestPackage.Sha256("Read me"u8.ToArray()));
    }

    [Fact]
    public async Task A_test_install_without_a_source_stays_marked_after_a_restart()
    {
        var package = _harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Test"));
        var request = _harness.Request(package) with { IsTest = true, Source = null };

        await _harness.Engine.InstallAsync(request, null, InstallHarness.Token);

        (await _harness.CreateEngine().LoadRecordsAsync(InstallHarness.Token)).Single().IsTest.Should().BeTrue();
    }

    [Fact]
    public async Task Replaces_and_deletes_read_only_files_and_puts_the_attribute_back()
    {
        File.SetAttributes(_harness.GameFile("readme.txt"), FileAttributes.ReadOnly);
        File.SetAttributes(_harness.GameFile("movies/intro.bik"), FileAttributes.ReadOnly | FileAttributes.Hidden);
        var before = _harness.Snapshot();

        var result = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage()
            .Copy("readme.txt", "Changed")
            .Delete("movies/intro.bik")));

        _harness.ReadGameFile("readme.txt").Should().Be("Changed");
        File.Exists(_harness.GameFile("movies/intro.bik")).Should().BeFalse();
        (await _harness.Engine.LoadFilesAsync(result.Record.Id, InstallHarness.Token)).Files.Select(file => file.OriginalAttributes)
            .Should().Equal(FileAttributes.ReadOnly, FileAttributes.ReadOnly | FileAttributes.Hidden);
        await _harness.UninstallAsync(result.Record.Id);
        _harness.Snapshot().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task Replaces_a_hard_linked_file_without_writing_through_the_link()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The link is made with mklink.");
        var elsewhere = Path.Join(_harness.Packages, "shared.txt");
        Directory.CreateDirectory(_harness.Packages);
        File.Delete(_harness.GameFile("readme.txt"));
        await File.WriteAllTextAsync(elsewhere, "Read me", InstallHarness.Token);
        using (var link = Process.Start(new ProcessStartInfo("cmd.exe")
               {
                   ArgumentList = { "/c", "mklink", "/H", _harness.GameFile("readme.txt"), elsewhere },
                   CreateNoWindow = true,
                   UseShellExecute = false,
               })!)
        {
            await link.WaitForExitAsync(InstallHarness.Token);
        }

        await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));

        _harness.ReadGameFile("readme.txt").Should().Be("Changed");
        (await File.ReadAllTextAsync(elsewhere, InstallHarness.Token)).Should().Be("Read me");
    }

    [Fact]
    public async Task Reports_checking_and_then_installing_bytes_that_only_grow()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var reports = new List<InstallProgress>();

        await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Patch("data/strings.pak", patch).Copy("a.txt", "a")),
            new InlineProgress<InstallProgress>(reports.Add));

        reports.Should().Contain(report => report.Stage == InstallStage.Checking);
        var installing = reports.Where(report => report.Stage == InstallStage.Installing).ToList();
        installing.Select(report => report.BytesDone).Should().BeInAscendingOrder();
        installing[^1].FilesDone.Should().Be(2);
        installing[^1].BytesDone.Should().Be(installing[^1].BytesTotal);
    }

    [Fact]
    public async Task Refuses_a_package_made_for_another_game_of_the_store_and_only_warns_about_another_version()
    {
        PackageTarget[] otherGame = [new(StoreId.Gog, "2000"), new(StoreId.Steam, "3000")];
        PackageTarget[] otherStore = [new(StoreId.Gog, "2000")];

        var blocked = await _harness.InstallBlockedAsync(
            _harness.ManifestPackage(new TestPackage().Copy("a.txt", "a").Targets(otherGame)));
        var fromOtherStore = await _harness.InstallAsync(
            _harness.ManifestPackage(new TestPackage().Copy("b.txt", "b").Targets(otherStore)));
        var fromOtherBuild = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("c.txt", "c")
            .Targets(new PackageTarget(StoreId.Gog, "2000"), new PackageTarget(StoreId.Steam, "1000", "41"))));

        blocked.Issues.Should().ContainSingle().Which.Should().BeOfType<OtherGameIssue>().Which.Targets.Should().Equal(otherGame);
        fromOtherStore.Warnings.Should().ContainSingle().Which.Should().BeOfType<OtherStoreWarning>()
            .Which.Targets.Should().Equal(otherStore);
        fromOtherBuild.Warnings.Should().Equal(new OtherGameBuildWarning("41", "42"));
    }

    [Fact]
    public async Task Installs_but_tells_that_the_store_keeps_an_unfinished_download_of_the_game()
    {
        _harness.Probe.Reason = GameStateReason.UnfinishedDownload;

        var result = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));

        result.Warnings.Should().Equal(new UnfinishedStoreDownloadWarning());
        _harness.ReadGameFile("a.txt").Should().Be("a");
    }

    [Fact]
    public async Task Tells_that_copying_large_originals_to_another_drive_takes_a_while()
    {
        await using var harness = new InstallHarness(slowBackupCopyBytes: 1000);
        harness.PutBackupsOnAnotherVolume();
        harness.WriteGameFile("data/large.pak", new byte[1000]);
        var package = harness.ManifestPackage(new TestPackage().Copy("data/large.pak", "x").Copy("readme.txt", "y"));

        var otherDrive = await harness.Engine.CheckInstallAsync(harness.Request(package, InstallHarness.InAppData), null, InstallHarness.Token);
        var sameDrive = await harness.Engine.CheckInstallAsync(harness.Request(package), null, InstallHarness.Token);

        otherDrive.CanProceed.Should().BeTrue();
        otherDrive.Warnings.Should().Equal(new SlowBackupCopyWarning(1007));
        sameDrive.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Lets_two_projects_share_a_game_when_their_files_differ()
    {
        var first = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")));
        var second = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("b.txt", "b")));

        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Select(record => record.Id).Should()
            .BeEquivalentTo([first.Record.Id, second.Record.Id]);
        await _harness.UninstallAsync(first.Record.Id);

        File.Exists(_harness.GameFile("a.txt")).Should().BeFalse();
        _harness.ReadGameFile("b.txt").Should().Be("b");
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which.Id.Should().Be(second.Record.Id);
    }
}
