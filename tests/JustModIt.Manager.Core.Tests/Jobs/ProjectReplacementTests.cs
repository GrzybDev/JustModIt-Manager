using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class ProjectReplacementTests
{
    [Fact]
    public async Task By_default_another_project_joins_the_installed_one()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        jobs.Catalogue.Add(OtherProjectPage());

        var result = await jobs.RunAsync(InstallOtherProject());

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        result.RemovedRecord.Should().BeNull();
        jobs.Engine.Calls.Should().NotContain("uninstall");
        jobs.Engine.Records.Should().ContainKey(record.Id);
    }

    [Fact]
    public async Task Installing_a_project_the_game_has_in_another_variant_replaces_its_install_even_next_to_others()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Installed();
        var other = jobs.Engine.Add(Record(source: Source(ReleaseTwo, FileTwo, 1, project: OtherProject)));

        var result = await jobs.RunAsync(JobsHarness.Install(variant: "Text and voice"));

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        result.RemovedRecord.Should().Be(record.Id);
        result.Installed!.Source!.VariantName.Should().Be("Text and voice");
        jobs.Engine.Calls.Should().ContainInOrder("predict", "uninstall", "install " + VoiceFile.ToString("D") + ".zip");
        jobs.Engine.Records.Keys.Should().BeEquivalentTo([other.Id, result.Installed.Id]);
    }

    [Fact]
    public async Task Where_the_app_keeps_one_project_another_replaces_it_the_way_an_update_does()
    {
        await using var jobs = await JobsHarness.CreateAsync(policy: new OneProjectPolicy());
        var record = jobs.Installed();
        jobs.Catalogue.Add(OtherProjectPage());

        var result = await jobs.RunAsync(InstallOtherProject());

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        result.RemovedRecord.Should().Be(record.Id);
        result.Installed!.Source!.ProjectId.Should().Be(OtherProject.ToString("D"));
        jobs.Engine.Calls.Should().Equal("recover", "load records", "predict", "uninstall", "install " + FileTwo.ToString("D") + ".zip");
        jobs.Stages(result.Job.Id).Should()
            .ContainInOrder(JobStage.Downloading, JobStage.Checking, JobStage.Uninstalling, JobStage.Installing);
    }

    [Fact]
    public async Task A_failed_replacement_puts_the_previous_project_back()
    {
        await using var jobs = await JobsHarness.CreateAsync(policy: new OneProjectPolicy());
        var record = jobs.Installed();
        jobs.Catalogue.Add(OtherProjectPage());
        jobs.Engine.NextInstalls.Enqueue((_, _, _) =>
            throw new InstallFailedException(rolledBack: true, new IOException("Locked meanwhile.")));

        var result = await jobs.RunAsync(InstallOtherProject());

        result.Outcome.Should().Be(JobOutcome.Failed);
        result.RestoredPrevious.Should().BeTrue();
        result.RemovedRecord.Should().Be(record.Id);
        result.Installed!.Source!.ProjectId.Should().Be(Project.ToString("D"));
    }

    [Fact]
    public async Task A_new_release_that_does_not_fit_the_game_leaves_the_installed_project_alone()
    {
        await using var jobs = await JobsHarness.CreateAsync(policy: new OneProjectPolicy());
        var record = jobs.Installed();
        jobs.Catalogue.Add(OtherProjectPage());
        jobs.Engine.Prediction = new PreflightReport([new MissingFileIssue("data/strings.pak")], []);

        var result = await jobs.RunAsync(InstallOtherProject());

        result.Failure!.Reason.Should().Be(JobFailureReason.NewReleaseIncompatible);
        jobs.Engine.Calls.Should().NotContain("uninstall");
        jobs.Engine.Records.Should().ContainKey(record.Id);
    }

    [Fact]
    public async Task Neither_an_automatic_install_nor_a_test_install_takes_the_installed_projects_place()
    {
        await using var jobs = await JobsHarness.CreateAsync(policy: new OneProjectPolicy());
        var record = jobs.Installed();
        jobs.Catalogue.Add(OtherProjectPage());

        var automatic = await jobs.RunAsync(InstallOtherProject(JobOrigin.Automatic));
        var test = await jobs.RunAsync(new TestInstallJobRequest(Game, "Example Game", Path.Join("packages", "trial.zip")));

        automatic.Failure!.Reason.Should().Be(JobFailureReason.OtherProjectInstalled);
        test.Failure!.Reason.Should().Be(JobFailureReason.OtherProjectInstalled);
        jobs.Downloader.Downloads.Should().BeEmpty();
        jobs.Engine.Records.Keys.Should().Equal(record.Id);
    }

    [Fact]
    public async Task A_refused_install_fails_with_the_apps_reason_before_anything_is_downloaded()
    {
        await using var jobs = await JobsHarness.CreateAsync(policy: new RefusingPolicy());
        jobs.Installed();
        jobs.Catalogue.Add(OtherProjectPage());

        var result = await jobs.RunAsync(InstallOtherProject());

        result.Failure!.Reason.Should().Be(JobFailureReason.OtherProjectInstalled);
        jobs.Catalogue.PageRequests.Should().BeEmpty();
    }

    private static InstallJobRequest InstallOtherProject(JobOrigin origin = JobOrigin.User) =>
        new(Game, "Example Game", Locator(OtherProject, "paper-team"), null, origin);

    private static ProjectPage OtherProjectPage() =>
        Page(OtherProject, Release(ReleaseTwo, 1, File(FileTwo)), teamSlug: "paper-team");

    private sealed class RefusingPolicy : IInstallPolicy
    {
        public InstallAdmission Admit(IReadOnlyList<InstallRecord> installs) =>
            InstallAdmission.Refused(JobFailureReason.OtherProjectInstalled);
    }
}
