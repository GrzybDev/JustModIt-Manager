using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class ReleaseDownloadJobTests
{
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
}
