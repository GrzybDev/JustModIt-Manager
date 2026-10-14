using System.IO.Compression;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Creator;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class CreatorPackageTests : IDisposable
{
    private static readonly Guid InstallJob = new("5c0a7e21-8d4b-4f1e-9a63-2b7c1d0e9f41");
    private static readonly Guid UndoJob = new("9e4d2b17-3a6c-4c8f-b5e2-7f1a0d3c6b52");
    private static readonly Guid TestInstallId = new("1a7f3c90-6e2d-4b5a-8c41-d9e0f2a7b863");

    private readonly CreatorWorkspace _work = new();
    private UiHarness? _ui;
    private long _version = 1;

    public CreatorPackageTests() => Directory.CreateDirectory(Output(""));

    [AvaloniaFact]
    public async Task Copies_alone_build_a_plain_zip_named_after_the_game_and_its_version()
    {
        _work.Author("data/strings.bin", "strings of the original game");
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);

        package.IsZip.Should().BeTrue();
        package.FormatTitle.Should().Be(Strings.Creator_ZipTitle);
        package.FormatText.Should().StartWith(Text.Format(Strings.Creator_FormatReason, ("reason", nameof(FormatReason.CopiesOnly))));
        package.FormatRows.Single(row => row.IsChosen).Extension.Should().Be(".zip");
        package.Version = "1.2";
        await RenderAsync(ui, "creator-package-zip", 1250);

        var output = Output("Lantern Isle v1.2.zip");
        ui.Pickers.SaveFile = output;
        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt);

        ui.Pickers.Suggested.Should().Equal("Lantern Isle v1.2.zip");
        using (var zip = ZipFile.OpenRead(output))
        {
            zip.Entries.Select(entry => entry.FullName).Should().Equal("data/extra/credits.txt");
        }

        package.ResultName.Should().Be("Lantern Isle v1.2.zip");
        package.HasSiteWarnings.Should().BeFalse();
        package.PublishText.Should().Be(Strings.Creator_PublishZip);
        ui.Click(Button(ui, Strings.Creator_Reveal));
        ui.Links.Folders.Should().Equal(Output(""));
    }

    [AvaloniaFact]
    public async Task A_manifest_package_needs_a_name_shows_its_build_under_way_and_stops_without_leaving_a_file()
    {
        _work.Author("data/pl/fonts.dat", "fonts of the translation");
        var files = new HeldFileSystem(Path.Combine(_work.AuthorFolder, "data", "pl", "fonts.dat"));
        files.Reads.Set();
        using var ui = await OpenAsync(files);
        var package = await PackageAsync(ui);

        package.IsManifest.Should().BeTrue();
        package.FormatText.Should().StartWith(Text.Format(Strings.Creator_FormatReason, ("reason", nameof(FormatReason.PatchesFiles))));
        package.PackageTitle.Should().Be("Lantern Isle", "a new package is named after its game");
        package.GameDetails.Should().Contain(Text.Format(Strings.Creator_GameBuild, ("build", "18001")));
        package.PackageTitle = "";
        await UiHarness.WaitUntilAsync(() => package.HasMetadataProblem);
        package.BuildCommand.CanExecute(null).Should().BeFalse();
        package.PackageTitle = "Lantern Isle in Example";
        package.Version = "0.9";
        package.NewAuthor = "Example Team";
        package.AddAuthorCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => !package.HasMetadataProblem && package.Authors.Count == 1);
        await RenderAsync(ui, "creator-package-manifest", 1500);

        var output = Output("Lantern Isle v0.9.examplemod");
        ui.Pickers.SaveFile = output;
        files.Reads.Reset();
        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.FileLine.Length > 0);
        package.IsBuilding.Should().BeTrue();
        package.FileLine.Should().Contain("data/extra/credits.txt");
        package.Stage.Should().Contain(Text.Format(Strings.Creator_BuildStage, ("stage", nameof(BuildStage.Copying))));
        package.BuildTarget.Should().Be(Text.Format(Strings.Creator_BuildTarget, ("path", output)));
        await RenderAsync(ui, "creator-building", 1500);

        var leaving = Page(ui).CanCloseAsync();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        ((ConfirmDialogViewModel)ui.Shell.Dialogs.Current!).Title.Should().Be(Strings.Creator_StopBuildTitle);
        ui.Shell.Dialogs.CancelCurrent();
        (await leaving).Should().BeFalse();
        Page(ui).OpenStepCommand.Execute(Page(ui).Steps[(int)CreatorStep.Review]);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        ui.Shell.Dialogs.CancelCurrent();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null);
        Page(ui).Step.Should().Be(CreatorStep.Package);
        package.IsBuilding.Should().BeTrue();

        ui.Click(Button(ui, CreatorPage.StopText));
        files.Reads.Set();
        await UiHarness.WaitUntilAsync(() => package.HasProblem);
        package.ProblemTitle.Should().Be(Strings.Creator_BuildCancelledTitle);
        package.IsReady.Should().BeTrue();
        File.Exists(output).Should().BeFalse();
        Directory.EnumerateFiles(Output("")).Should().BeEmpty("a stopped build leaves no temporary file either");

        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt);
        using var zip = ZipFile.OpenRead(output);
        zip.GetEntry("manifest.json").Should().NotBeNull();
        package.ResultDetails.Should().Contain("Example Team");
        package.ResultFolder.Should().Be(Output(""));
    }

    [AvaloniaFact]
    public async Task Closing_the_window_during_a_build_waits_until_the_build_removed_its_partial_package()
    {
        _work.Author("data/pl/fonts.dat", "fonts of the translation");
        var files = new HeldFileSystem(Path.Combine(_work.AuthorFolder, "data", "pl", "fonts.dat"));
        files.Reads.Set();
        using var ui = await OpenAsync(files);
        var package = await PackageAsync(ui);
        package.Version = "0.9";
        await UiHarness.WaitUntilAsync(() => !package.HasMetadataProblem);
        ui.Pickers.SaveFile = Output("Lantern Isle v0.9.examplemod");
        files.Reads.Reset();
        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.FileLine.Length > 0);
        Directory.EnumerateFiles(Output("")).Should().ContainSingle("the build writes its package next to where it goes");

        var closing = Page(ui).CanCloseAsync();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, Strings.Creator_StopBuildConfirm));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        (ui.Shell.Dialogs.Current is ChoiceDialogViewModel).Should().BeFalse("closing goes on once the stopped build removed its package");
        closing.IsCompleted.Should().BeFalse();
        files.Reads.Set();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel { IsClosing: false });
        Directory.EnumerateFiles(Output("")).Should().BeEmpty();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, Strings.Creator_UnsavedDiscard));

        (await closing).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task The_game_s_only_project_on_the_site_gives_its_last_release_the_next_version_and_its_authors()
    {
        var project = TestGames.Offer("Lantern Isle", version: 1);
        _work.LanternProjects = [new SiteProjectView(project.ProjectId, project.GameSlug, project.Team.Slug)];
        using var ui = await OpenAsync();
        ui.Agent.Answer = request => request is GetProjectPage ? TestPages.Page(project) : _work.Answer(request);
        var package = await PackageAsync(ui);

        await UiHarness.WaitUntilAsync(() => package.LastRelease.Length > 0);
        ui.Agent.Requests.OfType<GetProjectPage>().Should().Equal(new GetProjectPage(project.GameSlug, project.Team.Slug, project.ProjectId));
        package.LastRelease.Should().Be(Text.Format(Strings.Creator_LastRelease, ("version", GameWording.ShortVersion("1"))));
        package.Version.Should().Be("2");
        package.Authors.Should().Equal("Ada Lantern", "Olek Tide");
        package.AuthorsNote.Should().Be(Text.Format(Strings.Creator_AuthorsFromProject, ("project", "Lantern Isle")));
        await RenderAsync(ui, "creator-package-hints", 1500);

        ui.Click(Button(ui, Text.Format(Strings.Creator_RemoveAuthor, ("name", "Olek Tide"))));
        await UiHarness.WaitUntilAsync(() => package.Authors.Count == 1);
        package.AuthorsNote.Should().Be(Strings.Creator_AuthorsHint, "the authors are no longer the project's");
    }

    [AvaloniaFact]
    public async Task A_game_with_several_projects_on_the_site_gives_no_hints_since_none_is_surely_the_author_s()
    {
        _work.LanternProjects =
        [
            new SiteProjectView(Guid.NewGuid(), "lantern-isle", "lantern-team"),
            new SiteProjectView(Guid.NewGuid(), "lantern-isle", "night-owls"),
        ];
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));

        ui.Agent.Requests.OfType<GetProjectPage>().Should().BeEmpty();
        (package.LastRelease, package.Version, package.AuthorsNote).Should().Be(("", "", Strings.Creator_AuthorsHint));
    }

    [AvaloniaFact]
    public async Task A_new_version_is_built_into_a_file_of_its_own_and_the_earlier_package_stays()
    {
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        package.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.HasMetadataProblem);
        var first = Output("Lantern Isle v1.0.examplemod");
        ui.Pickers.SaveFile = first;
        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt);
        var built = File.GetLastWriteTimeUtc(first);

        ui.Click(Button(ui, Strings.Creator_Rebuild));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt && File.GetLastWriteTimeUtc(first) != built);
        package.Version = "1.1";
        await UiHarness.WaitUntilAsync(() => Page(ui).Session!.Project.Metadata.Version == "1.1");
        var second = Output("Lantern Isle v1.1.examplemod");
        ui.Pickers.SaveFile = second;
        ui.Click(Button(ui, Strings.Creator_Rebuild));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt && package.OutputPath == second);

        ui.Pickers.Suggested.Should().Equal("Lantern Isle v1.0.examplemod", "Lantern Isle v1.1.examplemod");
        File.Exists(first).Should().BeTrue("the package of the earlier version stays");
        File.Exists(second).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_package_built_for_one_copy_of_a_game_is_left_behind_once_the_author_picks_another_copy()
    {
        using var ui = await OpenAsync();
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        ui.Agent.Answer = request => request switch
        {
            GetDetectedGames => new DetectedGames([.. _work.Detected, gog]),
            TestInstallPackage => new JobStarted(InstallJob),
            _ => null,
        };
        var package = await PackageAsync(ui);
        package.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.HasMetadataProblem);
        var steam = Output("Lantern Isle (Steam) v1.0.examplemod");
        ui.Pickers.SaveFile = steam;
        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt);
        var built = File.GetLastWriteTimeUtc(steam);
        ui.Click(Button(ui, Strings.Creator_TestInstall));
        ui.Agent.End(new JobEnded(Job(InstallJob, JobKind.TestInstall, JobStage.Checking), JobOutcome.Failed,
            new JobFailureView(JobFailureReason.GameRunning)));
        package.Test.Should().Be(TestInstallState.Failed);

        var page = Page(ui);
        page.OpenStepCommand.Execute(page.Steps[(int)CreatorStep.Game]);
        ((GameStepViewModel)page.Content!).Choose(gog);

        (package.IsReady, package.OutputPath, package.Test, package.HasTestProblem).Should().Be((true, "", TestInstallState.None, false));
        page.GoTo(CreatorStep.Files);
        await UiHarness.WaitUntilAsync(() => page.Step == CreatorStep.Review && page.Session!.Review is not null
            && page.Session.Comparison!.GameDirectory == gog.Game.InstallDirectory);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Click(Button(ui, Strings.Creator_Confirm));
        await UiHarness.WaitUntilAsync(() => page.Content is PackageStepViewModel);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        Shows(ui, Strings.Creator_TestInstall).Should().BeFalse("the package built so far is the other copy's");

        var other = Output("Lantern Isle (GOG) v1.0.examplemod");
        ui.Pickers.SaveFile = other;
        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt);
        ui.Pickers.Suggested.Should().Equal(["Lantern Isle (Steam) v1.0.examplemod", "Lantern Isle (GOG) v1.0.examplemod"],
            "each copy's package is named after its store and goes where the author says, not over the other one");
        File.GetLastWriteTimeUtc(steam).Should().Be(built);
        ui.Click(Button(ui, Strings.Creator_TestInstall));
        ui.Agent.Requests.OfType<TestInstallPackage>().Last().Should().Be(new TestInstallPackage(gog.Key, other));
    }

    [AvaloniaFact]
    public async Task The_site_s_limits_are_warnings_after_the_build()
    {
        using var ui = await OpenAsync(state: TestStates.Idle(creatorMode: true) with
        {
            Catalogue = new CatalogueView
            {
                ReleaseFiles = new ReleaseFileRules { MaxBytes = 64, Formats = [new ReleaseFileFormat { Extension = ".zip" }] },
            },
        });
        var package = await PackageAsync(ui);
        package.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.HasMetadataProblem);
        ui.Pickers.SaveFile = Output("Lantern Isle.examplemod");

        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt);

        package.SiteWarnings.Should().HaveCount(2);
        package.SiteWarnings[1].Should().Be(CreatorWording.Finding(new PlanFinding(FindingKind.FormatNotOnSite)));
        await RenderAsync(ui, "creator-built", 1500);
    }

    [AvaloniaFact]
    public async Task A_patch_carrying_most_of_its_file_stops_the_build_until_it_is_accepted()
    {
        _work.Game("data/voices.pak", Noise(1, 1_200_000));
        _work.Author("data/voices.pak", Noise(2, 1_200_000));
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        package.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.HasMetadataProblem);
        ui.Pickers.SaveFile = Output("Lantern Isle.examplemod");

        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.HasBuildFindings);

        package.ProblemText.Should().Be(Text.Format(Strings.Creator_BuildFailed, ("failure", nameof(BuildFailure.UnacknowledgedFindings)),
            ("files", 0), ("file", "")));
        File.Exists(Output("Lantern Isle.examplemod")).Should().BeFalse();
        await RenderAsync(ui, "creator-build-findings", 1500);
        ui.Click(Button(ui, Strings.Creator_AcceptWork));
        await UiHarness.WaitUntilAsync(() => Page(ui).Session!.Project.Acknowledged.Count == 1);

        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt);
        File.Exists(Output("Lantern Isle.examplemod")).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_file_changed_since_the_comparison_fails_the_build_and_offers_a_new_one()
    {
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        package.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.HasMetadataProblem);
        _work.Author("data/extra/credits.txt", "credits of the author's work, with one more name");
        ui.Pickers.SaveFile = Output("Lantern Isle.examplemod");

        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.HasProblem);

        package.NeedsComparison.Should().BeTrue();
        package.ProblemText.Should().Contain("credits.txt");
        ui.Click(Button(ui, Strings.Creator_CompareAgain));
        await UiHarness.WaitUntilAsync(() => Page(ui).Step == CreatorStep.Review && Page(ui).Session!.Review is not null);
        Page(ui).Session!.IsConfirmed.Should().BeFalse("a new comparison asks for a new check");
    }

    [AvaloniaFact]
    public async Task A_test_install_runs_through_the_agent_keeps_the_package_as_built_and_is_undone_through_it()
    {
        _work.Author("data/strings.bin", "strings of the original game");
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        var output = Output("Lantern Isle.zip");
        ui.Pickers.SaveFile = output;
        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt);
        ui.Agent.Answer = request => request switch
        {
            TestInstallPackage => new JobStarted(InstallJob),
            UninstallProject => new JobStarted(UndoJob),
            _ => _work.Answer(request),
        };
        ui.Pickers.SaveFile = null;
        ui.Click(Button(ui, Strings.Creator_BuildElsewhere));
        await UiHarness.WaitUntilAsync(() => ui.Pickers.Suggested.Count == 2);
        package.ResultName.Should().Be("Lantern Isle.zip", "a place the author did not pick changes nothing");

        ui.Click(Button(ui, Strings.Creator_TestInstall));
        ui.Agent.Requests.OfType<TestInstallPackage>().Should().Equal(new TestInstallPackage(CreatorWorkspace.Lantern, output));
        ui.Push(State(Job(InstallJob, JobKind.TestInstall, JobStage.Installing), installed: false));
        package.Test.Should().Be(TestInstallState.Installing);
        ui.Push(State(null, installed: true));
        package.Test.Should().Be(TestInstallState.Installed);
        package.CanRebuild.Should().BeFalse("the creator would compare the files with those the package changed");
        await RenderAsync(ui, "creator-test-installed", 1400);

        ui.Click(Button(ui, Strings.Creator_Launch));
        ui.Agent.Requests.OfType<LaunchGame>().Should().Equal(new LaunchGame(CreatorWorkspace.Lantern));
        ui.Click(Button(ui, Strings.Creator_UndoTest));
        ui.Agent.Requests.OfType<UninstallProject>().Should().Equal(new UninstallProject(TestInstallId));
        ui.Push(State(Job(UndoJob, JobKind.Uninstall, JobStage.Uninstalling), installed: true));
        package.Test.Should().Be(TestInstallState.Undoing);
        ui.Push(State(null, installed: false));

        package.Test.Should().Be(TestInstallState.Undone);
        package.CanRebuild.Should().BeTrue();
        ui.Shell.Toasts.Items.Should().ContainSingle().Which.Title.Should().Be(Strings.Creator_TestUndoneTitle);
        await RenderAsync(ui, "creator-tested", 1400);
        ui.Click(Button(ui, Strings.Creator_OpenDashboard));
        ui.Links.Opened.Should().Equal(new Uri("https://example.com/dashboard"));
    }

    [AvaloniaFact]
    public async Task A_test_install_that_failed_between_two_states_on_a_game_outside_the_player_s_list_says_why()
    {
        var package = await TestedPackageAsync();
        var running = new JobFailureView(JobFailureReason.GameRunning) { Processes = ["lantern.exe"] };

        _ui!.Click(Button(_ui, Strings.Creator_TestInstall));
        package.Test.Should().Be(TestInstallState.Installing);
        _ui.Agent.End(new JobEnded(Job(InstallJob, JobKind.TestInstall, JobStage.Checking), JobOutcome.Failed, running));
        _ui.Push(State(null, installed: false));

        package.Test.Should().Be(TestInstallState.Failed);
        package.TestText.Should().Be(GameWording.FailureReason(running, StoreId.Steam, "Lantern Isle")).And.Contain("lantern.exe");
        package.HasTestProblem.Should().BeTrue();
        package.OffersTestInstall.Should().BeTrue();
        await RenderAsync(_ui, "creator-test-failed", 1400);
    }

    [AvaloniaFact]
    public async Task Undoing_a_test_install_says_why_it_failed_and_that_it_is_undone_once_the_install_is_gone()
    {
        var package = await TestedPackageAsync();
        var locked = new JobFailureView(JobFailureReason.FilesLocked) { Files = ["data/strings.bin"], Processes = ["editor.exe"] };
        _ui!.Click(Button(_ui, Strings.Creator_TestInstall));
        _ui.Agent.End(new JobEnded(Job(InstallJob, JobKind.TestInstall, JobStage.Installing), JobOutcome.Succeeded));
        package.Test.Should().Be(TestInstallState.Installing, "the install shows once the state lists it");
        _ui.Push(State(null, installed: true));
        package.Test.Should().Be(TestInstallState.Installed);

        _ui.Click(Button(_ui, Strings.Creator_UndoTest));
        _ui.Agent.End(new JobEnded(Job(UndoJob, JobKind.Uninstall, JobStage.Uninstalling), JobOutcome.Failed, locked));
        package.Test.Should().Be(TestInstallState.Installed);
        package.TestText.Should().Be(Text.Format(Strings.Creator_UndoTestFailed,
            ("reason", GameWording.FailureReason(locked, StoreId.Steam, "Lantern Isle"))));
        package.HasTestProblem.Should().BeTrue();
        _ui.Click(Button(_ui, Strings.Creator_UndoTest));
        _ui.Agent.End(new JobEnded(Job(UndoJob, JobKind.Uninstall, JobStage.Uninstalling), JobOutcome.Succeeded));
        package.Test.Should().Be(TestInstallState.Undoing, "the state still lists the install");
        _ui.Push(State(null, installed: false));

        package.Test.Should().Be(TestInstallState.Undone);
        package.HasTestProblem.Should().BeFalse();
        _ui.Shell.Toasts.Items.Should().ContainSingle().Which.Title.Should().Be(Strings.Creator_TestUndoneTitle);
    }

    [AvaloniaFact]
    public async Task A_test_install_that_ended_while_the_agent_was_out_of_reach_is_told_by_the_next_state()
    {
        var package = await TestedPackageAsync();
        _ui!.Click(Button(_ui, Strings.Creator_TestInstall));
        _ui.Push(State(Job(InstallJob, JobKind.TestInstall, JobStage.Installing), installed: false));

        _ui.Agent.Link = AgentLink.Connecting;
        _ui.Agent.Link = AgentLink.Connected;
        package.Test.Should().Be(TestInstallState.Installing, "the state before the agent went away lists the job");
        _ui.Push(State(null, installed: false));

        package.Test.Should().Be(TestInstallState.Failed);
        package.TestText.Should().Be(Strings.Creator_TestFailed);
    }

    [AvaloniaFact]
    public async Task The_package_step_names_every_control_for_screen_readers()
    {
        _work.Author("data/strings.bin", "strings of the original game");
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        ui.Unnamed().Should().BeEmpty();

        ui.Pickers.SaveFile = Output("Lantern Isle.zip");
        ui.Click(Button(ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Unnamed().Should().BeEmpty();
    }

    public void Dispose()
    {
        _ui?.Dispose();
        _work.Dispose();
    }

    private string Output(string name) => Path.Combine(_work.Root, "Packages", name);

    // A plain package built for the game, with the agent starting the test install and its undoing as the jobs above.
    private async Task<PackageStepViewModel> TestedPackageAsync()
    {
        _work.Author("data/strings.bin", "strings of the original game");
        _ui = await OpenAsync();
        var package = await PackageAsync(_ui);
        _ui.Pickers.SaveFile = Output("Lantern Isle.zip");
        _ui.Click(Button(_ui, Strings.Creator_Build));
        await UiHarness.WaitUntilAsync(() => package.IsBuilt);
        _ui.Agent.Answer = request => request switch
        {
            TestInstallPackage => new JobStarted(InstallJob),
            UninstallProject => new JobStarted(UndoJob),
            _ => _work.Answer(request),
        };
        return package;
    }

    private async Task<UiHarness> OpenAsync(IFileSystem? files = null, AppStateView? state = null)
    {
        var ui = new UiHarness(state ?? TestStates.Idle(creatorMode: true), CreatorWorkspace.Compose(files));
        ui.Agent.Answer = _work.Answer;
        ui.Shell.Navigator.Navigate(new CreatorRoute());
        await UiHarness.WaitUntilAsync(() => ui.Find<CreatorPage>().Any(page => page.IsEffectivelyVisible));
        return ui;
    }

    private async Task<PackageStepViewModel> PackageAsync(UiHarness ui)
    {
        var page = Page(ui);
        page.NewProjectCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => page.Content is GameStepViewModel { IsLoaded: true });
        ((GameStepViewModel)page.Content!).Choose(_work.Detected[1]);
        page.GoTo(CreatorStep.Files);
        ((FilesStepViewModel)page.Content!).UseFolder(_work.AuthorFolder);
        await UiHarness.WaitUntilAsync(() => page.Step == CreatorStep.Review && page.Session!.Review is not null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Click(Button(ui, Strings.Creator_Confirm));
        await UiHarness.WaitUntilAsync(() => page.Content is PackageStepViewModel);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        return (PackageStepViewModel)page.Content!;
    }

    private AppStateView State(JobView? job, bool installed) => TestStates.Idle(creatorMode: true) with
    {
        Version = ++_version,
        Games = installed ? [LanternWithTestInstall()] : [],
        Jobs = job is null ? [] : [job],
    };

    private GameView LanternWithTestInstall() => new()
    {
        Key = CreatorWorkspace.Lantern,
        Name = "Lantern Isle",
        Status = new GameStatusView(MyGameState.TestInstalled),
        Detected = _work.Detected[1].Game,
        Installs =
        [
            new InstallView
            {
                Id = TestInstallId,
                Status = new GameStatusView(MyGameState.TestInstalled),
                InstalledAt = TestStates.Now,
                InstallDirectory = _work.GameFolder,
                BackupDirectory = Path.Combine(_work.GameFolder, ".example-app"),
                PackageFileName = "Lantern Isle.zip",
                IsTest = true,
            },
        ],
    };

    private static JobView Job(Guid id, JobKind kind, JobStage stage) => new()
    {
        Id = id,
        Game = CreatorWorkspace.Lantern,
        GameName = "Lantern Isle",
        Kind = kind,
        Origin = JobOrigin.User,
        InstallId = kind == JobKind.Uninstall ? TestInstallId : null,
        Stage = stage,
        Fraction = 0.4,
        Sequence = 1,
    };

    private static string Noise(int seed, int length)
    {
        var random = new Random(seed);
        return string.Create(length, random, (chars, source) =>
        {
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = (char)source.Next('!', '~');
            }
        });
    }

    private static async Task RenderAsync(UiHarness ui, string name, double height)
    {
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render(name, height);
    }

    private static CreatorPageViewModel Page(UiHarness ui) => (CreatorPageViewModel)ui.Shell.Navigator.Current!;

    private static Button Button(UiHarness ui, string name) =>
        ui.Find<Button>().First(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled
            && ControlAutomationPeer.CreatePeerForElement(button).GetName() == name);

    private static bool Shows(UiHarness ui, string name) =>
        ui.Find<Button>().Any(button => button.IsEffectivelyVisible && ControlAutomationPeer.CreatePeerForElement(button).GetName() == name);
}
