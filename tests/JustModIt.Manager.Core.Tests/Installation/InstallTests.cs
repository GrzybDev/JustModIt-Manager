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

    [Fact]
    public async Task Adds_a_file_in_new_folders_and_removes_both_on_uninstall()
    {
        var before = _harness.Snapshot();

        var result = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("data/lang/extra/text.txt", "Text")));

        _harness.ReadGameFile("data/lang/extra/text.txt").Should().Be("Text");
        var files = await _harness.Engine.LoadFilesAsync(result.Record.Id, InstallHarness.Token);
        files.CreatedDirectories.Should().Equal("data/lang", "data/lang/extra");
        files.Files.Should().ContainSingle().Which.Change.Should().Be(FileChange.Added);

        (await _harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().BeEmpty();
        _harness.Snapshot().Should().BeEquivalentTo(before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Locations))]
    public async Task Installs_and_uninstalls_every_kind_of_change_wherever_backups_live(string location)
    {
        Use(location);
        var before = _harness.Snapshot();
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var package = _harness.ManifestPackage(new TestPackage()
            .Rename("bin/library.dll", "bin/library_original.dll")
            .Copy("bin/library.dll", "proxy library")
            .Patch("data/strings.pak", patch)
            .Delete("movies/intro.bik")
            .Copy("data/lang/text.txt", "Text"));

        var result = await _harness.InstallAsync(package);

        _harness.ReadGameFile("bin/library.dll").Should().Be("proxy library");
        _harness.ReadGameFile("bin/library_original.dll").Should().Be("original library");
        (await File.ReadAllBytesAsync(_harness.GameFile("data/strings.pak"), InstallHarness.Token)).Should().Equal(InstallHarness.ChangedPak);
        File.Exists(_harness.GameFile("movies/intro.bik")).Should().BeFalse();
        var files = await _harness.Engine.LoadFilesAsync(result.Record.Id, InstallHarness.Token);
        files.Files.Select(file => (file.Path, file.Change)).Should().Equal(
            ("bin/library.dll", FileChange.Replaced),
            ("bin/library_original.dll", FileChange.Added),
            ("data/strings.pak", FileChange.Replaced),
            ("movies/intro.bik", FileChange.Deleted),
            ("data/lang/text.txt", FileChange.Added));
        Directory.Exists(Path.Join(_harness.HiddenDirectory, "staging")).Should().BeFalse();
        File.Exists(Path.Join(_harness.InstallsDirectory, result.Record.Id.ToString("N"), "journal.jsonl")).Should().BeFalse();
        (await _harness.Engine.VerifyAsync(result.Record.Id, null, InstallHarness.Token)).IsIntact.Should().BeTrue();

        (await _harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().BeEmpty();

        _harness.Snapshot().Should().BeEquivalentTo(before);
        _harness.AppFiles().Should().BeEmpty();
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

    [Fact]
    public async Task Moves_originals_on_the_same_volume_instead_of_copying_them()
    {
        var created = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(_harness.GameFile("readme.txt"), created);

        var result = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));

        // A move keeps the file itself, while a copy would be a new file.
        File.GetCreationTimeUtc(Path.Join(result.Record.Backup.Directory, "0")).Should().Be(created);
    }

    [Fact]
    public async Task Copies_originals_to_another_volume_and_restores_their_time()
    {
        _harness.PutBackupsOnAnotherVolume();
        _harness.Backup = InstallHarness.InAppData;
        var written = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(_harness.GameFile("readme.txt"), written);

        var result = await _harness.InstallAsync(_harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed")));
        var backup = Path.Join(result.Record.Backup.Directory, "0");

        File.ReadAllText(backup).Should().Be("Read me");
        File.GetCreationTimeUtc(backup).Should().BeAfter(written);
        await _harness.UninstallAsync(result.Record.Id);
        File.GetLastWriteTimeUtc(_harness.GameFile("readme.txt")).Should().Be(written);
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
        var package = _harness.ManifestPackage(new TestPackage().Copy("readme.txt", "Changed"));

        var result = await _harness.InstallAsync(package);

        var record = (await _harness.CreateEngine().LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle().Which;
        record.Should().Be(result.Record);
        record.Game.Should().Be(InstallHarness.Key);
        record.GameName.Should().Be("Example Game");
        record.GameBuildId.Should().Be("42");
        record.GameVolume.Should().Be(InstallHarness.GameVolume);
        record.InstalledAt.Should().Be(InstallHarness.Now);
        record.Source.Should().Be(InstallHarness.Source);
        record.Package.Should().Be(new InstalledPackage(PackageKind.Manifest, Path.GetFileName(package), TestPackage.Id,
            "Example package", "1.0"));
        record.FileCount.Should().Be(1);
        var file = (await _harness.Engine.LoadFilesAsync(record.Id, InstallHarness.Token)).Files.Should().ContainSingle().Which;
        file.Installed!.LastWriteTimeUtc.Should().Be(File.GetLastWriteTimeUtc(_harness.GameFile("readme.txt")));
        file.Original!.Sha256.Should().Be(TestPackage.Sha256("Read me"u8.ToArray()));
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
    public async Task Tells_about_a_package_made_for_another_game_or_build()
    {
        var otherGame = _harness.ManifestPackage(new TestPackage().Copy("a.txt", "a")
            .Targets(new PackageTarget(StoreId.Gog, "2000")));
        var otherBuild = _harness.ManifestPackage(new TestPackage().Copy("b.txt", "b")
            .Targets(new PackageTarget(StoreId.Steam, "1000", "41")));

        var report = await _harness.Engine.CheckInstallAsync(_harness.Request(otherGame), null, InstallHarness.Token);
        var result = await _harness.InstallAsync(otherBuild);

        report.Warnings.Should().ContainSingle().Which.Should().BeOfType<OtherGameWarning>();
        result.Warnings.Should().Equal(new OtherGameBuildWarning("41", "42"));
    }

    [Fact]
    public async Task Tells_that_copying_large_originals_to_another_drive_takes_a_while()
    {
        _harness.PutBackupsOnAnotherVolume();
        _harness.SlowBackupCopyBytes = 1000;
        var engine = _harness.CreateEngine();
        _harness.WriteGameFile("data/large.pak", new byte[1000]);
        var package = _harness.ManifestPackage(new TestPackage().Copy("data/large.pak", "x").Copy("readme.txt", "y"));

        var otherDrive = await engine.CheckInstallAsync(_harness.Request(package, InstallHarness.InAppData), null, InstallHarness.Token);
        var sameDrive = await engine.CheckInstallAsync(_harness.Request(package), null, InstallHarness.Token);

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

    private void Use(string location)
    {
        if (location.EndsWith("another drive", StringComparison.Ordinal))
        {
            _harness.PutBackupsOnAnotherVolume();
        }

        _harness.Backup = location switch
        {
            _ when location.StartsWith("app data", StringComparison.Ordinal) => InstallHarness.InAppData,
            _ when location.StartsWith("custom", StringComparison.Ordinal) => _harness.InCustomFolder,
            _ => BackupChoice.GameFolder,
        };
    }
}
