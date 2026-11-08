using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Downloads;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.Catalog.TestSupport;
using JustModIt.Manager.Core.Tests.Downloads.TestSupport;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class ReleaseDownloadJobTests
{
    private static readonly FileLayout Layout = new()
    {
        Folders = [new("Ember Isles_Data", "EmberIsles_Data")],
        ObjectSize = 2048,
        ObjectSha256 = new string('a', 64),
    };

    private static readonly FolderRename[] KeptRule = [new("Ember Isles_Data", "Ember_Data")];

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Asks_for_a_release_s_files_together_only_where_the_site_says_which_add_ons_it_has(bool listsAddOns,
        bool together)
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne)) with { Features = listsAddOns ? [] : null }));

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        var download = jobs.Downloader.Releases.Should().ContainSingle().Which;
        download.ResolveTogether.Should().Be(together);
        download.Release.Should().Be(new ReleaseLocator("lantern-team", "example-project", ReleaseOne));
        download.Files.Select(file => file.Locator.FileUuid).Should().Equal(FileOne);
    }

    [Fact]
    public async Task A_refused_selection_reads_the_release_again_and_tries_once_more()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Downloader.ReleaseFailures.Enqueue(new CatalogueException(CatalogueErrorReason.InvalidSelection, "No such files."));

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Catalogue.PageRequests.Should().HaveCount(2);
        jobs.Downloader.Releases.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_selection_refused_twice_fails_as_a_release_the_site_no_longer_offers()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Downloader.ReleaseFailures.Enqueue(new CatalogueException(CatalogueErrorReason.InvalidSelection, "No such files."));
        jobs.Downloader.ReleaseFailures.Enqueue(new CatalogueException(CatalogueErrorReason.InvalidSelection, "No such files."));

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Failure.Should().BeEquivalentTo(new JobFailure(JobFailureReason.ReleaseUnavailable)
        {
            CatalogueError = CatalogueErrorReason.InvalidSelection,
        });
        jobs.Catalogue.PageRequests.Should().HaveCount(2);
        jobs.Engine.Installs.Should().BeEmpty();
    }

    [Fact]
    public async Task A_copied_file_comes_from_its_original_s_download_which_the_install_then_keeps()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.PublishReleaseTwo(File(FileTwo) with { CopiedFrom = FileOne });

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        jobs.Downloader.Downloads.Should().BeEmpty();
        jobs.Downloader.Releases.Should().BeEmpty("a kept package asks nothing of the site");
        jobs.Engine.Installs.Last().Parts.Single().PackagePath.Should().Be(jobs.Downloader.PathOf(FileOne));
        result.PackageFileIds.Should().Equal(FileOne);
        result.Installed!.Parts.Should().ContainSingle().Which.Should().Match<InstalledPart>(part =>
            part.ReleaseFileId == FileTwo.ToString("D") && part.CachedFileId == FileOne.ToString("D"));
        result.Installed.PackageFileIds.Should().Equal(FileOne);
    }

    [Fact]
    public async Task The_download_a_copied_file_came_from_is_kept_like_the_job_s_own_package()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Downloader.Cached[FileOne] = true;
        jobs.PublishReleaseTwo(File(FileTwo) with { CopiedFrom = FileOne });

        (await jobs.RunAsync(JobsHarness.Install())).Outcome.Should().Be(JobOutcome.Succeeded);
        (await jobs.Runner.ClearDownloadsAsync(new HashSet<Guid>(), JobsHarness.Token)).Should().BeTrue();

        jobs.Downloader.Cached.Keys.Should().Equal([FileOne], "the install that just finished needs it, which the list may not know of yet");
    }

    [Fact]
    public async Task A_repair_of_an_install_of_a_replaced_file_installs_its_kept_package()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileTwo) with { Replaces = [FileOne] })));

        var result = await jobs.RunAsync(new RepairJobRequest(record));

        jobs.Downloader.Downloads.Should().BeEmpty();
        jobs.Engine.Installs.Last().Parts.Single().PackagePath.Should().Be(jobs.Downloader.PathOf(FileOne));
        result.Installed!.Parts.Should().ContainSingle().Which.Should().Match<InstalledPart>(part =>
            part.ReleaseFileId == FileTwo.ToString("D") && part.CachedFileId == FileOne.ToString("D"));
        (await jobs.Runner.ClearDownloadsAsync(new HashSet<Guid>(), JobsHarness.Token)).Should().BeTrue();
        jobs.Downloader.Cached.Keys.Should().Equal([FileOne]);

        await jobs.RunAsync(new RepairJobRequest(result.Installed));

        jobs.Downloader.Downloads.Should().BeEmpty("the record now names the replacing file, and its kept download still serves");
        jobs.Engine.Installs.Last().Parts.Single().PackagePath.Should().Be(jobs.Downloader.PathOf(FileOne));
    }

    [Fact]
    public async Task A_new_install_on_another_store_copy_never_takes_a_replaced_file_s_kept_download()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Installed();
        jobs.Library.Publish(Installed(), Installed(OtherGame));
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileTwo) with { Replaces = [FileOne] })) with
        {
            StorefrontLinks = [Link(Game), Link(OtherGame)],
        });

        var result = await jobs.RunAsync(JobsHarness.Install(OtherGame));

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Downloader.Downloads.Should().Equal(FileTwo);
        jobs.Engine.Installs.Last().Parts.Single().PackagePath.Should().Be(jobs.Downloader.PathOf(FileTwo));
    }

    [Fact]
    public async Task A_change_of_add_ons_asks_only_for_the_add_on_beside_a_replaced_base()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var voice = Guid.Parse("33333333-0000-4000-8000-0000000000a1");
        var record = jobs.Engine.Add(WithParts(Record(source: Source(ReleaseOne, FileOne, 1))));
        jobs.Downloader.Cached[FileOne] = true;
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileTwo, "") with { Replaces = [FileOne] }) with
        {
            Features = [Feature("voice-over", "Voice-over", isDefault: false, files: AddOn(voice))],
        }));
        jobs.Choose(("voice-over", true));

        var result = await jobs.RunAsync(new ChangeFeaturesJobRequest(record));

        jobs.Downloader.Downloads.Should().Equal(voice);
        result.PackageFileIds.Should().Equal(FileOne, voice);
    }

    [Fact]
    public async Task A_job_hands_a_laid_out_zip_s_rule_to_the_install()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne) with { Size = 999, Md5 = new string('0', 32), Layout = Layout })));

        var result = await jobs.RunAsync(JobsHarness.Install());

        jobs.Downloader.Releases.Single().Files.Single().Should().Match<PackageRequest>(request =>
            request.Size == 2048 && request.Md5.Length == 0 && request.Sha256 == Layout.ObjectSha256 && request.IsSharedObject && request.HeldDownload == null);
        result.Installed!.Parts!.Single().Folders.Should().Equal(Layout.Folders);
    }

    [Fact]
    public async Task A_kept_package_installs_with_the_rule_its_record_kept()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = KeptInstall(jobs, KeptRule);
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileTwo, "") with { Replaces = [FileOne], Layout = Layout })));

        var repaired = await jobs.RunAsync(new RepairJobRequest(record));

        var part = jobs.Engine.Installs.Last().Parts.Single();
        part.PackagePath.Should().Be(jobs.Downloader.PathOf(FileOne, sharedObject: true));
        part.Folders.Should().Equal(KeptRule);

        jobs.Downloader.CachedObjects.Clear();
        await jobs.RunAsync(new RepairJobRequest(repaired.Installed!));

        part = jobs.Engine.Installs.Last().Parts.Single();
        part.PackagePath.Should().Be(jobs.Downloader.PathOf(FileTwo, sharedObject: true));
        part.Folders.Should().Equal(Layout.Folders);
    }

    [Fact]
    public async Task An_install_from_a_derived_archive_keeps_it_across_a_repair_and_a_put_back()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = KeptInstall(jobs, null);
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne, "") with { Layout = Layout })));

        var repaired = await jobs.RunAsync(new RepairJobRequest(record));

        jobs.Downloader.Downloads.Should().BeEmpty();
        jobs.Engine.Installs.Last().Parts.Single().Should().Match<InstallPart>(part =>
            part.PackagePath == jobs.Downloader.PathOf(FileOne) && part.Folders == null);

        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new IOException("Locked meanwhile.")));
        var failed = await jobs.RunAsync(new RepairJobRequest(repaired.Installed!));

        failed.RestoredPrevious.Should().BeTrue();
        jobs.Downloader.Downloads.Should().BeEmpty();
        jobs.Engine.Installs.Last().Parts.Single().Folders.Should().BeNull();

        jobs.Downloader.Cached.Clear();
        var restarted = await jobs.RunAsync(new RestoreJobRequest(new ReplacementMarker(record, JobKind.Repair, JobOrigin.User, Project)));

        restarted.RestoredPrevious.Should().BeTrue();
        jobs.Downloader.DownloadedFrom.Single().IsSharedObject.Should().BeFalse("bytes without a rule would land as they are written");
        jobs.Engine.Installs.Last().Parts.Single().Folders.Should().BeNull();
    }

    [Fact]
    public async Task A_file_s_archive_and_its_shared_object_stay_apart_across_two_copies_a_repair_and_a_put_back()
    {
        var token = JobsHarness.Token;
        byte[] archived = FakeStorage.Bytes(3000), shared = FakeStorage.Bytes(2048);
        using var folder = new TempDirectory();
        var storage = new FakeStorage(archived);
        storage.Store("/object.zip", shared);
        var api = new CountingApi(_ => new Uri("https://storage.example.com/archive.zip")) { ResolveObject = _ => new Uri("https://storage.example.com/object.zip") };
        using var client = CatalogueHttp.CreateStorageClient(CatalogueFixtures.Options(), storage);
        using var downloader = new PackageDownloader(api, client, folder.Path, ".examplemod", TimeProvider.System, NullLogger<PackageDownloader>.Instance, _ => null);
        await using var jobs = await JobsHarness.CreateAsync(downloader: downloader);
        var archive = (await downloader.DownloadAsync(new PackageRequest(new ReleaseFileLocator("lantern-team", "example-project", ReleaseOne, FileOne),
            PackageFormat.Zip, null), null, token)).Path;
        var record = KeptInstall(jobs, null);
        jobs.Library.Publish(Installed(), Installed(OtherGame));
        var layout = Layout with { ObjectSize = shared.Length, ObjectSha256 = TestPackage.Sha256(shared) };
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne, "") with { Layout = layout })) with { StorefrontLinks = [Link(Game), Link(OtherGame)] });

        (await jobs.RunAsync(JobsHarness.Install(OtherGame))).Outcome.Should().Be(JobOutcome.Succeeded);

        var onOtherCopy = jobs.Engine.Installs.Last().Parts.Single();
        onOtherCopy.Folders.Should().Equal(layout.Folders);
        (await System.IO.File.ReadAllBytesAsync(onOtherCopy.PackagePath, token)).Should().Equal(shared);
        (await System.IO.File.ReadAllBytesAsync(archive, token)).Should().Equal(archived, "another copy has no say over this install's archive");

        var repaired = await jobs.RunAsync(new RepairJobRequest(record));

        jobs.Engine.Installs.Last().Parts.Single().Should().Match<InstallPart>(part => part.PackagePath == archive && part.Folders == null);

        await System.IO.File.WriteAllBytesAsync(archive, [1, 2, 3], token);
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new IOException("Locked meanwhile.")));
        var failed = await jobs.RunAsync(new RepairJobRequest(repaired.Installed!));

        var attempts = jobs.Engine.Installs.TakeLast(2).Select(install => install.Parts.Single()).ToList();
        attempts[0].PackagePath.Should().Be(onOtherCopy.PackagePath);
        attempts[0].Folders.Should().Equal(layout.Folders);
        failed.RestoredPrevious.Should().BeTrue();
        attempts[1].Should().Match<InstallPart>(part => part.PackagePath == archive && part.Folders == null);
        (await System.IO.File.ReadAllBytesAsync(archive, token)).Should().Equal(archived, "the kept object must not go back without a rule");
        api.Layouts.Should().BeFalse();
    }

    [Fact]
    public async Task Putting_a_release_back_applies_the_rule_its_record_kept()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = KeptInstall(jobs, KeptRule);
        jobs.PublishReleaseTwo(File(FileTwo, ""));
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new IOException("Locked meanwhile.")));

        var failed = await jobs.RunAsync(new UpdateJobRequest(record));

        failed.RestoredPrevious.Should().BeTrue();
        jobs.Engine.Installs.Last().Parts.Single().Folders.Should().Equal(KeptRule);

        await jobs.Markers.SaveAsync(new ReplacementMarker(record, JobKind.Update, JobOrigin.User, Project), JobsHarness.Token);
        var marker = (await jobs.Markers.LoadAllAsync(JobsHarness.Token)).Single();
        jobs.Downloader.CachedObjects.Clear();
        var restarted = await jobs.RunAsync(new RestoreJobRequest(marker));

        restarted.Installed!.Parts!.Single().Folders.Should().Equal(KeptRule);
        jobs.Downloader.DownloadedFrom.Last().IsSharedObject.Should().BeTrue();
    }

    // A part with a rule came from a shared object, and one without from the file's archive.
    private static InstallRecord KeptInstall(JobsHarness jobs, IReadOnlyList<FolderRename>? folders)
    {
        (folders is null ? jobs.Downloader.Cached : jobs.Downloader.CachedObjects)[FileOne] = true;
        var record = WithParts(Record(source: Source(ReleaseOne, FileOne, 1)));
        return jobs.Engine.Add(record with { Parts = [record.Parts![0] with { Folders = folders }] });
    }
}
