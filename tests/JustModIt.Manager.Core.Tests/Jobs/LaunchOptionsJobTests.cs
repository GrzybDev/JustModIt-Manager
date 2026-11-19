using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class LaunchOptionsJobTests
{
    private static readonly InstalledLaunchOptions AddedByTheApp = new(["-examplelog"]) { Added = ["-examplelog"] };

    [Theory]
    [InlineData("-examplelog  -windowed", null, "-examplelog", "-windowed")]
    [InlineData("-examplelog \"%command%\"", LaunchOptionsRefusal.UnsafeOption)]
    public async Task An_install_keeps_the_launch_options_its_project_needs_or_why_they_were_refused(string value,
        LaunchOptionsRefusal? refusal, params string[] required)
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(WithLaunchOptions(Page(release: Release(ReleaseOne, 1, File(FileOne))), value));

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Installed!.LaunchOptions.Should().Be(new InstalledLaunchOptions([.. required]) { Refusal = refusal });
    }

    [Fact]
    public async Task An_update_keeps_what_the_app_added_that_the_new_release_still_needs_and_takes_back_the_rest()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Engine.Add(jobs.Installed() with
        {
            LaunchOptions = new InstalledLaunchOptions(["-examplelog", "-oldflag"]) { Added = ["-examplelog", "-oldflag"] },
        });
        jobs.Catalogue.Add(WithLaunchOptions(Page(release: Release(ReleaseTwo, 2, File(FileTwo))), "-examplelog -windowed"));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Installed!.LaunchOptions.Should().Be(new InstalledLaunchOptions(["-examplelog", "-windowed"]) { Added = ["-examplelog"] });
        result.RemovedLaunchOptions.Should().Equal("-oldflag");
    }

    [Fact]
    public async Task An_uninstall_takes_back_what_the_app_added()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Engine.Add(jobs.Installed() with { LaunchOptions = AddedByTheApp });

        (await jobs.RunAsync(new UninstallJobRequest(record))).RemovedLaunchOptions.Should().Equal("-examplelog");
    }

    [Fact]
    public async Task A_previous_release_put_back_keeps_the_launch_options_the_app_added()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Engine.Add(jobs.Installed() with { LaunchOptions = AddedByTheApp });
        jobs.PublishReleaseTwo();
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new IOException("Locked meanwhile.")));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.RestoredPrevious.Should().BeTrue();
        result.Installed!.LaunchOptions.Should().Be(AddedByTheApp);
        result.RemovedLaunchOptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_project_in_the_installs_place_takes_over_the_launch_options_the_app_added_that_it_needs_too()
    {
        await using var jobs = await JobsHarness.CreateAsync(policy: new OneProjectPolicy());
        jobs.Engine.Add(jobs.Installed() with { LaunchOptions = AddedByTheApp });
        jobs.Catalogue.Add(WithLaunchOptions(Page(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), teamSlug: "paper-team"), "-examplelog"));

        var result = await jobs.RunAsync(new InstallJobRequest(Game, "Example Game", Locator(OtherProject, "paper-team")));

        result.RemovedRecord.Should().NotBeNull();
        result.Installed!.LaunchOptions.Should().Be(AddedByTheApp);
        result.RemovedLaunchOptions.Should().BeEmpty();
    }

    private static ProjectPage WithLaunchOptions(ProjectPage page, string options) =>
        page with { StorefrontLinks = [.. page.StorefrontLinks.Select(link => link with { LaunchOptions = options })] };
}
