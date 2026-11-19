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
    public async Task An_update_needs_what_the_new_release_needs_and_keeps_what_the_app_added_for_the_project()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Engine.Add(jobs.Installed() with { LaunchOptions = AddedByTheApp });
        jobs.Catalogue.Add(WithLaunchOptions(Page(release: Release(ReleaseTwo, 2, File(FileTwo))), "-windowed"));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Installed!.LaunchOptions.Should().Be(new InstalledLaunchOptions(["-windowed"]) { Added = ["-examplelog"] });
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
    }

    [Fact]
    public async Task Another_project_in_the_installs_place_takes_over_none_of_the_launch_options_the_app_added()
    {
        await using var jobs = await JobsHarness.CreateAsync(policy: new OneProjectPolicy());
        jobs.Engine.Add(jobs.Installed() with { LaunchOptions = AddedByTheApp });
        jobs.Catalogue.Add(Page(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), teamSlug: "paper-team"));

        var result = await jobs.RunAsync(new InstallJobRequest(Game, "Example Game", Locator(OtherProject, "paper-team")));

        result.RemovedRecord.Should().NotBeNull();
        result.Installed!.LaunchOptions.Should().BeNull();
    }

    private static ProjectPage WithLaunchOptions(ProjectPage page, string options) =>
        page with { StorefrontLinks = [.. page.StorefrontLinks.Select(link => link with { LaunchOptions = options })] };
}
