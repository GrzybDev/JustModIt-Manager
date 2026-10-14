using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Tests.TestSupport;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.Jobs;

public sealed class FeatureJobTests
{
    private static readonly Guid VoiceOver = Guid.Parse("33333333-0000-4000-8000-0000000000a1");
    private static readonly Guid Signs = Guid.Parse("33333333-0000-4000-8000-0000000000a2");
    private static readonly Guid VoiceOverCopy = Guid.Parse("33333333-0000-4000-8000-0000000000a3");

    [Fact]
    public async Task Installs_the_base_with_the_add_ons_on_and_asks_for_their_files_in_one_request()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(Page(release: WithAddOns(ReleaseOne, 1, FileOne)));

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        var request = jobs.Engine.Installs.Should().ContainSingle().Subject;
        request.Parts.Select(part => (part.FeatureKey, part.IsOptional, part.ReleaseFileId)).Should().Equal(
            (null, false, FileOne.ToString("D")), ("voice-over", true, VoiceOver.ToString("D")));
        jobs.Downloader.Releases.Should().ContainSingle().Which.Should().Match<Core.Downloads.ReleaseDownload>(download =>
            download.ResolveTogether && download.Files.Select(file => file.Locator.FileUuid).SequenceEqual(new[] { FileOne, VoiceOver }));
        result.Installed!.Source.Should().Match<InstallSource>(source => source.VariantName == null && source.HadVariantChoice == false);
        result.PackageFileIds.Should().Equal(FileOne, VoiceOver);
    }

    [Fact]
    public async Task Installs_the_add_ons_the_player_turned_on()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(Page(release: WithAddOns(ReleaseOne, 1, FileOne)));
        jobs.Choose(("voice-over", false), ("signs", true));

        await jobs.RunAsync(JobsHarness.Install());

        jobs.Engine.Installs.Single().Parts.Select(part => part.FeatureKey).Should().Equal(null, "signs");
    }

    [Fact]
    public async Task A_change_of_add_ons_downloads_only_the_new_package_and_nothing_when_every_one_is_kept()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(Page(release: WithAddOns(ReleaseOne, 1, FileOne)) with { Status = InProgress });
        var record = jobs.Engine.Add(WithParts(Record(source: Source(ReleaseOne, FileOne, 1)), ("voice-over", VoiceOver)));
        jobs.Downloader.Cached[FileOne] = true;
        jobs.Downloader.Cached[VoiceOver] = true;
        jobs.Choose(("signs", true));

        var added = await jobs.RunAsync(new ChangeFeaturesJobRequest(record));
        jobs.Choose();
        var removed = await jobs.RunAsync(new ChangeFeaturesJobRequest(added.Installed!));

        added.Outcome.Should().Be(JobOutcome.Succeeded, "a project the team took back to work keeps changing the add-ons the player has");
        jobs.Downloader.Downloads.Should().Equal(Signs);
        jobs.Downloader.Releases.Should().ContainSingle("nothing is asked for when every package is kept");
        removed.Installed!.Parts!.Select(part => part.FeatureKey).Should().Equal(null, "voice-over");
        jobs.Engine.Calls.Count(call => call == "uninstall").Should().Be(2, "each change takes the install's place");
    }

    [Theory]
    [InlineData("100", false)]
    [InlineData("99", true)]
    public async Task An_add_on_the_install_went_without_is_tried_again_only_on_another_game_build(string installedOn, bool triedAgain)
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(Page(release: WithAddOns(ReleaseOne, 1, FileOne)));
        var record = jobs.Engine.Add(WithParts(Record(source: Source(ReleaseOne, FileOne, 1))) with
        {
            GameBuildId = installedOn,
            LeftOutFeatures = [new LeftOutFeature("voice-over", "Voice-over", PartLeftOutReason.GameVersion)],
        });
        jobs.Downloader.Cached[FileOne] = true;

        await jobs.RunAsync(new RepairJobRequest(record));

        jobs.Engine.Installs.Last().Parts.Select(part => part.FeatureKey).Should()
            .Equal(triedAgain ? new string?[] { null, "voice-over" } : new string?[] { null });
    }

    [Fact]
    public async Task A_new_install_of_a_project_no_longer_completed_is_refused_before_any_download()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        jobs.Catalogue.Add(Page(release: WithAddOns(ReleaseOne, 1, FileOne)) with { Status = InProgress });

        var result = await jobs.RunAsync(JobsHarness.Install());

        result.Failure!.Reason.Should().Be(JobFailureReason.ProjectNotCompleted);
        jobs.Downloader.Releases.Should().BeEmpty();
    }

    [Fact]
    public async Task A_copied_add_on_is_taken_from_the_download_of_the_file_it_copies()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Engine.Add(WithParts(Record(source: Source(ReleaseOne, FileOne, 1)), ("voice-over", VoiceOver)));
        jobs.Downloader.Cached[VoiceOver] = true;
        jobs.Catalogue.Add(Page(release: WithAddOns(ReleaseTwo, 2, FileTwo, voice: AddOn(VoiceOverCopy) with { CopiedFrom = VoiceOver })));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Downloader.Downloads.Should().Equal(FileTwo);
        jobs.Engine.Installs.Last().Parts[1].Should().Match<InstallPart>(part =>
            part.ReleaseFileId == VoiceOverCopy.ToString("D") && part.CachedFileId == VoiceOver.ToString("D"));
        result.PackageFileIds.Should().Equal(FileTwo, VoiceOver);
    }

    [Fact]
    public async Task An_automatic_update_without_an_installed_add_on_changes_nothing_and_downloads_nothing()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Engine.Add(WithParts(Record(source: Source(ReleaseOne, FileOne, 1)), ("voice-over", VoiceOver)));
        jobs.Catalogue.Add(Page(release: WithAddOns(ReleaseTwo, 2, FileTwo, voiceOn: false)));

        var automatic = await jobs.RunAsync(new UpdateJobRequest(record, JobOrigin.Automatic));

        automatic.Failure!.Reason.Should().Be(JobFailureReason.NoPackage);
        jobs.Downloader.Releases.Should().BeEmpty();
        jobs.Engine.Calls.Should().NotContain("uninstall");
        (await jobs.RunAsync(new UpdateJobRequest(record))).Outcome.Should().Be(JobOutcome.Succeeded, "the player may update without it");
    }

    [Fact]
    public async Task An_automatic_update_whose_installed_add_on_no_longer_fits_the_game_waits_for_the_player()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Engine.Add(WithParts(Record(source: Source(ReleaseOne, FileOne, 1)), ("voice-over", VoiceOver)));
        jobs.Catalogue.Add(Page(release: WithAddOns(ReleaseTwo, 2, FileTwo)));
        jobs.Engine.Prediction = new PreflightReport([], [])
        {
            LeftOut = [new LeftOutPart(1, new InstallPart(jobs.Downloader.PathOf(VoiceOver)) { FeatureKey = "voice-over" }, PartLeftOutReason.GameVersion)],
        };

        var automatic = await jobs.RunAsync(new UpdateJobRequest(record, JobOrigin.Automatic));
        var player = await jobs.RunAsync(new UpdateJobRequest(record));

        automatic.Failure!.Reason.Should().Be(JobFailureReason.NewReleaseIncompatible);
        player.Outcome.Should().Be(JobOutcome.Succeeded);
        jobs.Engine.Calls.Count(call => call == "uninstall").Should().Be(1, "only the player's update went ahead");
    }

    [Fact]
    public async Task Only_the_parts_the_prediction_keeps_take_over_what_the_old_install_hands_over()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Engine.Add(WithParts(Record(source: Source(ReleaseOne, FileOne, 1)), ("voice-over", VoiceOver)));
        jobs.Catalogue.Add(Page(release: WithAddOns(ReleaseTwo, 2, FileTwo, signsOn: true)));
        jobs.Engine.Prediction = new PreflightReport([], [])
        {
            LeftOut = [new LeftOutPart(2, new InstallPart(jobs.Downloader.PathOf(Signs)) { FeatureKey = "signs" }, PartLeftOutReason.NewerApp)],
        };

        await jobs.RunAsync(new RepairJobRequest(record));

        jobs.Engine.ReplacedBy.Should().ContainSingle().Which.Should().Equal(
            jobs.Downloader.PathOf(FileTwo), jobs.Downloader.PathOf(VoiceOver, PackageFormat.Manifest));
    }

    [Fact]
    public async Task A_failed_replacement_puts_every_part_of_the_previous_install_back()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = jobs.Engine.Add(WithParts(Record(source: Source(ReleaseOne, FileOne, 1)), ("voice-over", VoiceOver)));
        jobs.Downloader.Cached[FileOne] = true;
        jobs.Downloader.Cached[VoiceOver] = true;
        jobs.Catalogue.Add(Page(release: WithAddOns(ReleaseTwo, 2, FileTwo)));
        jobs.Engine.NextInstalls.Enqueue((_, _, _) => throw new InstallFailedException(rolledBack: true, new IOException("Locked meanwhile.")));

        var result = await jobs.RunAsync(new UpdateJobRequest(record));

        result.RestoredPrevious.Should().BeTrue();
        jobs.Engine.Installs.Last().Parts.Select(part => (Path.GetFileName(part.PackagePath), part.FeatureKey, part.IsOptional)).Should().Equal(
            ($"{FileOne:D}.zip", null, false), ($"{VoiceOver:D}.examplemod", "voice-over", true));
        jobs.Engine.Installs.Last().Restores.Should().Be(record.Id);
    }

    [Fact]
    public async Task A_stop_after_the_uninstall_puts_every_part_of_the_previous_install_back_at_the_start()
    {
        await using var jobs = await JobsHarness.CreateAsync();
        var record = WithParts(Record(source: Source(ReleaseOne, FileOne, 1)), ("voice-over", VoiceOver));
        jobs.Downloader.Cached[FileOne] = true;
        jobs.Downloader.Cached[VoiceOver] = true;

        var result = await jobs.RunAsync(new RestoreJobRequest(new ReplacementMarker(record, JobKind.Update, JobOrigin.User, Project)));

        result.RestoredPrevious.Should().BeTrue();
        result.Installed!.Parts!.Select(part => part.FeatureKey).Should().Equal(null, "voice-over");
    }

    private static Release WithAddOns(Guid release, int version, Guid baseFile, bool voiceOn = true, bool signsOn = false, ReleaseFile? voice = null) =>
        Release(release, version, File(baseFile, "")) with
        {
            Features =
            [
                Feature("voice-over", "Voice-over", voiceOn, files: voice ?? AddOn(VoiceOver)),
                Feature("signs", "Signs", signsOn, files: AddOn(Signs)),
            ],
        };
}
