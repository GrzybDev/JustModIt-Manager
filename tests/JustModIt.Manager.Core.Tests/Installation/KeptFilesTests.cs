using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.Installation.RuntimeFileTests;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class KeptFilesTests : IAsyncDisposable
{
    private const string List = "tool/list.dat";
    private const string Rewritten = "List rewritten by the software";

    private readonly InstallHarness _harness = WithList(InstallHarness.At("app data"));

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task A_kept_files_record_is_written_before_the_uninstall_commits()
    {
        var result = await InstallAsync(Software());
        Play(_harness);
        _harness.Probe.BuildId = "43";
        var records = 0;
        var writtenBeforeCommit = false;
        var folder = Path.Join(_harness.LeftoversDirectory, result.Record.Id.ToString("N"));
        _harness.BeforeChange = (kind, _) =>
        {
            if (kind == ChangeKind.Record && ++records == 2)
            {
                writtenBeforeCommit = File.Exists(Path.Join(folder, "files.json")) && File.Exists(Path.Join(folder, "record.json"));
            }
        };

        await _harness.UninstallAsync(result.Record.Id);

        records.Should().Be(2, "the kept-files record, then the commit");
        writtenBeforeCommit.Should().BeTrue();
    }

    [Fact]
    public async Task An_uninstall_undone_before_its_commit_takes_the_kept_files_record_it_wrote_with_it()
    {
        var result = await InstallAsync(Software());
        Play(_harness);
        _harness.Probe.BuildId = "43";
        var played = _harness.Snapshot();
        var records = 0;
        _harness.BeforeChange = (kind, _) =>
        {
            if (kind == ChangeKind.Record && ++records == 2)
            {
                throw new IOException("The disk is full.");
            }
        };

        var uninstall = () => _harness.UninstallAsync(result.Record.Id);

        (await uninstall.Should().ThrowAsync<InstallFailedException>()).Which.RolledBack.Should().BeTrue();
        _harness.BeforeChange = null;
        Directory.Exists(Path.Join(_harness.LeftoversDirectory, result.Record.Id.ToString("N"))).Should().BeFalse();
        _harness.Snapshot().Should().BeEquivalentTo(played);
        (await _harness.Engine.LoadRecordsAsync(InstallHarness.Token)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_kept_path_still_as_the_software_left_it_refuses_an_install_that_declares_or_writes_it()
    {
        var removed = await KeepListAsync();

        var declaring = await _harness.InstallBlockedAsync(_harness.ManifestPackage(Software()));
        var writing = await _harness.InstallBlockedAsync(_harness.ManifestPackage(new TestPackage().Copy(List, "Another list")));
        var elsewhere = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));

        declaring.Issues.Should().Equal(new KeptFileIssue(List, removed));
        writing.Issues.Should().Equal(new KeptFileIssue(List, removed));
        elsewhere.Record.Should().NotBeNull("a package that leaves the path alone installs as usual");
        (await _harness.KeptFilesAsync()).Should().ContainKey(removed);
    }

    [Theory]
    [InlineData("the store's check wrote it")]
    [InlineData("it holds an original the package names")]
    public async Task A_kept_path_whose_content_changed_or_matches_the_package_s_originals_no_longer_blocks_and_its_copy_goes(string why)
    {
        var removed = await KeptRecordAsync(await KeepListAsync());
        var copy = Path.Join(removed.Record.Backup.Directory, removed.Files.Files.Single().Backup);
        File.Exists(copy).Should().BeTrue();
        var package = Software();
        if (why.StartsWith("the store", StringComparison.Ordinal))
        {
            _harness.WriteGameFile(List, "The store's list of the new build");
        }
        else
        {
            package = Software(Rewritten);
        }

        var installed = await InstallAsync(package);

        installed.Record.Should().NotBeNull();
        (await _harness.KeptFilesAsync()).Should().BeEmpty();
        File.Exists(copy).Should().BeFalse("the entry left its record together with its copy");
    }

    [Fact]
    public async Task Kept_copies_on_an_unplugged_drive_refuse_an_install_while_deleted_ones_leave_the_original_uncertain()
    {
        await using var harness = WithList(InstallHarness.At("custom folder on another drive"));
        var first = await harness.InstallAsync(harness.ManifestPackage(Software()));
        Play(harness);
        await harness.Engine.UninstallAsync(first.Record.Id, [harness.ManifestPackage(Software())], null, InstallHarness.Token);
        var unplugged = harness.CustomFolder + " on an unplugged drive";
        Directory.Move(harness.CustomFolder, unplugged);

        var blocked = await harness.InstallBlockedAsync(harness.ManifestPackage(Software()));
        Directory.Move(unplugged, harness.CustomFolder);
        Directory.Delete(first.Record.Backup.Directory, recursive: true);
        var second = await harness.InstallAsync(harness.ManifestPackage(Software()));

        blocked.Issues.Should().Contain(new BackupFolderUnavailableIssue(first.Record.Backup.Directory));
        (await harness.FilesOnDiskAsync(second.Record.Id)).Files.Single(file => file.Path == List).OriginalUncertain.Should().BeTrue();
    }

    [Fact]
    public async Task Removing_kept_copies_deletes_only_what_the_record_lists()
    {
        var removed = await KeptRecordAsync(await KeepListAsync());
        var stray = Path.Join(removed.Record.Backup.Directory, "notes.txt");
        await File.WriteAllTextAsync(stray, "the player's", InstallHarness.Token);

        var done = await _harness.Engine.RemoveLeftoverBackupsAsync(removed.Record.Id, InstallHarness.Token);

        done.Should().BeTrue();
        File.Exists(Path.Join(removed.Record.Backup.Directory, removed.Files.Files.Single().Backup)).Should().BeFalse();
        File.Exists(stray).Should().BeTrue();
        (await _harness.KeptFilesAsync()).Should().BeEmpty();
        (await _harness.InstallAsync(_harness.ManifestPackage(Software()))).Record.Should().NotBeNull(
            "without its record the path no longer blocks");
    }

    [Fact]
    public async Task Kept_copies_are_listed_with_their_size_once_the_uninstall_committed()
    {
        var result = await InstallAsync(Software());
        Play(_harness);
        _harness.Probe.BuildId = "43";
        var records = 0;
        IReadOnlyList<LeftoverBackup>? beforeCommit = null;
        _harness.BeforeChange = (kind, _) =>
        {
            if (kind == ChangeKind.Record && ++records == 2)
            {
                beforeCommit = _harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token).GetAwaiter().GetResult();
            }
        };

        await _harness.UninstallAsync(result.Record.Id);
        var listed = await _harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token);

        beforeCommit.Should().BeEmpty("an uninstall that has not committed may still be undone");
        var kept = listed.Should().ContainSingle().Which.Kept;
        kept!.KeptAt.Should().Be(_harness.Time.GetUtcNow());
        listed.Should().BeEquivalentTo([new LeftoverBackup(result.Record.Id, result.Record.Game, result.Record.GameName,
            result.Record.Backup.Directory, 13)
        {
            GameDirectory = _harness.GameRoot,
            Cover = InstallHarness.Source.CoverUrl,
            Kept = new KeptFilesSummary([List], 1, kept.KeptAt, KeptReason.GameUpdatedSinceInstall, "Example Project"),
        }]);
    }

    [Fact]
    public async Task Kept_copies_that_went_with_a_game_folder_gone_for_good_are_dropped()
    {
        await using var harness = WithList(new InstallHarness());
        var result = await harness.InstallAsync(harness.ManifestPackage(Software()));
        Play(harness);
        harness.Probe.BuildId = "43";
        (await harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().ContainSingle();
        TreeCopy.Delete(harness.GameRoot);

        (await harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token)).Should().BeEmpty();

        Directory.Exists(Path.Join(harness.LeftoversDirectory, result.Record.Id.ToString("N"))).Should().BeFalse();
    }

    [Fact]
    public async Task Kept_copies_outside_a_game_folder_gone_for_good_stay_listed_with_their_size()
    {
        var removed = await KeepListAsync();
        TreeCopy.Delete(_harness.GameRoot);

        var listed = await _harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token);

        listed.Should().ContainSingle().Which.Should().Match<LeftoverBackup>(kept =>
            kept.RecordId == removed && kept.Bytes == 13 && kept.Kept != null, "the copies in the app's data are all that is left to remove");
    }

    [Theory]
    [InlineData(KeptReason.GameUpdatedSinceInstall, KeptReason.OriginalUnknown)]
    [InlineData(KeptReason.OriginalUnknown, KeptReason.BackupMissing)]
    [InlineData(KeptReason.OriginalUnknown, KeptReason.BackupDamaged)]
    [InlineData(KeptReason.BackupMissing, KeptReason.ChangedSinceInstall)]
    public void A_game_update_or_a_doubted_original_is_told_before_a_lost_copy_and_that_before_a_changed_file(KeptReason first,
        KeptReason second)
    {
        KeptReasons.MostPressing([second, first]).Should().Be(first);
        KeptReasons.MostPressing([first, second]).Should().Be(first);
        KeptReasons.MostPressing([]).Should().BeNull();
    }

    [Fact]
    public async Task A_kept_record_of_notes_without_copies_is_not_listed()
    {
        var first = await InstallAsync(new TestPackage().Copy("tool/loader.dll", "loader").Copy("tool/list_original.dat", "Shipped copy"));

        await _harness.Engine.UninstallAsync(first.Record.Id, [_harness.ManifestPackage(Software())], null, InstallHarness.Token);

        (await _harness.KeptFilesAsync()).Should().ContainKey(first.Record.Id);
        (await _harness.Engine.FindLeftoverBackupsAsync(InstallHarness.Token)).Should().BeEmpty();
    }

    // Uninstalls the software after a game update, which keeps the list and its copy; returns the uninstalled install's id.
    private async Task<Guid> KeepListAsync()
    {
        var result = await InstallAsync(Software());
        Play(_harness);
        _harness.Probe.BuildId = "43";
        (await _harness.UninstallAsync(result.Record.Id)).KeptFiles.Should().Equal(new KeptFile(List, KeptReason.GameUpdatedSinceInstall));
        _harness.Probe.BuildId = "42";
        return result.Record.Id;
    }

    private async Task<KeptFilesRecord> KeptRecordAsync(Guid id)
    {
        var folder = new InstallRecordStore(_harness.LeftoversDirectory, TimeProvider.System).FolderOf(id);
        var record = (await InstallRecordStore.LoadAsync(folder, InstallHarness.Token)).Record!;
        return new KeptFilesRecord(folder, record, await InstallRecordStore.LoadFilesAsync(folder, InstallHarness.Token));
    }

    private Task<InstallResult> InstallAsync(TestPackage package) => _harness.InstallAsync(_harness.ManifestPackage(package));
}
