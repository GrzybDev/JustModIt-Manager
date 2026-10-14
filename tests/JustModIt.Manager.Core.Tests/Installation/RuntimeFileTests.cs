using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class RuntimeFileTests : IAsyncDisposable
{
    private const string List = "tool/list.dat";
    private const string ListCopy = "tool/list_original.dat";
    private const string OriginalList = "Original list";

    private static readonly DateTime OriginalTime = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private readonly InstallHarness _harness;
    private readonly Dictionary<string, string> _before;

    public RuntimeFileTests()
    {
        _harness = WithList(new InstallHarness());
        _before = _harness.Snapshot();
    }

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Theory]
    [InlineData("game folder")]
    [InlineData("app data on another drive")]
    public async Task A_declared_game_file_is_copied_with_its_time_and_stays_in_place(string location)
    {
        await using var harness = WithList(InstallHarness.At(location));

        var result = await harness.InstallAsync(harness.ManifestPackage(Software()));

        harness.ReadGameFile(List).Should().Be(OriginalList);
        File.GetLastWriteTimeUtc(harness.GameFile(List)).Should().Be(OriginalTime);
        var files = await harness.FilesOnDiskAsync(result.Record.Id);
        files.FormatVersion.Should().Be(2);
        var entry = files.Files.Single(file => file.Path == List);
        entry.Should().BeEquivalentTo(new InstalledFile(List, FileChange.Unchanged,
            new FileSnapshot(13, TestPackage.Sha256("Original list"u8.ToArray()), OriginalTime), Backup: entry.Backup) { Runtime = true });
        var copy = Path.Join(result.Record.Backup.Directory, entry.Backup);
        (await File.ReadAllTextAsync(copy, InstallHarness.Token)).Should().Be(OriginalList);
        File.GetLastWriteTimeUtc(copy).Should().Be(OriginalTime, "a copy moved back must carry its original's time");
        result.Record.FileCount.Should().Be(1, "the install changed only the loader");
        result.Record.Package.RuntimeFiles.Should().Equal(List, ListCopy);
    }

    [Fact]
    public async Task An_absent_declared_file_is_recorded_and_its_missing_folders_are_created()
    {
        var result = await InstallAsync(new TestPackage().Copy("tool/loader.dll", "loader").Declare("tool/cache/index/list.dat"));

        Directory.Exists(_harness.GameFile("tool/cache/index")).Should().BeTrue("the software finds its folder");
        var files = await _harness.FilesOnDiskAsync(result.Record.Id);
        files.Files.Should().ContainEquivalentOf(new InstalledFile("tool/cache/index/list.dat", FileChange.Unchanged) { Runtime = true });
        files.CreatedDirectories.Should().Equal("tool/cache", "tool/cache/index");
    }

    [Fact]
    public async Task Uninstalling_removes_a_file_the_software_created_and_the_folders_made_for_it()
    {
        var result = await InstallAsync(new TestPackage().Copy("tool/loader.dll", "loader").Declare("tool/cache/index/list.dat"));
        _harness.WriteGameFile("tool/cache/index/list.dat", "Written by the software");

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().BeEmpty();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Declarations_reaching_one_file_through_a_link_are_one_entry()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The link is a junction.");
        _harness.WriteGameFile("real/list.dat", "Original list");
        _harness.LinkDirectory("alias", _harness.GameFile("real"));

        var result = await InstallAsync(new TestPackage().Copy("tool/loader.dll", "loader").Declare("alias/list.dat").Declare("REAL/list.dat"));

        (await _harness.FilesOnDiskAsync(result.Record.Id)).Files.Where(file => file.Runtime).Should().ContainSingle()
            .Which.Path.Should().Be("real/list.dat");
    }

    [Fact]
    public async Task Without_declarations_the_file_list_keeps_its_first_format()
    {
        var result = await InstallAsync(new TestPackage().Copy("tool/loader.dll", "loader"));

        (await _harness.FilesOnDiskAsync(result.Record.Id)).FormatVersion.Should().Be(1, "older versions undo it as before");
    }

    [Theory]
    [InlineData("game folder", InstallHarness.GameVolume)]
    [InlineData("app data on another drive", InstallHarness.OtherVolume)]
    public async Task Free_space_counts_copies_of_declared_files_on_the_backup_s_drive(string location, string volume)
    {
        await using var harness = WithList(InstallHarness.At(location));
        harness.WriteGameFile(List, new byte[1 << 20]);
        harness.Volumes.Available[volume] = InstallPreflight.SpaceMargin + (1 << 20) - 1;

        var blocked = await harness.InstallBlockedAsync(harness.ManifestPackage(Software()));

        blocked.Issues.Should().ContainSingle().Which.Should().BeOfType<NotEnoughSpaceIssue>();
    }

    [Fact]
    public async Task A_declared_file_the_software_writes_during_the_copy_stops_the_install_unchanged()
    {
        var progress = new InlineProgress<InstallProgress>(report =>
        {
            if (report is { Stage: InstallStage.Installing, BytesDone: > 0 } && _harness.ReadGameFile(List) == OriginalList)
            {
                _harness.WriteGameFile(List, "Rewritten by the software while the game started");
            }
        });

        var install = () => _harness.Engine.InstallAsync(_harness.Request(_harness.ManifestPackage(Software())), progress,
            InstallHarness.Token);

        (await install.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should().Equal(new FileLockedIssue(List, []));
        File.Exists(_harness.GameFile("tool/loader.dll")).Should().BeFalse();
        _harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task Reading_a_declared_file_never_locks_out_a_writer()
    {
        var opened = false;
        var progress = new InlineProgress<InstallProgress>(report =>
        {
            if (report is { Stage: InstallStage.Installing, BytesDone: > 0 } && !opened)
            {
                using var writer = new FileStream(_harness.GameFile(List), FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                opened = true;
            }
        });

        await _harness.Engine.InstallAsync(_harness.Request(_harness.ManifestPackage(Software())), progress, InstallHarness.Token);

        opened.Should().BeTrue();
    }

    [Fact]
    public async Task A_declared_file_another_program_holds_open_refuses_the_install()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to share an open file.");
        InstallBlockedException blocked;
        await using (new FileStream(_harness.GameFile(List), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            blocked = await _harness.InstallBlockedAsync(_harness.ManifestPackage(Software()));
        }

        blocked.Issues.Should().Equal(new FileLockedIssue(List, ["Example.exe"]));
    }

    [Fact]
    public async Task A_declared_path_another_install_lists_refuses_the_install()
    {
        var declaring = await InstallAsync(Software());
        var writing = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy(List, "Another list")));
        await _harness.UninstallAsync(declaring.Record.Id);
        var other = await InstallAsync(new TestPackage().Copy(ListCopy, "Another file"));

        var declaringAgain = await _harness.InstallBlockedAsync(_harness.ManifestPackage(Software()));

        writing.Issues.Should().Equal(new FileOwnedByOtherInstallIssue(List, declaring.Record.Id));
        declaringAgain.Issues.Should().Equal(new FileOwnedByOtherInstallIssue(ListCopy, other.Record.Id));
    }

    [Theory]
    [InlineData(".example-app/list.dat")]
    [InlineData(".egstore/list.dat")]
    [InlineData(".other-app/list.dat")]
    [InlineData("../list.dat")]
    public async Task A_declared_path_in_the_app_s_a_store_s_or_another_app_s_folder_is_refused(string path)
    {
        if (path.StartsWith(".other-app", StringComparison.Ordinal))
        {
            InstallHarness.WriteOwnerFile(_harness.GameFile(".other-app"), "OtherApp");
        }

        Directory.CreateDirectory(_harness.GameFile(".egstore"));
        var package = path.StartsWith("..", StringComparison.Ordinal)
            ? new TestPackage().Copy("tool/loader.dll", "loader").RawManifest($$"""
                { "formatVersion": 1, "id": "{{TestPackage.Id}}", "title": "T", "version": "1", "requires": [ "op:copy", "runtime:files@1" ],
                  "runtimeFiles": [ { "path": "{{path}}" } ],
                  "operations": [ { "type": "copy", "path": "tool/loader.dll", "source": "files/tool/loader.dll", "size": 6,
                    "sha256": "{{TestPackage.Sha256("loader"u8.ToArray())}}" } ] }
                """)
            : new TestPackage().Copy("tool/loader.dll", "loader").Declare(path);

        var install = () => InstallAsync(package);

        if (path.StartsWith("..", StringComparison.Ordinal))
        {
            (await install.Should().ThrowAsync<InvalidPackageException>()).Which.Error.Should().Be(PackageError.InvalidPath);
        }
        else
        {
            (await install.Should().ThrowAsync<InstallBlockedException>()).Which.Issues.Should().Contain(new UnsafePathIssue(path));
        }
    }

    [Fact]
    public async Task An_original_that_matches_the_package_s_originals_is_certain()
    {
        var result = await InstallAsync(Software(OriginalList).Targets(new PackageTarget(StoreId.Steam, TestProjects.Game.StoreGameId, "42")));

        (await RuntimeEntryAsync(result, List)).OriginalUncertain.Should().BeFalse();
    }

    [Theory]
    [InlineData("42", true)]
    [InlineData("41", false)]
    public async Task On_the_package_s_own_build_an_original_unlike_the_package_s_originals_is_uncertain(string build, bool uncertain)
    {
        var result = await InstallAsync(Software("Store list").Targets(new PackageTarget(StoreId.Steam, TestProjects.Game.StoreGameId, build)));

        (await RuntimeEntryAsync(result, List)).OriginalUncertain.Should().Be(uncertain);
    }

    [Fact]
    public async Task Verification_never_opens_a_file_the_package_s_software_holds_open()
    {
        var result = await InstallAsync(Software());

        VerificationResult verified;
        await using (new FileStream(_harness.GameFile(List), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            verified = await _harness.Engine.VerifyAsync(result.Record.Id, null, InstallHarness.Token);
        }

        verified.Files.Should().Equal(new FileVerification("tool/loader.dll", FileChange.Added, FileIntegrity.Intact));
    }

    [Fact]
    public async Task Changes_the_software_makes_never_ask_for_a_repair()
    {
        var result = await InstallAsync(Software());
        Play();
        File.Delete(_harness.GameFile(List));

        var verified = await _harness.Engine.VerifyAsync(result.Record.Id, null, InstallHarness.Token);

        verified.IsIntact.Should().BeTrue();
    }

    [Theory]
    [InlineData("game folder")]
    [InlineData("app data on another drive")]
    public async Task Uninstalling_brings_back_a_declared_file_whatever_the_software_wrote(string location)
    {
        await using var harness = WithList(InstallHarness.At(location));
        var before = harness.Snapshot();
        var result = await harness.InstallAsync(harness.ManifestPackage(Software()));
        Play(harness);

        var uninstalled = await harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().BeEmpty();
        harness.Snapshot().Should().BeEquivalentTo(before);
        File.GetLastWriteTimeUtc(harness.GameFile(List)).Should().Be(OriginalTime);
        harness.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task After_a_game_update_declared_originals_stay_their_copies_are_kept_and_created_files_go()
    {
        var result = await InstallAsync(Software());
        Play();
        _harness.Probe.BuildId = "43";

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
        _harness.ReadGameFile(List).Should().Be("List rewritten by the software");
        File.Exists(_harness.GameFile(ListCopy)).Should().BeFalse("a file the store never shipped can always go");
        File.Exists(_harness.GameFile("tool/loader.dll")).Should().BeFalse();
        var kept = (await _harness.KeptFilesAsync()).Should().ContainSingle().Which;
        kept.Key.Should().Be(result.Record.Id);
        var entry = kept.Value.Files.Should().ContainSingle().Which;
        entry.Kept.Should().Be(KeptReason.GameUpdatedSinceInstall);
        entry.Installed!.Sha256.Should().Be(TestPackage.Sha256("List rewritten by the software"u8.ToArray()));
        entry.Taken.Should().Be(result.Record.GameVersion);
        (await File.ReadAllTextAsync(Path.Join(result.Record.Backup.Directory, entry.Backup), InstallHarness.Token)).Should().Be(OriginalList);
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task With_no_version_known_on_either_side_the_declared_files_are_reverted()
    {
        _harness.Probe.BuildId = null;
        var result = await _harness.Engine.InstallAsync(_harness.Request(_harness.ManifestPackage(Software())) with
        {
            Game = _harness.Game with { BuildId = null },
        }, null, InstallHarness.Token);
        Play();

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().BeEmpty();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    public async Task With_a_version_known_on_one_side_only_the_originals_are_kept()
    {
        var result = await _harness.Engine.InstallAsync(_harness.Request(_harness.ManifestPackage(Software())) with
        {
            Game = _harness.Game with { BuildId = null },
        }, null, InstallHarness.Token);
        Play();

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
        _harness.ReadGameFile(List).Should().Be("List rewritten by the software");
    }

    [Fact]
    public async Task A_language_switch_keeps_the_originals_like_an_update()
    {
        var result = await InstallAsync(Software());
        Play();
        _harness.Probe.Languages = ["de"];

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
    }

    [Fact]
    public async Task A_game_its_store_reports_in_another_folder_keeps_the_originals_like_an_update()
    {
        var result = await InstallAsync(Software());
        Play();
        _harness.Probe.InstallDirectory = Path.Join(_harness.Packages, "Example Game");

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
        _harness.ReadGameFile(List).Should().Be("List rewritten by the software");
    }

    [Fact]
    public async Task An_uncertain_original_is_put_back_and_reported_even_when_unchanged()
    {
        var result = await InstallAsync(Software("Store list").Targets(new PackageTarget(StoreId.Steam, TestProjects.Game.StoreGameId, "42")));

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().Equal(new KeptFile(List, KeptReason.OriginalUnknown));
        _harness.Snapshot().Should().BeEquivalentTo(_before);
        var marker = (await _harness.KeptFilesAsync()).Values.Should().ContainSingle().Which.Files.Should().ContainSingle().Which;
        marker.Should().BeEquivalentTo(new InstalledFile(List, FileChange.Unchanged, marker.Original, marker.Original)
        {
            Runtime = true,
            OriginalUncertain = true,
            Kept = KeptReason.OriginalUnknown,
        });
        marker.Original!.Sha256.Should().Be(TestPackage.Sha256("Original list"u8.ToArray()));
    }

    [Theory]
    [InlineData(false, KeptReason.BackupMissing)]
    [InlineData(true, KeptReason.BackupDamaged)]
    public async Task A_missing_or_damaged_copy_keeps_the_other_originals_too(bool damaged, KeptReason reason)
    {
        _harness.WriteGameFile("data/index.dat", "Original index");
        var result = await InstallAsync(Software().Declare("data/index.dat"));
        Play();
        _harness.WriteGameFile("data/index.dat", "Index rewritten by the software");
        var copy = Path.Join(result.Record.Backup.Directory, (await RuntimeEntryAsync(result, "data/index.dat")).Backup);
        if (damaged)
        {
            await File.WriteAllTextAsync(copy, "Original ind3x", InstallHarness.Token);
        }
        else
        {
            File.Delete(copy);
        }

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().BeEquivalentTo([new KeptFile(List, reason), new KeptFile("data/index.dat", reason)]);
        _harness.ReadGameFile(List).Should().Be("List rewritten by the software");
        _harness.ReadGameFile("data/index.dat").Should().Be("Index rewritten by the software");
        var kept = (await _harness.KeptFilesAsync()).Values.Single().Files;
        kept.Single(file => file.Path == List).Backup.Should().NotBeNull("its copy is sound");
        kept.Single(file => file.Path == "data/index.dat").Backup.Should().BeNull();
    }

    [Fact]
    public async Task A_link_at_a_declared_path_is_never_written_through()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The link is a junction.");
        var result = await InstallAsync(Software());
        Play();
        var elsewhere = Directory.CreateDirectory(Path.Join(_harness.Packages, "elsewhere")).FullName;
        File.Move(_harness.GameFile(List), Path.Join(elsewhere, "list.dat"));
        File.Move(_harness.GameFile(ListCopy), Path.Join(elsewhere, "list_original.dat"));
        File.Move(_harness.GameFile("tool/loader.dll"), Path.Join(elsewhere, "loader.dll"));
        Directory.Delete(_harness.GameFile("tool"));
        _harness.LinkDirectory("tool", elsewhere);

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().Contain(new KeptFile(List, KeptReason.ChangedSinceInstall));
        (await File.ReadAllTextAsync(Path.Join(elsewhere, "list.dat"), InstallHarness.Token)).Should().Be("List rewritten by the software");
        File.Exists(Path.Join(elsewhere, "list_original.dat")).Should().BeTrue();
    }

    [Fact]
    public async Task A_link_to_another_folder_of_the_game_at_a_declared_path_is_never_written_through()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The link is a junction.");
        var result = await InstallAsync(Software());
        Play();
        var moved = Directory.CreateDirectory(_harness.GameFile("moved")).FullName;
        File.Move(_harness.GameFile(List), Path.Join(moved, "list.dat"));
        File.Move(_harness.GameFile(ListCopy), Path.Join(moved, "list_original.dat"));
        File.Move(_harness.GameFile("tool/loader.dll"), Path.Join(moved, "loader.dll"));
        Directory.Delete(_harness.GameFile("tool"));
        _harness.LinkDirectory("tool", moved);

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().Contain([new KeptFile(List, KeptReason.ChangedSinceInstall),
            new KeptFile(ListCopy, KeptReason.ChangedSinceInstall)]);
        _harness.ReadGameFile("moved/list.dat").Should().Be("List rewritten by the software");
        File.Exists(_harness.GameFile("moved/list_original.dat")).Should().BeTrue();
    }

    [Fact]
    public async Task A_folder_at_one_declared_path_keeps_the_other_originals_too()
    {
        _harness.WriteGameFile("data/index.dat", "Original index");
        var result = await InstallAsync(Software().Declare("data/index.dat"));
        Play();
        File.Delete(_harness.GameFile("data/index.dat"));
        Directory.CreateDirectory(_harness.GameFile("data/index.dat"));

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().BeEquivalentTo([new KeptFile("data/index.dat", KeptReason.ChangedSinceInstall),
            new KeptFile(List, KeptReason.ChangedSinceInstall)]);
        _harness.ReadGameFile(List).Should().Be("List rewritten by the software");
    }

    [Fact]
    public async Task A_copy_damaged_after_the_check_undoes_the_whole_uninstall()
    {
        var result = await InstallAsync(Software());
        Play();
        var played = _harness.SnapshotOfGameFiles();
        var copy = Path.Join(result.Record.Backup.Directory, (await RuntimeEntryAsync(result, List)).Backup);
        var progress = new InlineProgress<InstallProgress>(report =>
        {
            if (report.Stage == InstallStage.Uninstalling && File.ReadAllText(copy) == OriginalList)
            {
                File.SetAttributes(copy, FileAttributes.Normal);
                File.WriteAllText(copy, "Original lis7");
            }
        });

        var uninstall = () => _harness.Engine.UninstallAsync(result.Record.Id, null, progress, InstallHarness.Token);

        (await uninstall.Should().ThrowAsync<InstallFailedException>()).Which.RolledBack.Should().BeTrue();
        _harness.SnapshotOfGameFiles().Should().BeEquivalentTo(played);
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle();
        (await _harness.KeptFilesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_letter_case_difference_is_not_a_link()
    {
        var result = await InstallAsync(new TestPackage().Copy("tool/loader.dll", "loader").Declare("tool/List_Original.dat"));
        _harness.WriteGameFile("tool/list_original.dat", "Copy written by the software");

        var uninstalled = await _harness.UninstallAsync(result.Record.Id);

        uninstalled.KeptFiles.Should().BeEmpty();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Declared_files_are_read_only_after_the_probe()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to share an open file.");
        var result = await InstallAsync(Software());
        Play();
        _harness.Probe.Runs("Example.exe");
        _harness.Probe.State = GameInstallState.Running;

        InstallBlockedException running;
        // Any read of the file before the probe would fail on the running game's lock rather than on the game.
        await using (new FileStream(_harness.GameFile(List), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var uninstall = () => _harness.UninstallAsync(result.Record.Id);
            running = (await uninstall.Should().ThrowAsync<InstallBlockedException>()).Which;
        }

        running.Issues.Should().Equal(new GameRunningIssue("Example.exe"));
        _harness.ReadGameFile(List).Should().Be("List rewritten by the software");
    }

    [Fact]
    public async Task Reading_a_declared_file_at_uninstall_never_locks_out_a_writer()
    {
        var result = await InstallAsync(Software());
        _harness.WriteGameFile(List, "Rewritten 13!");
        var opened = false;
        var progress = new InlineProgress<InstallProgress>(report =>
        {
            if (report is { Stage: InstallStage.Checking, BytesDone: > 0 } && !opened)
            {
                using var writer = new FileStream(_harness.GameFile(List), FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                opened = true;
            }
        });

        await _harness.Engine.UninstallAsync(result.Record.Id, null, progress, InstallHarness.Token);

        opened.Should().BeTrue();
        _harness.Snapshot().Should().BeEquivalentTo(_before);
    }

    // A proxy library that keeps a copy of the game's list and rewrites the list at every start, as such tools do.
    internal static TestPackage Software(params string[] originals) =>
        new TestPackage().Copy("tool/loader.dll", "loader").Declare(List, originals).Declare(ListCopy);

    // What the software does when the game starts: it copies the list once, which keeps the list's time, and rewrites it.
    internal static void Play(InstallHarness harness)
    {
        if (!File.Exists(harness.GameFile(ListCopy)))
        {
            File.Copy(harness.GameFile(List), harness.GameFile(ListCopy));
        }

        harness.WriteGameFile(List, "List rewritten by the software");
    }

    internal static InstallHarness WithList(InstallHarness harness)
    {
        harness.WriteGameFile(List, OriginalList);
        File.SetLastWriteTimeUtc(harness.GameFile(List), OriginalTime);
        return harness;
    }

    private void Play() => Play(_harness);

    private Task<InstallResult> InstallAsync(TestPackage package) => _harness.InstallAsync(_harness.ManifestPackage(package));

    private async Task<InstalledFile> RuntimeEntryAsync(InstallResult result, string path) =>
        (await _harness.Engine.LoadFilesAsync(result.Record.Id, InstallHarness.Token)).Files.Single(file => file.Path == path);
}
