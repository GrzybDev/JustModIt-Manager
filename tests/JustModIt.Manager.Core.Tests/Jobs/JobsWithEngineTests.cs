using System.Collections.Concurrent;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.Installation;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class JobsWithEngineTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_install_and_its_uninstall_leave_the_game_exactly_as_it_was()
    {
        await using var game = new InstallHarness();
        await using var jobs = await JobsAsync(game);
        var original = game.Snapshot();
        new TestPackage().Copy("data/strings.pak", "Translated strings").Copy("readme.txt", "Read me, translated")
            .WriteManifestPackage(jobs.Downloader.PathOf(FileOne, PackageFormat.Manifest));
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne, requiresManager: true))));
        jobs.Downloader.Cached[FileOne] = true;

        var installed = await RunAsync(jobs, new InstallJobRequest(InstallHarness.Key, "Example Game", Locator(), null));

        installed.Outcome.Should().Be(JobOutcome.Succeeded);
        game.ReadGameFile("readme.txt").Should().Be("Read me, translated");
        installed.Installed!.Source!.ReleaseId.Should().Be(ReleaseOne.ToString("D"));
        jobs.Stages(installed.Job.Id).Should().Equal(JobStage.Queued, JobStage.Preparing, JobStage.Checking, JobStage.Installing);

        var uninstalled = await RunAsync(jobs, new UninstallJobRequest(installed.Installed));

        uninstalled.Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Stages(uninstalled.Job.Id).Should().Equal(JobStage.Queued, JobStage.Checking, JobStage.Uninstalling);
        game.Snapshot().Should().BeEquivalentTo(original);
        (await game.Engine.LoadRecordsAsync(Token)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_update_that_fails_while_installing_puts_the_previous_release_back_on_the_game()
    {
        await using var game = new InstallHarness();
        await using var jobs = await JobsAsync(game);
        new TestPackage().Copy("readme.txt", "Release one").WriteManifestPackage(jobs.Downloader.PathOf(FileOne, PackageFormat.Manifest));
        new TestPackage().Operation(new CopyOperation("readme.txt", "files/readme.txt", 11, new string('0', 64)))
            .Entry("files/readme.txt", "Release two").WriteManifestPackage(jobs.Downloader.PathOf(FileTwo, PackageFormat.Manifest));
        jobs.Downloader.Cached[FileOne] = true;
        jobs.Downloader.Cached[FileTwo] = true;
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne, requiresManager: true))));
        var installed = (await RunAsync(jobs, new InstallJobRequest(InstallHarness.Key, "Example Game", Locator(), null))).Installed!;
        jobs.Catalogue.Add(Page(release: Release(ReleaseTwo, 2, File(FileTwo, requiresManager: true))));

        var result = await RunAsync(jobs, new UpdateJobRequest(installed));

        result.Failure!.Reason.Should().Be(JobFailureReason.InvalidPackage);
        result.RestoredPrevious.Should().BeTrue();
        game.ReadGameFile("readme.txt").Should().Be("Release one");
        (await game.Engine.LoadRecordsAsync(Token)).Should().ContainSingle()
            .Which.Source!.ReleaseId.Should().Be(ReleaseOne.ToString("D"));
    }

    [Fact]
    public async Task An_update_and_a_test_install_refuse_a_package_made_for_another_game_and_keep_the_installed_release()
    {
        await using var game = new InstallHarness();
        await using var jobs = await JobsAsync(game);
        new TestPackage().Copy("readme.txt", "Release one").WriteManifestPackage(jobs.Downloader.PathOf(FileOne, PackageFormat.Manifest));
        var otherGame = new TestPackage().Copy("readme.txt", "Release two").Targets(new PackageTarget(StoreId.Steam, "3000"))
            .WriteManifestPackage(jobs.Downloader.PathOf(FileTwo, PackageFormat.Manifest));
        jobs.Downloader.Cached[FileOne] = true;
        jobs.Downloader.Cached[FileTwo] = true;
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne, requiresManager: true))));
        var installed = (await RunAsync(jobs, new InstallJobRequest(InstallHarness.Key, "Example Game", Locator(), null))).Installed!;
        jobs.Catalogue.Add(Page(release: Release(ReleaseTwo, 2, File(FileTwo, requiresManager: true))));

        var update = await RunAsync(jobs, new UpdateJobRequest(installed));
        var test = await RunAsync(jobs, new TestInstallJobRequest(InstallHarness.Key, "Example Game", otherGame));

        update.Failure!.Reason.Should().Be(JobFailureReason.OtherGame);
        update.RemovedPrevious.Should().BeFalse();
        test.Failure!.Reason.Should().Be(JobFailureReason.OtherGame);
        game.ReadGameFile("readme.txt").Should().Be("Release one");
        (await game.Engine.LoadRecordsAsync(Token)).Should().Equal(installed);
    }

    private static async Task<JobResult> RunAsync(JobsContext jobs, JobRequest request) =>
        await jobs.Runner.Start(request).Completion.WaitAsync(TimeSpan.FromSeconds(30), Token);

    private static async Task<JobsContext> JobsAsync(InstallHarness game)
    {
        var library = new FakeLibrary();
        library.Publish(game.Game);
        var downloader = new FakeDownloader { Folder = Path.Join(game.Packages, "downloads") };
        Directory.CreateDirectory(downloader.Folder);
        var catalogue = new FakeCatalogue();
        var runner = new JobRunner(game.Engine, catalogue, downloader, library, new FakeRunningGames(), new FakeSettings(),
            new FakeTimeProvider(InstallHarness.Now), NullLogger<JobRunner>.Instance);
        await runner.RecoverAsync(Token);
        var context = new JobsContext(runner, catalogue, downloader);
        runner.Changed += (_, job) => context.Changes.Enqueue(job);
        return context;
    }

    private sealed record JobsContext(JobRunner Runner, FakeCatalogue Catalogue, FakeDownloader Downloader) : IAsyncDisposable
    {
        public ConcurrentQueue<JobSnapshot> Changes { get; } = new();

        public IReadOnlyList<JobStage> Stages(Guid job) => [.. Changes.Where(change => change.Id == job).Select(change => change.Stage)];

        public ValueTask DisposeAsync() => Runner.DisposeAsync();
    }
}
