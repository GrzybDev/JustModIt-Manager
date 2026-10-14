using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.Installation;
using JustModIt.Manager.Core.Tests.TestSupport;
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

        var installed = await jobs.RunAsync(new InstallJobRequest(Game, "Example Game", Locator()));

        installed.Outcome.Should().Be(JobOutcome.Succeeded);
        game.ReadGameFile("readme.txt").Should().Be("Read me, translated");
        installed.Installed!.Source!.ReleaseId.Should().Be(ReleaseOne.ToString("D"));
        jobs.Stages(installed.Job.Id).Should().Equal(JobStage.Queued, JobStage.Preparing, JobStage.Checking, JobStage.Installing);

        var uninstalled = await jobs.RunAsync(new UninstallJobRequest(installed.Installed));

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
        var installed = (await jobs.RunAsync(new InstallJobRequest(Game, "Example Game", Locator()))).Installed!;
        jobs.Catalogue.Add(Page(release: Release(ReleaseTwo, 2, File(FileTwo, requiresManager: true))));

        var result = await jobs.RunAsync(new UpdateJobRequest(installed));

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
        var installed = (await jobs.RunAsync(new InstallJobRequest(Game, "Example Game", Locator()))).Installed!;
        jobs.Catalogue.Add(Page(release: Release(ReleaseTwo, 2, File(FileTwo, requiresManager: true))));

        var update = await jobs.RunAsync(new UpdateJobRequest(installed));
        var test = await jobs.RunAsync(new TestInstallJobRequest(Game, "Example Game", [otherGame]));

        update.Failure!.Reason.Should().Be(JobFailureReason.OtherGame);
        update.RemovedPrevious.Should().BeFalse();
        test.Failure!.Reason.Should().Be(JobFailureReason.OtherGame);
        game.ReadGameFile("readme.txt").Should().Be("Release one");
        (await game.Engine.LoadRecordsAsync(Token)).Should().BeEquivalentTo([installed]);
    }

    [Fact]
    public async Task An_update_hands_the_files_the_software_changes_over_to_the_new_release()
    {
        await using var game = RuntimeFileTests.WithList(new InstallHarness());
        await using var jobs = await JobsAsync(game);
        var original = game.Snapshot();
        RuntimeFileTests.Software().WriteManifestPackage(jobs.Downloader.PathOf(FileOne, PackageFormat.Manifest));
        RuntimeFileTests.Software().Copy("tool/settings.cfg", "release two")
            .WriteManifestPackage(jobs.Downloader.PathOf(FileTwo, PackageFormat.Manifest));
        var installed = await InstallReleaseOneAsync(game, jobs);
        RuntimeFileTests.Play(game);
        jobs.Catalogue.Add(Page(release: Release(ReleaseTwo, 2, File(FileTwo, requiresManager: true))));

        var updated = await jobs.RunAsync(new UpdateJobRequest(installed));

        updated.Outcome.Should().Be(JobOutcome.Succeeded);
        updated.KeptFiles.Should().BeEmpty("handed-over files are neither kept nor reported");
        game.ReadGameFile("tool/list.dat").Should().Be("List rewritten by the software", "the software keeps its state");
        game.ReadGameFile("tool/list_original.dat").Should().Be("Original list");
        (await jobs.RunAsync(new UninstallJobRequest(updated.Installed!))).KeptFiles.Should().BeEmpty();
        game.Snapshot().Should().BeEquivalentTo(original);
        game.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_update_hands_the_files_the_software_changes_to_the_release_it_puts_back()
    {
        await using var game = RuntimeFileTests.WithList(new InstallHarness());
        await using var jobs = await JobsAsync(game);
        var original = game.Snapshot();
        RuntimeFileTests.Software().WriteManifestPackage(jobs.Downloader.PathOf(FileOne, PackageFormat.Manifest));
        RuntimeFileTests.Software().Operation(new CopyOperation("tool/settings.cfg", "files/tool/settings.cfg", 11, new string('0', 64)))
            .Entry("files/tool/settings.cfg", "Release two").WriteManifestPackage(jobs.Downloader.PathOf(FileTwo, PackageFormat.Manifest));
        var installed = await InstallReleaseOneAsync(game, jobs);
        RuntimeFileTests.Play(game);
        jobs.Catalogue.Add(Page(release: Release(ReleaseTwo, 2, File(FileTwo, requiresManager: true))));

        var result = await jobs.RunAsync(new UpdateJobRequest(installed));

        result.RestoredPrevious.Should().BeTrue();
        game.ReadGameFile("tool/list.dat").Should().Be("List rewritten by the software");
        game.ReadGameFile("tool/list_original.dat").Should().Be("Original list");
        (await game.KeptFilesAsync()).Should().BeEmpty("the release put back took every note over");
        await jobs.RunAsync(new UninstallJobRequest(result.Installed!));
        game.Snapshot().Should().BeEquivalentTo(original);
    }

    [Fact]
    public async Task A_failed_update_after_a_game_update_puts_the_release_back_with_what_its_uninstall_held_back()
    {
        await using var game = RuntimeFileTests.WithList(new InstallHarness());
        await using var jobs = await JobsAsync(game);
        RuntimeFileTests.Software().WriteManifestPackage(jobs.Downloader.PathOf(FileOne, PackageFormat.Manifest));
        new TestPackage().Copy("tool/loader.dll", "loader two")
            .Operation(new CopyOperation("tool/settings.cfg", "files/tool/settings.cfg", 11, new string('0', 64)))
            .Entry("files/tool/settings.cfg", "Release two").WriteManifestPackage(jobs.Downloader.PathOf(FileTwo, PackageFormat.Manifest));
        var installed = await InstallReleaseOneAsync(game, jobs);
        RuntimeFileTests.Play(game);
        UpdateGame(game, jobs);
        jobs.Catalogue.Add(Page(release: Release(ReleaseTwo, 2, File(FileTwo, requiresManager: true))));

        var result = await jobs.RunAsync(new UpdateJobRequest(installed));

        result.Failure!.Reason.Should().Be(JobFailureReason.InvalidPackage);
        result.RestoredPrevious.Should().BeTrue("the player must not lose the release that worked");
        game.ReadGameFile("tool/list.dat").Should().Be("List rewritten by the software");
        game.ReadGameFile("tool/loader.dll").Should().Be("loader");
        (await game.KeptFilesAsync()).Should().BeEmpty("the release put back took over what its uninstall held back");
    }

    [Fact]
    public async Task An_update_that_writes_a_file_the_uninstall_would_hold_back_leaves_the_installed_release_alone()
    {
        await using var game = RuntimeFileTests.WithList(new InstallHarness());
        await using var jobs = await JobsAsync(game);
        RuntimeFileTests.Software().WriteManifestPackage(jobs.Downloader.PathOf(FileOne, PackageFormat.Manifest));
        new TestPackage().Copy("tool/loader.dll", "loader two").Copy("tool/list.dat", "List shipped by release two")
            .WriteManifestPackage(jobs.Downloader.PathOf(FileTwo, PackageFormat.Manifest));
        var installed = await InstallReleaseOneAsync(game, jobs);
        RuntimeFileTests.Play(game);
        UpdateGame(game, jobs);
        var played = game.Snapshot();
        jobs.Catalogue.Add(Page(release: Release(ReleaseTwo, 2, File(FileTwo, requiresManager: true))));

        var result = await jobs.RunAsync(new UpdateJobRequest(installed));

        result.Failure!.Reason.Should().Be(JobFailureReason.KeptFilesBlockReplacement, "checking the files in the store would not get past it");
        result.RemovedPrevious.Should().BeFalse();
        game.Snapshot().Should().BeEquivalentTo(played);
        (await game.Engine.LoadRecordsAsync(Token)).Should().ContainSingle().Which.Id.Should().Be(installed.Id);
    }

    [Fact]
    public async Task A_replacement_cut_by_a_stop_hands_the_notes_to_the_release_put_back_at_start()
    {
        await using var game = RuntimeFileTests.WithList(new InstallHarness());
        await using var jobs = await JobsAsync(game);
        var original = game.Snapshot();
        RuntimeFileTests.Software().WriteManifestPackage(jobs.Downloader.PathOf(FileOne, PackageFormat.Manifest));
        var two = RuntimeFileTests.Software().Copy("tool/settings.cfg", "release two")
            .WriteManifestPackage(jobs.Downloader.PathOf(FileTwo, PackageFormat.Manifest));
        var installed = await InstallReleaseOneAsync(game, jobs);
        RuntimeFileTests.Play(game);
        // The stop came after the old uninstall handed the files over and before the new install.
        await game.Engine.UninstallAsync(installed.Id, [two], null, Token);

        var restore = await RestoreAtStartAsync(game, installed);

        restore.RestoredPrevious.Should().BeTrue();
        game.ReadGameFile("tool/list.dat").Should().Be("List rewritten by the software");
        game.ReadGameFile("tool/list_original.dat").Should().Be("Original list");
        (await game.KeptFilesAsync()).Should().BeEmpty("the release put back took every note over");
        (await game.UninstallAsync(restore.Installed!.Id)).KeptFiles.Should().BeEmpty();
        game.Snapshot().Should().BeEquivalentTo(original);
        game.AppFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task A_replacement_cut_by_a_stop_after_a_game_update_puts_the_release_back_at_start_with_what_its_uninstall_held_back()
    {
        await using var game = RuntimeFileTests.WithList(new InstallHarness());
        await using var jobs = await JobsAsync(game);
        RuntimeFileTests.Software().WriteManifestPackage(jobs.Downloader.PathOf(FileOne, PackageFormat.Manifest));
        var two = new TestPackage().Copy("tool/loader.dll", "loader two").WriteManifestPackage(jobs.Downloader.PathOf(FileTwo, PackageFormat.Manifest));
        var installed = await InstallReleaseOneAsync(game, jobs);
        RuntimeFileTests.Play(game);
        UpdateGame(game, jobs);
        (await game.Engine.UninstallAsync(installed.Id, [two], null, Token)).KeptFiles.Should().ContainSingle();

        var restore = await RestoreAtStartAsync(game, installed, build: "43");

        restore.RestoredPrevious.Should().BeTrue("the player must not lose the release that worked");
        game.ReadGameFile("tool/list.dat").Should().Be("List rewritten by the software");
        (await game.KeptFilesAsync()).Should().BeEmpty("the release put back took over what its uninstall held back");
    }

    private static async Task<InstallRecord> InstallReleaseOneAsync(InstallHarness game, JobsHarness jobs)
    {
        jobs.Downloader.Cached[FileOne] = true;
        jobs.Downloader.Cached[FileTwo] = true;
        jobs.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne, requiresManager: true))));
        var installed = await jobs.RunAsync(new InstallJobRequest(Game, "Example Game", Locator()));
        game.ReadGameFile("tool/loader.dll").Should().Be("loader");
        return installed.Installed!;
    }

    // The store updated the game, as both the job's look and the engine's own see it.
    private static void UpdateGame(InstallHarness game, JobsHarness jobs)
    {
        game.Probe.BuildId = "43";
        jobs.Library.Publish(game.Game with { BuildId = "43" });
    }

    // A start of the app after a stop that left the marker of the replacement of an install of release one.
    private static async Task<JobResult> RestoreAtStartAsync(InstallHarness game, InstallRecord replaced, string build = "42")
    {
        await using var start = await JobsAsync(game, recover: false);
        start.Library.Publish(game.Game with { BuildId = build });
        start.Downloader.Cached[FileOne] = true;
        start.Catalogue.Add(Page(release: Release(ReleaseOne, 1, File(FileOne, requiresManager: true))));
        await start.Markers.SaveAsync(new ReplacementMarker(replaced, JobKind.Update, JobOrigin.User, Project), Token);

        await start.Runner.RecoverAsync(Token);
        await Eventually.HoldsAsync(() => !start.Results.IsEmpty, "the restore ends");

        start.MarkerFiles.Should().BeEmpty();
        return start.Results.Single();
    }

    private static async Task<JobsHarness> JobsAsync(InstallHarness game, bool recover = true)
    {
        var jobs = await JobsHarness.CreateAsync(recover, engine: game.Engine);
        jobs.Library.Publish(game.Game);
        jobs.Downloader.Folder = Path.Join(game.Packages, "downloads");
        Directory.CreateDirectory(jobs.Downloader.Folder);
        return jobs;
    }
}
