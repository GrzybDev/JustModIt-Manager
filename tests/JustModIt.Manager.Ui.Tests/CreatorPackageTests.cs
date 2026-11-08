using System.IO.Compression;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Tests.TestSupport;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Projects;
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

        package.Description.IsManifest.Should().BeFalse();
        package.Description.FormatTitle.Should().Be(Strings.Creator_ZipTitle);
        package.Description.FormatText.Should().StartWith(Text.Format(Strings.Creator_FormatReason, ("reason", nameof(FormatReason.CopiesOnly))));
        package.Description.FormatRows.Single(row => row.IsChosen).Extension.Should().Be(".zip");
        package.Description.Version = "1.2";
        await RenderAsync(ui, "creator-package-zip", 1250);

        var output = Output("Lantern Isle v1.2.zip");
        await BuildAsync(ui);
        await BuiltAsync(package);

        using (var zip = ZipFile.OpenRead(output))
        {
            zip.Entries.Select(entry => entry.FullName).Should().Equal("data/extra/credits.txt");
        }

        package.Build.ResultName.Should().Be("Lantern Isle v1.2.zip");
        package.Build.HasSiteWarnings.Should().BeFalse();
        package.Build.PublishText.Should().Be(Strings.Creator_PublishZip);
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

        package.Description.IsManifest.Should().BeTrue();
        package.Description.FormatText.Should().StartWith(Text.Format(Strings.Creator_FormatReason, ("reason", nameof(FormatReason.PatchesFiles))));
        package.Description.PackageTitle.Should().Be("Lantern Isle", "a new package is named after its game");
        package.VersionLine.Should().Contain(Text.Format(Strings.Creator_GameBuild, ("build", "18001")));
        package.Description.PackageTitle = "";
        await UiHarness.WaitUntilAsync(() => package.Description.HasMetadataProblem);
        await UiHarness.WaitUntilAsync(() => package.BlockedText.Contains(Strings.Creator_BlockedTitle, StringComparison.Ordinal));
        package.CanBuild.Should().BeFalse();
        package.Description.PackageTitle = "Lantern Isle in Example";
        package.Description.Version = "0.9";
        package.Description.NewAuthor = "Example Team";
        package.Description.AddAuthorCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem && package.Description.Authors.Count == 1);
        await RenderAsync(ui, "creator-package-manifest", 1500);

        var output = Output("Lantern Isle v0.9.examplemod");
        files.Reads.Reset();
        await BuildAsync(ui);
        await UiHarness.WaitUntilAsync(() => package.Build.FileLine.Length > 0);
        package.Build.IsBuilding.Should().BeTrue();
        package.Build.FileLine.Should().Contain("data/extra/credits.txt");
        package.Build.Stage.Should().Contain(Text.Format(Strings.Creator_BuildStage, ("stage", nameof(BuildStage.Copying))));
        package.OutputText.Should().Be(Text.Format(Strings.Creator_OutputTo, ("path", Output(""))));
        await RenderAsync(ui, "creator-building", 1500);

        var leaving = ui.Shell.Navigator.CanCloseAsync();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        ((ConfirmDialogViewModel)ui.Shell.Dialogs.Current!).Title.Should().Be(Strings.Creator_StopBuildTitle);
        ui.Shell.Dialogs.CancelCurrent();
        (await leaving).Should().BeFalse();
        ui.Find<CellPanel>().First(panel => panel.IsEffectivelyVisible).IsEffectivelyEnabled.Should().BeFalse("the project cannot change under its build");
        package.Tab = ProjectTab.Description;
        package.Tab.Should().Be(ProjectTab.Description, "the tabs can be looked at while the build runs");
        ui.Find<TransitioningContentControl>("TabContent").IsEffectivelyEnabled.Should().BeFalse("the project cannot change under its build");
        package.Tab = ProjectTab.Release;
        package.Build.IsBuilding.Should().BeTrue();

        ui.Click(Button(ui, Strings.Creator_Stop));
        files.Reads.Set();
        await BuildEndedAsync(package);
        package.Build.ProblemTitle.Should().Be(Strings.Creator_BuildCancelledTitle);
        package.Build.IsReady.Should().BeTrue();
        File.Exists(output).Should().BeFalse();
        Directory.EnumerateFiles(Output("")).Should().BeEmpty("a stopped build leaves no temporary file either");

        await BuildAsync(ui);
        await BuiltAsync(package);
        using var zip = ZipFile.OpenRead(output);
        zip.GetEntry("manifest.json").Should().NotBeNull();
        package.Build.ResultDetails.Should().Contain("Example Team");
        package.Build.ResultFolder.Should().Be(Output(""));
        await RenderAsync(ui, "creator-built-manifest", null);

        package.Tab = ProjectTab.Description;
        await RenderAsync(ui, "creator-description", null);
        package.Description.PackageTitle = "Lantern Isle in Example, second edition";
        package.Session!.Project.Metadata.Title.Should().Be("Lantern Isle in Example, second edition", "the description stays open after a build");
        package.Build.IsBuilt.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_plan_made_while_the_description_shows_is_in_the_operations_once_the_tab_is_left()
    {
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        var counts = package.Review.Counts;
        package.Tab = ProjectTab.Description;
        _work.Author("data/extra/more.txt", "another file of the author's work");

        await package.CheckAsync(anyway: true);
        await UiHarness.WaitUntilAsync(() => package.Cell!.IsCompared && package.Session!.Review!.Plan.Operations.Count == 3);
        package.Review.Counts.Should().Be(counts, "the rows are not rebuilt for each key typed in the description");

        package.Tab = ProjectTab.Release;
        package.Review.Counts.Should().NotBe(counts);
    }

    [AvaloniaFact]
    public async Task Stopping_a_build_on_a_drive_that_stopped_answering_ends_it_within_the_limit()
    {
        var (ui, package, files) = await BuildingAsync();
        using var shown = ui;

        ui.Click(Button(ui, Strings.Creator_Stop));
        await UiHarness.WaitUntilAsync(() =>
        {
            ui.Clock.Advance(LibraryAccess.TimeLimit);
            return !package.Build.BuildCommand.IsRunning;
        });

        package.Build.ProblemTitle.Should().Be(Strings.Creator_BuildCancelledTitle);
        files.Reads.Set();
    }

    [AvaloniaFact]
    public async Task Closing_the_window_during_a_build_waits_until_the_build_removed_its_partial_package()
    {
        var (ui, _, files) = await BuildingAsync();
        using var shown = ui;
        Directory.EnumerateFiles(Output("")).Should().ContainSingle("the build writes its package next to where it goes");

        var closing = ui.Shell.Navigator.CanCloseAsync();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, Strings.Creator_StopBuildConfirm));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        closing.IsCompleted.Should().BeFalse("closing goes on once the stopped build removed its package");
        files.Reads.Set();

        (await closing).Should().BeTrue();
        Directory.EnumerateFiles(Output("")).Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task A_running_build_keeps_the_window_open_until_it_is_stopped()
    {
        var (ui, package, files) = await BuildingAsync();
        using var shown = ui;

        foreach (var closing in new Func<Task<bool>>[] { () => ui.Agent.ShutdownHandler!(ShutdownReason.Update), ui.Shell.CloseFlow.DecideAsync })
        {
            var answer = closing();
            await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
            ((ConfirmDialogViewModel)ui.Shell.Dialogs.Current!).Title.Should().Be(Strings.Creator_StopBuildTitle);
            ui.Shell.Dialogs.CancelCurrent();
            (await answer).Should().BeFalse();
            await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null);
            package.Build.IsBuilding.Should().BeTrue();
        }

        var stopping = ui.Agent.ShutdownHandler!(ShutdownReason.Update);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, Strings.Creator_StopBuildConfirm));
        files.Reads.Set();

        (await stopping).Should().BeTrue();
        package.Build.IsBuilding.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task The_game_s_only_project_on_the_site_gives_its_last_release_the_next_version_and_its_authors()
    {
        var project = TestGames.Offer("Lantern Isle", version: 1);
        _work.LanternProjects = [new SiteProjectView(project.ProjectId, project.GameSlug, project.Team.Slug)];
        using var ui = await OpenAsync();
        ui.Agent.Answer = request => request is GetProjectPage ? TestPages.Page(project) : _work.Answer(request);
        var package = await PackageAsync(ui);

        await UiHarness.WaitUntilAsync(() => package.Description.LastRelease.Length > 0);
        ui.Agent.Requests.OfType<GetProjectPage>().Should().NotBeEmpty().And.OnlyContain(request => request == new GetProjectPage(project.GameSlug, project.Team.Slug, project.ProjectId),
            "the new project's dialog and its page ask for the one project that is surely the author's");
        package.Description.LastRelease.Should().Be(Text.Format(Strings.Creator_LastRelease, ("version", GameWording.ShortVersion("1"))));
        package.Description.Version.Should().Be("2");
        package.Description.Authors.Should().Equal("Ada Lantern", "Olek Tide");
        package.Description.AuthorsNote.Should().Be(Text.Format(Strings.Creator_AuthorsFromProject, ("project", "Lantern Isle")));
        await RenderAsync(ui, "creator-package-hints", 1500);

        package.Tab = ProjectTab.Description;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Click(Button(ui, Text.Format(Strings.Creator_RemoveAuthor, ("name", "Olek Tide"))));
        await UiHarness.WaitUntilAsync(() => package.Description.Authors.Count == 1);
        package.Description.AuthorsNote.Should().Be(Strings.Creator_AuthorsHint, "the authors are no longer the project's");
    }

    [AvaloniaFact]
    public async Task Going_to_another_page_during_a_build_keeps_it_running_and_the_close_asks_once()
    {
        var (ui, package, files) = await BuildingAsync();
        using var shown = ui;
        var page = Page(ui);

        ui.Shell.Navigator.Navigate(new SettingsRoute());
        ui.Shell.Navigator.Navigate(new CreatorRoute());
        package.Build.IsBuilding.Should().BeTrue("the sidebar drops the project's page, not its work");
        var creator = (CreatorPageViewModel)ui.Shell.Navigator.Current!;
        await UiHarness.WaitUntilAsync(() => creator.Cards.SingleOrDefault()?.Line?.Label == Text.Format(Strings.Creator_CardState, ("state", "Building"), ("path", "")));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        ui.Click(ui.Find<Button>().First(button => button.DataContext is ProjectCard && button.Classes.Contains("gameCard")));
        ui.Shell.Navigator.Current.Should().BeSameAs(page, "the project's card leads back to the work under way");

        List<DialogViewModel> asked = [];
        ui.Shell.Dialogs.PropertyChanged += (_, _) =>
        {
            if (ui.Shell.Dialogs.Current is { } dialog && !asked.Contains(dialog))
            {
                asked.Add(dialog);
            }
        };
        var closing = ui.Agent.ShutdownHandler!(ShutdownReason.Exit);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, Strings.Creator_StopBuildConfirm));
        files.Reads.Set();

        (await closing).Should().BeTrue();
        asked.Should().ContainSingle("the library page alone answers for the open project");
    }

    [AvaloniaFact]
    public async Task Removing_the_open_project_during_a_build_asks_stops_and_leaves_no_folder()
    {
        var (ui, package, files) = await BuildingAsync();
        using var shown = ui;
        var session = Page(ui).Session!;
        var folder = Path.Combine(ui.CreatorDirectory, session.Project.Id.ToString());
        await UiHarness.WaitUntilAsync(() => File.Exists(Path.Combine(folder, "project.json")));
        ui.Click(Button(ui, Strings.Creator_Projects));
        var creator = (CreatorPageViewModel)ui.Shell.Navigator.Current!;
        await UiHarness.WaitUntilAsync(() => creator.Cards.Any());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        var remove = async () =>
        {
            ui.Click(ui.Find<Button>().First(button => button.DataContext is ProjectCard && button.Classes.Contains("cardMore")));
            await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel { IsClosing: false });
            await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
            ui.Click(Button(ui, Strings.Creator_Remove));
            await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
            await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
            ui.Click(Button(ui, Strings.Creator_Remove));
            await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false, Title: var title } && title == Strings.Creator_StopBuildTitle);
            await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        };

        await remove();
        ui.Shell.Dialogs.CancelCurrent();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        (creator.OpenPage is not null, package.Build.IsBuilding, Directory.Exists(folder)).Should().Be((true, true, true), "a refused question ends the removal");

        await remove();
        ui.Click(Button(ui, Strings.Creator_StopBuildConfirm));
        files.Reads.Set();
        await UiHarness.WaitUntilAsync(() => creator.OpenPage is null && !creator.Cards.Any());
        Directory.Exists(folder).Should().BeFalse();

        session.Change(project => project with { Name = "Edited after the removal" });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        Directory.Exists(folder).Should().BeFalse("nothing brings a removed project's folder back");
    }

    [AvaloniaFact]
    public async Task A_late_answer_of_the_site_leaves_a_project_closed_and_opened_again_as_it_is()
    {
        var project = TestGames.Offer("Lantern Isle", version: 1);
        _work.LanternProjects = [new SiteProjectView(project.ProjectId, project.GameSlug, project.Team.Slug)];
        var site = new TaskCompletionSource();
        using var ui = await OpenAsync();
        ui.Agent.Answer = request => request is GetProjectPage ? TestPages.Page(project) : _work.Answer(request);
        ui.Agent.Held = request => request is GetProjectPage ? site.Task : Task.CompletedTask;
        await PackageAsync(ui);
        var closed = Page(ui).Session!;
        ui.Click(Button(ui, Strings.Creator_Projects));
        var creator = (CreatorPageViewModel)ui.Shell.Navigator.Current!;
        (await creator.CloseProjectAsync()).Should().BeTrue();
        await UiHarness.WaitUntilAsync(() => creator.Cards.Any());
        creator.OpenCardCommand.Execute(creator.Cards.Single());
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { Cell.IsCompared: true, Session.Review: not null });
        var reopened = Page(ui).Session!;
        reopened.Change(current => current with { Name = "Edited in the reopened session" });
        (await reopened.FlushAsync()).Should().BeTrue();

        site.SetResult();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));

        closed.Project.Metadata.Version.Should().BeEmpty();
        using var library = new Creator.Projects.ProjectLibrary(ui.CreatorDirectory, TimeProvider.System);
        (await library.Store(reopened.Project.Id).LoadAsync(CancellationToken.None)).Should().Be(reopened.Project);
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
        (package.Description.LastRelease, package.Description.Version, package.Description.AuthorsNote).Should().Be(("", "", Strings.Creator_AuthorsHint));
    }

    [AvaloniaFact]
    public async Task In_the_smallest_window_the_title_stands_whole_beside_the_library_s_buttons()
    {
        using var ui = await OpenAsync(width: 960, height: 600);
        var page = ui.Find<CreatorPage>().Single();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        var title = page.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("pageTitle"));
        var shown = title.Bounds.Width;
        title.Measure(Size.Infinity);
        title.DesiredSize.Width.Should().BeLessThanOrEqualTo(shown + 0.5, "the title is not cut");
        Button(ui, Strings.Creator_Import).Classes.Should().Contain("icon", "the import button keeps only its icon, and its name");
        Shows(ui, Strings.Creator_New).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_new_version_is_built_into_a_file_of_its_own_and_the_earlier_package_stays()
    {
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        var first = Output("Lantern Isle v1.0.examplemod");
        await BuildAsync(ui);
        await BuiltAsync(package);
        var built = File.GetLastWriteTimeUtc(first);

        await BuildAsync(ui);
        await BuiltAsync(package);
        File.GetLastWriteTimeUtc(first).Should().NotBe(built, "the package is written again");
        package.Description.Version = "1.1";
        await UiHarness.WaitUntilAsync(() => Page(ui).Session!.Project.Metadata.Version == "1.1");
        var second = Output("Lantern Isle v1.1.examplemod");
        await BuildAsync(ui);
        await BuiltAsync(package);
        package.Build.OutputPath.Should().Be(second);

        File.Exists(first).Should().BeTrue("the package of the earlier version stays");
        File.Exists(second).Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(1280, 800, "")]
    [InlineData(960, 600, "-minimum")]
    public async Task A_package_naming_files_changed_while_the_game_runs_says_why_only_the_app_installs_it_and_records_them(
        double width, double height, string suffix)
    {
        var library = Noise(4, 4096);
        var movies = Noise(5, 3000);
        _work.Author("data/strings.bin", "strings of the original game");
        _work.Game("dx11/render.dll", library);
        _work.Author("dx11/render_o.dll", library);
        _work.Author("dx11/render.dll", Noise(6, 3500));
        _work.Game("data/movies.idx", movies);
        using var ui = await OpenAsync(width: width, height: height);
        var package = await PackageAsync(ui, part => part
            .WithRuntimeFile(new RuntimeFileChoice("data/movies.idx", RuntimeFileKind.ChangesGameFile))
            .WithRuntimeFile(new RuntimeFileChoice("data/movies_original.idx", RuntimeFileKind.CreatesFile)));

        package.Description.IsManifest.Should().BeTrue();
        package.Description.FormatText.Should().StartWith(Text.Format(Strings.Creator_FormatReason, ("reason", nameof(FormatReason.RenamesFiles)))
            + " " + Text.Format(Strings.Creator_FormatReason, ("reason", nameof(FormatReason.DeclaresRuntimeFiles))));
        ui.Find<Pages.Creator.ProjectPage>().Single().GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToHome();
        await RenderAsync(ui, "creator-runtime-package" + suffix, null);

        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        await BuildAsync(ui);
        await BuiltAsync(package);

        await using var built = await new PackageReader().OpenAsync(Output("Lantern Isle v1.0.examplemod"), TestContext.Current.CancellationToken);
        built.Manifest!.Requires.Should().Contain(PackageCapabilities.RuntimeFiles);
        built.Manifest.RuntimeFiles!.Select(file => (file.Path, file.Originals?.Single().Size)).Should().Equal(
            ("data/movies.idx", (long?)movies.Length),
            ("data/movies_original.idx", null));
    }

    [AvaloniaFact]
    public async Task Each_version_compares_and_builds_in_its_own_layout()
    {
        using var ui = await OpenAsync();
        // The other store's copy keeps the game's data in a folder of another name.
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        Directory.Move(Path.Combine(gog.Game.InstallDirectory, "data"), Path.Combine(gog.Game.InstallDirectory, "content"));
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : _work.Answer(request);
        var page = await PackageAsync(ui);
        page.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => page.HasOffer && !page.Description.HasMetadataProblem);
        ui.Click(Button(ui, page.OfferAction));
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.Cells.All(cell => cell.IsCompared && cell.Review is not null) && page.CanBuild,
            () => page.BlockedText);

        page.Session!.Project.Versions[1].Folders.Should().Equal(new FolderRename("data", "content"));
        page.Cells[1].Folded.Should().Contain(Text.Format(Strings.Creator_FolderMoved, ("from", "data"), ("to", "content")), "a wrong rule would put copies where the game does not read");
        page.Cells.Select(cell => cell.Review!.Plan.Operations.Select(operation => (operation.Kind, operation.Path))).Should().AllSatisfy(operations =>
            operations.Should().BeEquivalentTo([(OperationKind.Patch, "data/strings.bin"), (OperationKind.Copy, "data/extra/credits.txt")]), "both versions plan in the project's layout");
        await BuildAsync(ui);
        await BuiltAsync(page);

        foreach (var (name, folder) in new[] { ("Lantern Isle (Steam) v1.0.examplemod", "data"), ("Lantern Isle (GOG) v1.0.examplemod", "content") })
        {
            await using var built = await new PackageReader().OpenAsync(Output(name), TestContext.Current.CancellationToken);
            built.Manifest!.Operations.Select(operation => operation switch { PatchOperation patch => patch.Path, CopyOperation copy => copy.Path, _ => "" })
                .Should().BeEquivalentTo($"{folder}/strings.bin", $"{folder}/extra/credits.txt");
        }
    }

    [AvaloniaFact]
    public async Task A_version_taken_off_the_project_drops_its_cell_and_its_packages()
    {
        using var ui = await OpenAsync();
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        File.WriteAllText(Path.Combine(gog.Game.InstallDirectory, "data", "strings.bin"), "strings of the original game, as the other store ships them");
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : _work.Answer(request);
        var page = await PackageAsync(ui);
        page.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => page.HasOffer && !page.Description.HasMetadataProblem);
        ui.Click(Button(ui, page.OfferAction));
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.CanBuild, () => page.BlockedText);
        await BuildAsync(ui);
        await BuiltAsync(page);

        ui.Click(Button(ui, page.Cells[1].Name));
        ui.Click(Button(ui, page.Cells[1].RemoveText));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, Strings.Creator_RemoveVersionConfirm));

        await UiHarness.WaitUntilAsync(() => !page.IsTable && page.Record!.Parts.SelectMany(part => part.Packages).All(package => !package.Versions.Contains(gog.Key)));
        page.Cells.Select(cell => cell.Key.Version).Should().Equal(CreatorWorkspace.Lantern);
        page.Build.Packages.Should().NotContain(row => row.Versions.Contains(gog.Key));
        page.Record!.Parts.SelectMany(part => part.Packages).Select(package => package.Path).Should().Equal(Output("Lantern Isle (Steam) v1.0.examplemod"));
        File.Exists(Output("Lantern Isle (GOG) v1.0.examplemod")).Should().BeTrue("the files on the disk stay");
    }

    [AvaloniaFact]
    public async Task One_button_builds_a_package_per_version_named_after_its_store_and_tests_the_chosen_one()
    {
        using var ui = await OpenAsync();
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        // The copy holds another original of the file the author changed, so its package patches the file otherwise.
        File.WriteAllText(Path.Combine(gog.Game.InstallDirectory, "data", "strings.bin"), "strings of the original game, as the other store ships them");
        ui.Agent.Answer = request => request switch
        {
            GetDetectedGames => new DetectedGames([.. _work.Detected, gog]),
            TestInstallPackage => new JobStarted(InstallJob),
            _ => null,
        };
        var page = await PackageAsync(ui);
        page.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => page.HasOffer && !page.Description.HasMetadataProblem);
        page.OfferText.Should().Be(Text.Format(Strings.Creator_OfferVersion, ("game", "Lantern Isle"), ("store", "GOG")));
        await ui.RenderBothAsync("creator-single-offer");

        ui.Click(Button(ui, page.OfferAction));
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.Cells.All(cell => cell.IsCompared && cell.Review is not null) && page.CanBuild,
            () => string.Join(", ", page.Cells.Select(cell => cell.Brief.Label)) + " " + page.BlockedText);
        (page.HasOffer, page.BuildText).Should().Be((false, Strings.Creator_BuildAll));
        page.Session!.Project.Versions.Select(version => version.Game.Key).Should().Equal(CreatorWorkspace.Lantern, gog.Key);
        page.Cells[1].Folded.Should().Be(GameWording.Join([Text.Format(Strings.Creator_SharedFiles, ("count", 1), ("store", "Steam")),
            Text.Format(Strings.Creator_OwnPatches, ("count", 1))]), "the copy patches its own original and shares the new file");
        await ui.RenderBothAsync("creator-table");
        ui.Click(Button(ui, page.Cells[1].Name));
        (page.IsPanelOpen, page.Cell).Should().Be((true, page.Cells[1]));
        await ui.RenderBothAsync("creator-panel");
        ui.Press(Avalonia.Input.Key.Escape);
        page.IsPanelOpen.Should().BeFalse();

        // The table takes the panel's room back once the panel faded out.
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        await BuildAsync(ui);
        await BuiltAsync(page);
        page.Build.Packages.Select(row => (row.Name, row.Versions.Single())).Should().Equal(
            ("Lantern Isle (Steam) v1.0.examplemod", CreatorWorkspace.Lantern), ("Lantern Isle (GOG) v1.0.examplemod", gog.Key));
        page.Build.Packages.Should().OnlyContain(row => File.Exists(row.Path));
        await ui.RenderBothAsync("creator-built");
        ui.Click(Button(ui, Strings.Creator_TestInstall));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel);
        var choice = (ChoiceDialogViewModel)ui.Shell.Dialogs.Current!;
        choice.Choices.Select(tile => tile.Title).Should().Equal(Text.Format(Strings.Creator_TestOn, ("store", "Steam")), Text.Format(Strings.Creator_TestOn, ("store", "GOG")));
        choice.ChooseCommand.Execute(choice.Choices[1]);

        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.OfType<TestInstallPackage>().Any());
        var test = ui.Agent.Requests.OfType<TestInstallPackage>().Single();
        (test.Game, test.PackagePaths.Single()).Should().Be((gog.Key, Output("Lantern Isle (GOG) v1.0.examplemod")));
    }

    [AvaloniaFact]
    public async Task An_add_on_waits_for_its_base_and_builds_a_package_of_its_own()
    {
        var held = HeldComparison();
        using var ui = await OpenAsync(held);
        var page = await PackageAsync(ui);
        page.Description.Version = "1.0";
        var captions = _work.Folder("Captions", ("data/strings.bin", "strings of the original game, with captions"));
        held.Reads.Reset();
        page.Cells[0].CompareCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => page.Cells[0].FileLine.Length > 0);

        ui.Pickers.Folder = captions;
        ui.Click(Button(ui, Strings.Creator_AddFeature));
        await UiHarness.WaitUntilAsync(() => page.Cells.Count == 2);
        var addOn = page.Cells[1];
        (addOn.Key.Part, addOn.State, addOn.Brief.Label, addOn.Brief.Detail).Should().Be(("Captions", CellState.Waiting,
            Text.Format(Strings.Creator_CellState, ("state", "Waiting"), ("store", "Steam")), Text.Format(Strings.Creator_CellDetail, ("detail", "Waiting"))));
        await ui.RenderBothAsync("creator-table-waiting");
        page.Cells[0].CancelCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => page.Cells[0].State == CellState.Stopped);
        (addOn.State, addOn.Brief.Label, addOn.Brief.Detail, addOn.CanCompare).Should().Be((CellState.BaseMissing,
            Text.Format(Strings.Creator_CellState, ("state", "BaseMissing"), ("store", "Steam")), null, false), "an add-on compared without its base would be compared for nothing");
        await ui.RenderBothAsync("creator-table-base-missing");

        held.Reads.Set();
        page.Cells[0].CompareCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => page.Cells.All(cell => cell.IsCompared) && page.CanBuild, () => page.BlockedText);
        addOn.Review!.Plan.Operations.Should().ContainSingle().Which.Original!.FullPath.Should().Be(Path.Combine(_work.AuthorFolder, "data", "strings.bin"),
            "the add-on patches the file its base installs before it");
        await BuildAsync(ui);
        await BuiltAsync(page);
        page.Build.Packages.Select(row => (row.Name, row.Part)).Should().Equal(
            ("Lantern Isle v1.0.examplemod", ""), ("Lantern Isle v1.0 – Captions.examplemod", "Captions"));
        page.Build.Packages.Should().OnlyContain(row => File.Exists(row.Path));
    }

    [AvaloniaFact]
    public async Task Choosing_a_cell_stays_during_a_build_whose_bar_lists_the_run_s_packages()
    {
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        var original = Path.Combine(gog.Game.InstallDirectory, "data", "strings.bin");
        File.WriteAllText(original, "strings of the original game, as the other store ships them");
        File.WriteAllText(Path.Combine(gog.Game.InstallDirectory, "data", "config.xml"), "<config language=\"en\" store=\"other\" />");
        var files = new HeldFileSystem(original);
        files.Reads.Set();
        using var ui = await OpenAsync(files);
        var (page, _) = await TwoVersionsAsync(ui, gog);
        ui.Pickers.Folder = _work.Folder("Captions", ("data/config.xml", "<config language=\"en\" captions=\"on\" />"));
        ui.Click(Button(ui, Strings.Creator_AddFeature));
        await UiHarness.WaitUntilAsync(() => page.Cells.Count == 4 && page.CanBuild, () => page.BlockedText);

        files.Reads.Reset();
        await BuildAsync(ui);
        await UiHarness.WaitUntilAsync(() => page.Build.RunRows.Count == 4 && page.Build.RunRows[1].IsBuilding);

        page.Build.Heading.Should().Be(Text.Format(Strings.Creator_BuildingOf, ("number", 2), ("count", 4)));
        page.Build.RunRows.Select(row => (row.Name, row.Details)).Should().Equal(
            ("Lantern Isle (Steam) v1.0.examplemod", GameWording.Join([Strings.Creator_PartBase, "Steam"])),
            ("Lantern Isle (GOG) v1.0.examplemod", GameWording.Join([Strings.Creator_PartBase, "GOG"])),
            ("Lantern Isle (Steam) v1.0 – Captions.examplemod", GameWording.Join(["Captions", "Steam"])),
            ("Lantern Isle (GOG) v1.0 – Captions.examplemod", GameWording.Join(["Captions", "GOG"])));
        page.Build.RunRows.Select(row => row.State).Should().Equal(RunRowState.Finished, RunRowState.Building, RunRowState.Waiting, RunRowState.Waiting);
        page.Build.RunRows[0].Amount.Should().Be(Formats.FileSize(new FileInfo(Output("Lantern Isle (Steam) v1.0.examplemod")).Length));
        await ui.RenderBothAsync("creator-building-run");

        ui.Click(Button(ui, page.Cells[1].Name));
        (page.IsPanelOpen, page.Cell).Should().Be((true, page.Cells[1]), "choosing a cell stays during a build");
        ui.Find<Button>("AddPart").IsEffectivelyEnabled.Should().BeFalse("the project cannot change under its build");
        ui.Find<Button>().First(button => button.Name == "AddVersion" && button.IsEffectivelyVisible).IsEffectivelyEnabled.Should().BeFalse();
        ui.Find<TextBox>().First(box => box.Name == "PartName" && box.IsEffectivelyVisible).IsEffectivelyEnabled.Should().BeFalse();
        ui.Find<CellPanel>().Single(panel => panel.Parent is StackPanel { Parent: ScrollViewer { Parent: Border { Name: "Aside" } } }).IsEffectivelyEnabled.Should().BeFalse();
        await ui.RenderBothAsync("creator-building-panel");

        ui.Click(Button(ui, Strings.Creator_Stop));
        files.Reads.Set();
        await BuildEndedAsync(page);
        await UiHarness.WaitUntilAsync(() => page.CanBuild);
        var (bar, change) = (ui.Find<Border>("BuildBar"), ui.Find<Button>("ChangeOutput"));
        (change.TranslatePoint(new Point(change.Bounds.Width, 0), bar)!.Value.X).Should().BeLessThanOrEqualTo(bar.Bounds.Width, "the way to change the folder stays in the bar beside the panel");
    }

    [AvaloniaFact]
    public async Task Renaming_an_add_on_keeps_its_packages_in_the_record_and_on_the_card()
    {
        using var ui = await OpenAsync();
        var page = await PackageAsync(ui);
        page.Description.Version = "1.0";
        ui.Pickers.Folder = _work.Folder("Captions", ("data/captions.txt", "captions of the intro"));
        ui.Click(Button(ui, Strings.Creator_AddFeature));
        await UiHarness.WaitUntilAsync(() => page.Cells.Count == 2 && page.CanBuild, () => page.BlockedText);
        await BuildAsync(ui);
        await BuiltAsync(page);

        await page.RenamePartAsync("Captions", "Film captions");

        page.Record!.Parts.Select(part => part.Part).Should().Equal("", "Film captions");
        page = await ReopenAsync(ui, () => { });
        await UiHarness.WaitUntilAsync(() => page.Cells.All(cell => cell.IsCompared) && page.Build.Packages.Count == 2);
        page.Build.Packages.Select(row => row.Part).Should().Equal("", "Film captions");
    }

    [AvaloniaFact]
    public async Task A_test_installs_the_base_with_the_chosen_add_ons_in_order()
    {
        var project = TestGames.Offer("Lantern Isle", version: 1);
        _work.LanternProjects = [new SiteProjectView(project.ProjectId, project.GameSlug, project.Team.Slug)];
        using var ui = await OpenAsync();
        ui.Agent.Answer = request => request switch
        {
            GetProjectPage => TestPages.Page(project, """[{ "uuid": "b1111111-2222-4333-8444-555555555555", "key": "captions", "name": "Captions", "is_default": true }, { "uuid": "c1111111-2222-4333-8444-555555555555", "key": "voices", "name": "Voices" }]"""),
            TestInstallPackage => new JobStarted(InstallJob),
            _ => _work.Answer(request),
        };
        var page = await PackageAsync(ui);
        await UiHarness.WaitUntilAsync(() => page.Description.Hints is not null);
        ui.Pickers.Folder = _work.Folder("Captions files", ("data/captions/intro.srt", "captions of the intro"));
        ui.Click(Button(ui, Strings.Creator_AddFeature));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel);
        var choice = (ChoiceDialogViewModel)ui.Shell.Dialogs.Current!;
        choice.Choices.Select(tile => (tile.Title, tile.Description)).Should().Equal(
            ("Captions", Text.Format(Strings.Creator_FeatureRow, ("on", "true"))), ("Voices", Text.Format(Strings.Creator_FeatureRow, ("on", "false"))),
            (Strings.Creator_PickFolder, Strings.Creator_FeatureFromFolder));
        await ui.RenderBothAsync("creator-add-feature");
        choice.ChooseCommand.Execute(choice.Choices[0]);
        await UiHarness.WaitUntilAsync(() => page.Cells.Count == 2 && page.Cells[1].IsCompared);

        // An add-on the site names takes its folder later, on its cell.
        ui.Pickers.Folder = null;
        ui.Click(Button(ui, Strings.Creator_AddFeature));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel);
        choice = (ChoiceDialogViewModel)ui.Shell.Dialogs.Current!;
        choice.Choices.Select(tile => tile.Title).Should().Equal("Voices", Strings.Creator_PickFolder);
        choice.ChooseCommand.Execute(choice.Choices[0]);
        await UiHarness.WaitUntilAsync(() => page.Cells.Count == 3);
        page.Rows.Select(row => (row.Name, row.Caption)).Should().Equal(
            (Strings.Creator_PartBase, Text.Format(Strings.Creator_PartMode, ("mode", "Simple"))),
            ("Captions", Text.Format(Strings.Creator_FeatureRow, ("on", "true"))),
            ("Voices", Text.Format(Strings.Creator_FeatureRow, ("on", "false"))));
        ui.Click(Button(ui, page.Cells[2].Name));
        (page.Cells[2].State, page.Cells[2].DropText).Should().Be((CellState.NoFolder, GameWording.Join(
            [Text.Format(Strings.Creator_FeatureFrom, ("version", GameWording.ShortVersion(GameWording.Version("v1", 1)))), Strings.Creator_FeatureFolder])));
        await ui.RenderBothAsync("creator-panel-add-on");
        ui.Pickers.Folder = _work.Folder("Voices", ("data/voices/intro.ogg", "voices of the intro"));
        ui.Click(Button(ui, page.Cells[2].PickName));

        // The add-on keeps its comparison under its new name.
        var captions = page.Cells[1];
        var compared = page.Session!.ComparisonOf(captions.Key);
        var name = ui.Find<TextBox>().First(box => box.Name == "PartName" && box.Text == "Captions");
        name.Focus();
        name.Text = "voices";
        ui.Find<Button>("AddPart").Focus();
        UiHarness.Settle();
        page.Session.Project.Parts.Select(part => part.Name).Should().Equal(["", "Captions", "Voices"], "another part has the name, whatever its letter case");
        name = ui.Find<TextBox>().First(box => box.Name == "PartName" && box.Text == "Captions");
        name.Focus();
        name.Text = "Film captions";
        ui.Find<Button>("AddPart").Focus();
        (page.Cells[1], page.Session.ComparisonOf(page.Cells[1].Key), page.Rows[1].Caption).Should().Be(
            (captions, compared, Text.Format(Strings.Creator_FeatureRow, ("on", ""))));
        await UiHarness.WaitUntilAsync(() => page.Cells.All(cell => cell.IsCompared) && page.CanBuild, () => page.BlockedText);
        await ui.RenderBothAsync("creator-table-add-ons");
        await BuildAsync(ui);
        await BuiltAsync(page);

        page.Test.TestFeatures.Select(feature => feature.Part).Should().Equal("Film captions", "Voices");
        page.Test.TestTitle.Should().Be(Text.Format(Strings.Creator_TestTitle, ("several", "true")));
        await RenderAsync(ui, "creator-built-add-ons", 1500);
        ui.Click(ui.Find<ToggleButton>().First(pill => pill.IsEffectivelyVisible && Equals(pill.Content, "Film captions")));
        page.Test.TestTitle.Should().Be(Text.Format(Strings.Creator_TestTitle, ("several", "true")), "the base and one add-on are left");
        ui.Click(Button(ui, Strings.Creator_TestInstall));
        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.OfType<TestInstallPackage>().Any());
        ui.Agent.Requests.OfType<TestInstallPackage>().Single().PackagePaths.Should().Equal(
            Output("Lantern Isle v2.examplemod"), Output("Lantern Isle v2 – Voices.zip"));
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
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);

        await BuildAsync(ui);
        await BuiltAsync(package);

        package.Build.SiteWarnings.Should().HaveCount(2);
        package.Build.SiteWarnings[1].Should().Be(CreatorWording.Finding(new PlanFinding(FindingKind.FormatNotOnSite)));
        await RenderAsync(ui, "creator-built", 1500);
    }

    [AvaloniaFact]
    public async Task A_place_the_build_refuses_is_asked_for_again_rather_than_taken_by_the_next_build()
    {
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        var refused = Text.Format(Strings.Creator_BuildFailed, ("failure", nameof(BuildFailure.OutputInGameFolder)), ("files", 0), ("file", ""));
        ui.Pickers.Folder = _work.GameFolder;
        ui.Click(Button(ui, Strings.Creator_OutputTitle));
        await UiHarness.WaitUntilAsync(() => package.HasProblem);
        (package.ProblemText, package.OutputFolder).Should().Be((refused, Output("")), "a folder picked in the game is refused at once");

        // A project's folder may come to lie in the game's only later, e.g. once the store moved the game.
        package.Session!.Change(project => project with { OutputFolder = _work.GameFolder });
        await package.Session.SettledAsync();
        await BuildAsync(ui);
        await BuildEndedAsync(package);

        package.Build.ProblemText.Should().Be(refused);
        ui.Pickers.Folder = Output("");
        await BuildAsync(ui);
        await BuiltAsync(package);
        package.Build.OutputPath.Should().Be(Output("Lantern Isle v1.0.examplemod"));
    }

    [AvaloniaFact]
    public async Task A_place_the_build_cannot_write_to_is_asked_for_again_rather_than_taken_by_the_next_build()
    {
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        await File.WriteAllTextAsync(Output("Archive"), "a file where the package's folder would be", TestContext.Current.CancellationToken);
        package.Session!.Change(project => project with { OutputFolder = Output("Archive") });
        await package.Session.SettledAsync();

        await BuildAsync(ui);
        await BuildEndedAsync(package);

        package.Build.ProblemText.Should().Be(Strings.Creator_BuildWriteFailed);
        ui.Pickers.Folder = Output("");
        await BuildAsync(ui);
        await BuiltAsync(package);
        package.Build.OutputPath.Should().Be(Output("Lantern Isle v1.0.examplemod"));
    }

    [AvaloniaFact]
    public async Task A_build_on_an_output_drive_that_does_not_answer_ends_and_the_window_closes()
    {
        using var files = new HangingFileSystem(PhysicalFileSystem.Instance);
        using var ui = await OpenAsync(files);
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        files.Hang(Output(""));

        await BuildAsync(ui);
        await UiHarness.WaitUntilAsync(() => files.Blocked > 0);
        var closing = ui.Shell.Navigator.CanCloseAsync();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, Strings.Creator_StopBuildConfirm));
        (await closing).Should().BeTrue("a stop does not wait for the drive");
        package.Build.ProblemTitle.Should().Be(Strings.Creator_BuildCancelledTitle);

        await BuildAsync(ui);
        await files.RunOutOfTimeAsync(ui.Clock);
        await BuildEndedAsync(package);

        package.Build.ProblemText.Should().Be(Text.Format(Strings.Creator_BuildFailed, ("failure", nameof(BuildFailure.FolderUnavailable)),
            ("files", 1), ("file", CreatorWording.Wrappable(Output("")))), "the folder is named as the author chose it");
        (await ui.Shell.Navigator.CanCloseAsync()).Should().BeTrue();
        Directory.EnumerateFiles(Output("")).Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task A_file_of_another_origin_at_the_output_path_asks_first()
    {
        _work.Author("data/strings.bin", "strings of the original game");
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        Directory.CreateDirectory(Output(""));
        await File.WriteAllTextAsync(Output("Lantern Isle.zip"), "a file the author keeps", TestContext.Current.CancellationToken);

        await BuildAsync(ui);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        ((ConfirmDialogViewModel)ui.Shell.Dialogs.Current!).Title.Should().Be(Strings.Creator_ReplaceFilesTitle);
        await RenderAsync(ui, "creator-replace-files", null);
        ui.Shell.Dialogs.CancelCurrent();
        await BuildEndedAsync(package);
        package.Build.IsBuilt.Should().BeFalse();
        (await File.ReadAllTextAsync(Output("Lantern Isle.zip"), TestContext.Current.CancellationToken)).Should().Be("a file the author keeps");

        await BuildAsync(ui);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, Strings.Creator_ReplaceFilesConfirm));
        await BuiltAsync(package);

        await BuildAsync(ui);
        await BuiltAsync(package);
        ui.Shell.Dialogs.Current.Should().BeNull("a package this project built is its own to replace");
    }

    [AvaloniaFact]
    public async Task A_build_that_fails_unexpectedly_says_so_and_builds_where_it_was_going_once_the_cause_is_gone()
    {
        var files = new HeldFileSystem(_work.AuthorFolder);
        files.Reads.Set();
        using var ui = await OpenAsync(files);
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        var output = Output("Lantern Isle v1.0.examplemod");
        files.Failure = new PatchException(PatchError.EngineUnavailable, "the engine is missing");

        await BuildAsync(ui);
        await BuildEndedAsync(package);

        (package.Build.State, package.Build.ProblemTitle, package.Build.ProblemText).Should().Be((PackageState.Ready, Strings.Creator_BuildFailedTitle,
            Text.Format(Strings.Creator_Unexpected, ("error", nameof(PatchException)))));
        File.Exists(output).Should().BeFalse();
        files.Failure = null;
        await BuildAsync(ui);
        await BuiltAsync(package);
    }

    [AvaloniaFact]
    public async Task A_warning_of_the_build_about_a_file_left_out_since_no_longer_holds_the_build_back()
    {
        _work.Game("data/voices.pak", Noise(1, 1_200_000));
        _work.Author("data/voices.pak", Noise(2, 1_200_000));
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        await BuildAsync(ui);
        await BuildEndedAsync(package);
        package.Build.HasBuildFindings.Should().BeTrue();
        package.Build.ProblemText.Should().Be(Text.Format(Strings.Creator_BuildFailed, ("failure", nameof(BuildFailure.UnacknowledgedFindings)),
            ("files", 0), ("file", "")));

        var page = Page(ui);
        page.ShowOperationsCommand.Execute(null);
        var review = page.Review;
        review.ExcludeCommand.Execute(review.Operations.OfType<OperationRow>().Single(row => row.Entry?.File.Path == "data/voices.pak"));
        await UiHarness.WaitUntilAsync(() => page.Session!.Review!.Plan.Operations.All(operation => operation.Path != "data/voices.pak"));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        (package.Build.HasBuildFindings, package.Build.HasProblem).Should().Be((false, false), "the patch the build warned about is no longer made");
        await BuildAsync(ui);
        await BuiltAsync(package);
        page.Session!.Project.Acknowledged.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task An_accepted_large_patch_can_be_taken_back_after_a_rebuild_and_a_reopen()
    {
        _work.Game("data/voices.pak", Noise(1, 1_200_000));
        _work.Author("data/voices.pak", Noise(2, 1_200_000));
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);

        await BuildAsync(ui);
        await BuildEndedAsync(package);

        package.Build.HasBuildFindings.Should().BeTrue();
        package.Build.ProblemText.Should().Be(Text.Format(Strings.Creator_BuildFailed, ("failure", nameof(BuildFailure.UnacknowledgedFindings)),
            ("files", 0), ("file", "")));
        File.Exists(Output("Lantern Isle v1.0.examplemod")).Should().BeFalse();
        Page(ui).CanBuild.Should().BeFalse("the way on goes through the patches the build found");
        await RenderAsync(ui, "creator-build-findings", 1500);
        ui.Click(Button(ui, Strings.Creator_Accept));

        await UiHarness.WaitUntilAsync(() => Shows(ui, Strings.Creator_Rebuild));
        Page(ui).Session!.Project.Acknowledged.Should().ContainSingle();
        Shows(ui, Strings.Creator_Accept).Should().BeFalse();
        ui.Find<TextBlock>().Should().Contain(text => text.IsEffectivelyVisible && text.Text == Strings.Creator_Accepted);
        await RenderAsync(ui, "creator-build-findings-accepted", 1500);
        ui.Click(Button(ui, Strings.Creator_UndoAccept));
        await UiHarness.WaitUntilAsync(() => Shows(ui, Strings.Creator_Accept) && !Shows(ui, Strings.Creator_Rebuild));
        ui.Click(Button(ui, Strings.Creator_Accept));
        await UiHarness.WaitUntilAsync(() => Shows(ui, Strings.Creator_Rebuild));
        ui.Click(Button(ui, Strings.Creator_Rebuild));
        await BuiltAsync(package);
        File.Exists(Output("Lantern Isle v1.0.examplemod")).Should().BeTrue();

        // Once the build that found it is over, only its key remembers the patch, which the card lists with the way back.
        package.Review.AcceptedRules.Should().ContainSingle().Which.Title.Should().Be(
            Text.Format(Strings.Creator_RuleTitle, ("kind", "LargePatchAccepted"), ("path", "data/voices.pak")));
        await ui.RenderBothAsync("creator-panel-accepted",
            () => ui.Find<ItemsControl>().First(rows => rows.Name == "AcceptedRows" && rows.IsEffectivelyVisible).BringIntoView());
        var reopened = await ReopenAsync(ui, () => { });
        await UiHarness.WaitUntilAsync(() => reopened.Review.AcceptedRules.Count == 1 && reopened.Cell!.IsCompared);
        ui.Click(Button(ui, Strings.Creator_UndoAccept));
        await UiHarness.WaitUntilAsync(() => reopened.Review.AcceptedRules.Count == 0);
        reopened.Session!.Project.Acknowledged.Should().BeEmpty();
        await BuildAsync(ui);
        await BuildEndedAsync(reopened);
        reopened.Build.HasBuildFindings.Should().BeTrue("the patch asks again");
    }

    [AvaloniaFact]
    public async Task A_build_that_fails_after_a_good_one_shows_the_good_one_as_an_earlier_package()
    {
        var held = HeldComparison();
        using var ui = await OpenAsync(held);
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        await BuildAsync(ui);
        await BuiltAsync(package);
        _work.Author("data/extra/credits.txt", "credits of the author's work, with one more name");
        held.Reads.Reset();

        await BuildAsync(ui);
        await BuildEndedAsync(package);

        ui.Find<TextBlock>().Should().NotContain(text => text.IsEffectivelyVisible && text.Text == package.Build.BuiltOverline,
            "the package is not ready while its build failed");
        package.Build.ResultName.Should().Be("Lantern Isle v1.0.examplemod");
        package.Build.EarlierText.Should().StartWith(Strings.Creator_EarlierPackage.ToUpper(Text.Culture));
        ui.Find<TextBlock>().Should().Contain(text => text.IsEffectivelyVisible && text.Text == package.Build.EarlierText);
        Shows(ui, Strings.Creator_TestInstall).Should().BeFalse("only a package the last build made is tried in the game");
        await UiHarness.WaitUntilAsync(() => package.Cell!.IsComparing);
        Page(ui).CanBuild.Should().BeFalse("the files are compared again first");
        await RenderAsync(ui, "creator-rebuild-failed", 1700);
        held.Reads.Set();
    }

    [AvaloniaFact]
    public async Task A_package_moved_away_since_the_build_offers_no_test()
    {
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        await BuildAsync(ui);
        await BuiltAsync(package);

        var reopened = await ReopenAsync(ui, () => File.Move(Output("Lantern Isle v1.0.examplemod"), Output("Kept elsewhere.examplemod")));
        await UiHarness.WaitUntilAsync(() => reopened.Build.IsBuilt, () => reopened.Cell!.Line.Label);
        (reopened.Build.Packages.Single().IsMissing, reopened.Build.ResultSize).Should().Be((true, Strings.Creator_PackageMissing));
        Shows(ui, Strings.Creator_TestInstall).Should().BeFalse("only a package that is on the disk is tried in the game");
        Shows(ui, Strings.Creator_Reveal).Should().BeFalse();
        await RenderAsync(ui, "creator-reopened-missing", null);

        ui.Push(State(null, installed: true));
        await UiHarness.WaitUntilAsync(() => Shows(ui, Strings.Creator_UndoTest), () => reopened.Test.State.ToString());
        reopened.Test.TestTitle.Should().Be(Text.Format(Strings.Creator_TestInstalledTitle, ("game", "Lantern Isle"), ("several", "false")),
            "a test package in the game keeps its undo on the page, whatever became of the package");
    }

    [AvaloniaFact]
    public async Task A_version_that_comes_back_compares_again_the_versions_that_share_its_folder()
    {
        const string config = "<config language=\"en\" edition=\"store\" />";
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        _work.Game("data/config.xml", config);
        _work.Author("data/config.xml", config);
        using var ui = await OpenAsync();
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : null;
        var page = await PackageAsync(ui);
        await UiHarness.WaitUntilAsync(() => page.HasOffer);
        ui.Click(Button(ui, page.OfferAction));
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.Cells.All(cell => cell.IsCompared && cell.Review is not null));
        Patches(page.Cells[1]).Should().BeFalse("the file is the first version's own, so it is no work of the author's");
        var install = LanternWithTestInstall();
        ui.Push(TestStates.Idle(creatorMode: true) with { Version = ++_version, Games = [install with { Installs = [install.Installs[0] with { IsTest = false }] }] });

        page = await ReopenAsync(ui, () => { });
        await UiHarness.WaitUntilAsync(() => page.Cells.Count == 2 && page.Cells[0].State == CellState.Installed && page.Cells[1].IsCompared && page.Cells[1].Review is not null);
        Patches(page.Cells[1]).Should().BeTrue("while the first version holds a package its files cannot be told from the author's");

        ui.Push(TestStates.Idle(creatorMode: true) with { Version = ++_version });
        await UiHarness.WaitUntilAsync(() => page.Cells[0].IsCompared && page.Cells[1] is { IsCompared: true, Review: not null } && !Patches(page.Cells[1]));

        static bool Patches(CellViewModel cell) => cell.Review!.Plan.Operations.Any(operation => operation.Path == "data/config.xml");
    }

    [AvaloniaFact]
    public async Task A_changed_project_shows_the_table_without_the_old_packages()
    {
        using var ui = await OpenAsync();
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        File.WriteAllText(Path.Combine(gog.Game.InstallDirectory, "data", "strings.bin"), "strings of the original game, as the other store ships them");
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : _work.Answer(request);
        var page = await PackageAsync(ui);
        page.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => page.HasOffer && !page.Description.HasMetadataProblem);
        ui.Click(Button(ui, page.OfferAction));
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.CanBuild, () => page.BlockedText);
        await BuildAsync(ui);
        await BuiltAsync(page);

        var reopened = await ReopenAsync(ui, () => { });
        await UiHarness.WaitUntilAsync(() => reopened.IsTable && reopened.Build.IsBuilt, () => reopened.BlockedText);
        reopened.Build.Packages.Select(row => (row.Name, row.IsMissing)).Should().Equal(
            ("Lantern Isle (Steam) v1.0.examplemod", false), ("Lantern Isle (GOG) v1.0.examplemod", false));
        Shows(ui, Strings.Creator_TestInstall).Should().BeTrue("the last build's packages are tried as a run's are");
        await ui.RenderBothAsync("creator-reopened");
        reopened.Description.Version = "1.1";
        await UiHarness.WaitUntilAsync(() => !reopened.Build.ShowsPackage);
        reopened.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => reopened.Build.IsBuilt);

        reopened = await ReopenAsync(ui, () => _work.Author("data/extra/credits.txt", "credits of the author's work, with one more name"));
        await UiHarness.WaitUntilAsync(() => reopened.Cells.Count == 2 && reopened.Cells.All(cell => cell.IsCompared && cell.HasChanges));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        (reopened.IsTable, reopened.Build.ShowsPackage, reopened.Build.Packages.Count).Should().Be((true, false, 0),
            "the packages of 1.0 no longer hold the files as they are");
    }

    [AvaloniaFact]
    public async Task A_file_changed_since_the_comparison_fails_the_build_and_compares_again_by_itself()
    {
        var held = HeldComparison();
        using var ui = await OpenAsync(held);
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);

        foreach (var (change, file) in new (Action Change, string File)[]
        {
            (() => _work.Author("data/extra/credits.txt", "credits of the author's work, with one more name"), "data/extra/credits.txt"),
            (() => _work.Game("data/strings.bin", "strings of the original game, as its store updated them"), "data/strings.bin"),
        })
        {
            change();
            held.Reads.Reset();
            await BuildAsync(ui);
            await BuildEndedAsync(package);

            package.Build.ProblemText.Should().Be(Text.Format(Strings.Creator_BuildFailed, ("failure", nameof(BuildFailure.FilesChanged)),
                ("files", 1), ("file", file)), "a file is named by its path in the folder or the game, not above it");
            await UiHarness.WaitUntilAsync(() => package.Cell!.IsComparing);
            held.Reads.Set();
            await UiHarness.WaitUntilAsync(() => package.Cell!.IsCompared && !package.Build.HasProblem);
        }

        await BuildAsync(ui);
        await BuiltAsync(package);
    }

    [AvaloniaFact]
    public async Task A_file_rewritten_with_the_same_bytes_counts_as_no_change()
    {
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        package.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        await BuildAsync(ui);
        await BuiltAsync(package);
        (string, object?)[] since = [("hasVersion", "true"), ("version", GameWording.ShortVersion("1.0"))];
        var unchanged = Text.Format(Strings.Creator_CellState, [("state", "Unchanged"), ("work", ""), .. since]);
        package.Cell!.Detail.Should().Be(unchanged);

        var credits = Path.Combine(_work.AuthorFolder, "data", "extra", "credits.txt");
        File.WriteAllText(credits, File.ReadAllText(credits));
        File.SetLastWriteTimeUtc(credits, DateTime.UtcNow.AddMinutes(1));
        var compared = package.Session!.Comparison;
        ui.Click(Button(ui, Strings.Creator_Projects));
        var creator = (CreatorPageViewModel)ui.Shell.Navigator.Current!;
        await UiHarness.WaitUntilAsync(() => creator.Cards.Single().Line?.Label == Card("Changed", [("count", 1), .. since]),
            () => creator.Cards.Single().Line?.Label ?? "");
        creator.OpenCardCommand.Execute(creator.Cards.Single());

        await UiHarness.WaitUntilAsync(() => package.Session.Comparison != compared && package.Cell!.Detail == unchanged, () => package.Cell!.Line.Label);
        package.Cell!.HasChanges.Should().BeFalse("the file holds the bytes the package was built from");
        using (var library = new ProjectLibrary(ui.CreatorDirectory, TimeProvider.System))
        {
            (await library.ReadBuildAsync(package.ProjectId, CancellationToken.None))!.Parts[0].Folders[0].Files["data/extra/credits.txt"].LastWriteTime
                .Should().Be(File.GetLastWriteTimeUtc(credits), "the card tells it next time without reading the file");
        }

        ui.Click(Button(ui, Strings.Creator_Projects));
        await UiHarness.WaitUntilAsync(() => creator.Cards.Single().Line?.Label == Card("Unchanged", since));
        _work.Author("data/strings.bin", "strings changed by the author, a little longer, and once more");
        creator.OpenCardCommand.Execute(creator.Cards.Single());

        await UiHarness.WaitUntilAsync(() => package.Cell!.HasChanges);
        package.Cell!.ChangedText.Should().Be(Text.Format(Strings.Creator_ChangedSince, [("count", 1), .. since]));
        package.Cell!.Changes.Select(row => (row.KindText, row.Folder + row.Name)).Should().Equal((Strings.Creator_KindPatch, "data/strings.bin"));
        await RenderAsync(ui, "creator-single-changes", null);
        (ui.Window.Width, ui.Window.Height) = (960, 600);
        await RenderAsync(ui, "creator-single-changes-960", null);

        static string Card(string state, (string, object?)[] values) => Text.Format(Strings.Creator_CardState, [("state", state), ("path", ""), .. values]);
    }

    [AvaloniaFact]
    public async Task Coming_back_during_a_build_compares_only_after_it_ended()
    {
        var (ui, package, files) = await BuildingAsync();
        using var shown = ui;
        var compared = package.Session!.Comparison;
        _work.Author("data/extra/notes.txt", "notes the author added during the build");

        ui.Window.Activate();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        (package.Cell!.IsComparing, package.Session.Comparison).Should().Be((false, compared), "the build reads the files a comparison would plan from");

        files.Reads.Set();
        await BuiltAsync(package);
        await UiHarness.WaitUntilAsync(() => package.Session.Comparison != compared && package.Cell!.HasChanges);
        package.Cell!.Changes.Select(row => row.Folder + row.Name).Should().Equal("data/extra/notes.txt");
        package.Cell!.ChangedText.Should().Be(Text.Format(Strings.Creator_ChangedSince, ("count", 1), ("hasVersion", "true"),
            ("version", GameWording.ShortVersion("0.9"))));
    }

    [AvaloniaFact]
    public async Task A_test_install_runs_through_the_agent_keeps_the_package_as_built_and_is_undone_through_it()
    {
        _work.Author("data/strings.bin", "strings of the original game");
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        var output = Output("Lantern Isle.zip");
        await BuildAsync(ui);
        await BuiltAsync(package);
        ui.Agent.Answer = request => request switch
        {
            TestInstallPackage => new JobStarted(InstallJob),
            UninstallProject => new JobStarted(UndoJob),
            _ => _work.Answer(request),
        };
        ui.Pickers.Folder = null;
        ui.Click(Button(ui, Strings.Creator_OutputTitle));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        (package.Build.ResultName, package.OutputFolder).Should().Be(("Lantern Isle.zip", Output("")), "a place the author did not pick changes nothing");

        ui.Click(Button(ui, Strings.Creator_TestInstall));
        ui.Agent.Requests.OfType<TestInstallPackage>().Should().ContainSingle().Which.PackagePaths.Should().Equal(output);
        ui.Push(State(Job(InstallJob, JobKind.TestInstall, JobStage.Installing), installed: false));
        package.Test.State.Should().Be(TestInstallState.Installing);
        ui.Push(State(null, installed: true));
        package.Test.State.Should().Be(TestInstallState.Installed);
        package.Test.CanRebuild.Should().BeFalse("the creator would compare the files with those the package changed");
        package.CanBuild.Should().BeFalse("the test package goes first");
        await RenderAsync(ui, "creator-test-installed", 1400);

        ui.Click(Button(ui, Strings.Creator_Launch));
        ui.Agent.Requests.OfType<LaunchGame>().Should().Equal(new LaunchGame(CreatorWorkspace.Lantern));
        ui.Click(Button(ui, Strings.Creator_UndoTest));
        ui.Agent.Requests.OfType<UninstallProject>().Should().Equal(new UninstallProject(TestInstallId));
        ui.Push(State(Job(UndoJob, JobKind.Uninstall, JobStage.Uninstalling), installed: true));
        package.Test.State.Should().Be(TestInstallState.Undoing);
        ui.Push(State(null, installed: false));

        package.Test.State.Should().Be(TestInstallState.Undone);
        package.Test.CanRebuild.Should().BeTrue();
        ui.Shell.Toasts.Items.Should().ContainSingle().Which.Title.Should().Be(Strings.Creator_TestUndoneTitle);
        await RenderAsync(ui, "creator-tested", 1400);
        ui.Click(Button(ui, Strings.Creator_OpenDashboard));
        ui.Links.Opened.Should().Equal(new Uri("https://example.com/dashboard"));
    }

    [AvaloniaFact]
    public async Task An_add_on_is_tried_together_with_the_package_it_goes_with_which_installs_first()
    {
        _work.Author("data/strings.bin", "strings of the original game");
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        var output = Output("Lantern Isle.zip");
        await BuildAsync(ui);
        await BuiltAsync(package);
        ui.Agent.Answer = request => request is TestInstallPackage ? new JobStarted(InstallJob) : _work.Answer(request);
        var main = Output("Lantern Isle main.zip");
        ui.Pickers.File = main;
        await RenderAsync(ui, "creator-test-together", 1400);
        var (width, height) = (ui.Window.Width, ui.Window.Height);
        (ui.Window.Width, ui.Window.Height) = (960, 600);
        UiHarness.Settle();
        ui.Find<Button>("TestTogether").BringIntoView();
        await RenderAsync(ui, "creator-test-together-960", null);
        (ui.Window.Width, ui.Window.Height) = (width, height);
        UiHarness.Settle();

        Strings.Creator_TestTogether.Should().Be("Razem z paczką spolszczenia…");
        ui.Click(Button(ui, Strings.Creator_TestTogether));
        await UiHarness.WaitUntilAsync(() => ui.Agent.Requests.OfType<TestInstallPackage>().Any());

        ui.Pickers.Asked.Should().Contain(Strings.Creator_TestTogetherTitle);
        ui.Agent.Requests.OfType<TestInstallPackage>().Single().PackagePaths.Should().Equal(main, output);
        package.Test.State.Should().Be(TestInstallState.Installing);
    }

    [AvaloniaFact]
    public async Task A_test_install_that_failed_between_two_states_on_a_game_outside_the_player_s_list_says_why()
    {
        var package = await TestedPackageAsync();
        var running = new JobFailureView(JobFailureReason.GameRunning) { Processes = ["lantern.exe"] };

        _ui!.Click(Button(_ui, Strings.Creator_TestInstall));
        package.Test.State.Should().Be(TestInstallState.Installing);
        _ui.Agent.End(new JobEnded(Job(InstallJob, JobKind.TestInstall, JobStage.Checking), JobOutcome.Failed, running));
        _ui.Push(State(null, installed: false));

        package.Test.State.Should().Be(TestInstallState.Failed);
        package.Test.TestText.Should().Be(GameWording.FailureReason(running, StoreId.Steam, "Lantern Isle")).And.Contain("lantern.exe");
        package.Test.HasTestProblem.Should().BeTrue();
        package.Test.OffersTestInstall.Should().BeTrue();
        await RenderAsync(_ui, "creator-test-failed", 1400);
    }

    [AvaloniaFact]
    public async Task Undoing_a_test_install_says_why_it_failed_and_that_it_is_undone_once_the_install_is_gone()
    {
        var package = await TestedPackageAsync();
        var locked = new JobFailureView(JobFailureReason.FilesLocked) { Files = ["data/strings.bin"], Processes = ["editor.exe"] };
        _ui!.Click(Button(_ui, Strings.Creator_TestInstall));
        _ui.Agent.End(new JobEnded(Job(InstallJob, JobKind.TestInstall, JobStage.Installing), JobOutcome.Succeeded));
        package.Test.State.Should().Be(TestInstallState.Installing, "the install shows once the state lists it");
        _ui.Push(State(null, installed: true));
        package.Test.State.Should().Be(TestInstallState.Installed);

        _ui.Click(Button(_ui, Strings.Creator_UndoTest));
        _ui.Agent.End(new JobEnded(Job(UndoJob, JobKind.Uninstall, JobStage.Uninstalling), JobOutcome.Failed, locked));
        package.Test.State.Should().Be(TestInstallState.Installed);
        package.Test.TestText.Should().Be(Text.Format(Strings.Creator_UndoTestFailed,
            ("reason", GameWording.FailureReason(locked, StoreId.Steam, "Lantern Isle"))));
        package.Test.HasTestProblem.Should().BeTrue();
        _ui.Click(Button(_ui, Strings.Creator_UndoTest));
        _ui.Agent.End(new JobEnded(Job(UndoJob, JobKind.Uninstall, JobStage.Uninstalling), JobOutcome.Succeeded));
        package.Test.State.Should().Be(TestInstallState.Undoing, "the state still lists the install");
        _ui.Push(State(null, installed: false));

        package.Test.State.Should().Be(TestInstallState.Undone);
        package.Test.HasTestProblem.Should().BeFalse();
        _ui.Shell.Toasts.Items.Should().ContainSingle().Which.Title.Should().Be(Strings.Creator_TestUndoneTitle);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Undoing_a_test_that_left_files_says_so_whether_its_end_or_the_state_comes_first(bool endFirst)
    {
        var package = await TestedPackageAsync();
        _ui!.Click(Button(_ui, Strings.Creator_TestInstall));
        _ui.Push(State(null, installed: true));
        _ui.Click(Button(_ui, Strings.Creator_UndoTest));
        var end = new JobEnded(Job(UndoJob, JobKind.Uninstall, JobStage.Uninstalling), JobOutcome.Succeeded)
        {
            KeptFiles = [new KeptFilesView(KeptReason.GameUpdatedSinceInstall, 1), new KeptFilesView(KeptReason.BackupMissing, 2)],
        };
        if (endFirst)
        {
            _ui.Agent.End(end);
        }

        _ui.Push(State(null, installed: false));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        if (!endFirst)
        {
            _ui.Agent.End(end);
            await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        }

        package.Test.State.Should().Be(TestInstallState.Undone);
        var toast = _ui.Shell.Toasts.Items.Should().ContainSingle("an end that comes after the state replaces what the state alone told").Which;
        toast.IsWarning.Should().BeTrue();
        toast.Message.Should().Be("Lantern Isle ma z powrotem oryginalne pliki poza plikami zmienionymi przez oprogramowanie z paczki: gra "
            + "dostała aktualizację w trakcie testu, więc zostały bez zmian, a kopie ich oryginałów zachowano. Oryginalne pliki przywróci "
            + "sprawdzenie spójności plików gry w Steam.");
        if (endFirst)
        {
            await RenderAsync(_ui, "creator-undone-kept", 800);
        }
    }

    [AvaloniaFact]
    public async Task An_undone_test_reads_again_which_game_files_it_left_as_the_software_changed_them()
    {
        await TestedPackageAsync();
        _ui!.Click(Button(_ui, Strings.Creator_TestInstall));
        _ui.Push(State(null, installed: true));
        _ui.Click(Button(_ui, Strings.Creator_UndoTest));
        var asked = _ui.Agent.Requests.OfType<GetLeftoverBackups>().Count();

        _ui.Agent.End(new JobEnded(Job(UndoJob, JobKind.Uninstall, JobStage.Uninstalling), JobOutcome.Succeeded)
        {
            KeptFiles = [new KeptFilesView(KeptReason.OriginalUnknown, 1)],
        });

        _ui.Agent.Requests.OfType<GetLeftoverBackups>().Should().HaveCount(asked + 1, "the plan must know the files the undo left");
    }

    [AvaloniaFact]
    public async Task An_undone_test_whose_files_may_only_not_have_been_the_store_s_says_so_as_information()
    {
        var package = await TestedPackageAsync();
        _ui!.Click(Button(_ui, Strings.Creator_TestInstall));
        _ui.Push(State(null, installed: true));
        _ui.Click(Button(_ui, Strings.Creator_UndoTest));
        _ui.Agent.End(new JobEnded(Job(UndoJob, JobKind.Uninstall, JobStage.Uninstalling), JobOutcome.Succeeded)
        {
            KeptFiles = [new KeptFilesView(KeptReason.OriginalUnknown, 2)],
        });
        _ui.Push(State(null, installed: false));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));

        package.Test.State.Should().Be(TestInstallState.Undone);
        _ui.Shell.Toasts.Items.Should().ContainSingle().Which.Tone.Should().Be(ToastTone.Info,
            "the files are as they were before the test, only perhaps not the store's");
    }

    [AvaloniaFact]
    public async Task A_test_install_that_ended_while_the_agent_was_out_of_reach_is_told_by_the_next_state()
    {
        var package = await TestedPackageAsync();
        _ui!.Click(Button(_ui, Strings.Creator_TestInstall));
        _ui.Push(State(Job(InstallJob, JobKind.TestInstall, JobStage.Installing), installed: false));

        _ui.Agent.Link = AgentLink.Connecting;
        _ui.Agent.Link = AgentLink.Connected;
        package.Test.State.Should().Be(TestInstallState.Installing, "the state before the agent went away lists the job");
        _ui.Push(State(null, installed: false));

        package.Test.State.Should().Be(TestInstallState.Failed);
        package.Test.TestText.Should().Be(Strings.Creator_TestFailed);
    }

    [AvaloniaFact]
    public async Task The_package_step_names_every_control_for_screen_readers()
    {
        _work.Author("data/strings.bin", "strings of the original game");
        using var ui = await OpenAsync();
        var package = await PackageAsync(ui);
        ui.Unnamed().Should().BeEmpty();
        var missing = Text.Format(Strings.Creator_BeforeItemState, ("item", package.BeforeItems[0].Text), ("state", "Missing"));
        ui.Find<TextBlock>().Should().Contain(text => Avalonia.Automation.AutomationProperties.GetName(text) == missing, "only an icon tells that the version is missing");

        await BuildAsync(ui);
        await BuiltAsync(package);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Unnamed().Should().BeEmpty();
    }

    public void Dispose()
    {
        _ui?.Dispose();
        _work.Dispose();
    }

    private string Output(string name) => Path.TrimEndingDirectorySeparator(Path.Combine(_work.Root, "Packages", name));

    // A project over Lantern Isle's Steam copy and a GOG copy with another original of the file the author changed, ready to build.
    private async Task<(ProjectPageViewModel Page, DetectedGameEntry Gog)> TwoVersionsAsync(UiHarness ui, DetectedGameEntry? copy = null)
    {
        var gog = copy ?? _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        if (copy is null)
        {
            File.WriteAllText(Path.Combine(gog.Game.InstallDirectory, "data", "strings.bin"), "strings of the original game, as the other store ships them");
        }

        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : _work.Answer(request);
        var page = await PackageAsync(ui);
        page.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => page.HasOffer && !page.Description.HasMetadataProblem);
        ui.Click(Button(ui, page.OfferAction));
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.CanBuild, () => page.BlockedText);
        return (page, gog);
    }

    // A plain package built for the game, with the agent starting the test install and its undoing as the jobs above.
    private async Task<ProjectPageViewModel> TestedPackageAsync()
    {
        _work.Author("data/strings.bin", "strings of the original game");
        _ui = await OpenAsync();
        var package = await PackageAsync(_ui);
        await BuildAsync(_ui);
        await BuiltAsync(package);
        _ui.Agent.Answer = request => request switch
        {
            TestInstallPackage => new JobStarted(InstallJob),
            UninstallProject => new JobStarted(UndoJob),
            _ => _work.Answer(request),
        };
        return package;
    }

    private async Task<UiHarness> OpenAsync(IFileSystem? files = null, AppStateView? state = null, double width = 1280, double height = 800)
    {
        var ui = new UiHarness(state ?? TestStates.Idle(creatorMode: true), CreatorWorkspace.Compose(files), width, height);
        ui.Agent.Answer = _work.Answer;
        ui.Shell.Navigator.Navigate(new CreatorRoute());
        await UiHarness.WaitUntilAsync(() => ui.Find<CreatorPage>().Any(page => page.IsEffectivelyVisible));
        return ui;
    }

    private async Task<ProjectPageViewModel> PackageAsync(UiHarness ui, Func<ProjectPart, ProjectPart>? change = null)
    {
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared && page.Session!.Review is not null);
        // The packages go to a folder of the test's own, which the tests below look in.
        page.Session!.Change(project => project with { OutputFolder = Output("") });
        if (change is not null)
        {
            page.Session.ChangePart(change);
        }

        await page.Session.SettledAsync();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        return page;
    }

    // The build stands at a file the held file system keeps it from reading, until the test lets it go on.
    private async Task<(UiHarness Ui, ProjectPageViewModel Package, HeldFileSystem Files)> BuildingAsync()
    {
        _work.Author("data/pl/fonts.dat", "fonts of the translation");
        var files = new HeldFileSystem(Path.Combine(_work.AuthorFolder, "data", "pl", "fonts.dat"));
        files.Reads.Set();
        var ui = await OpenAsync(files);
        var package = await PackageAsync(ui);
        package.Description.Version = "0.9";
        await UiHarness.WaitUntilAsync(() => !package.Description.HasMetadataProblem);
        files.Reads.Reset();
        await BuildAsync(ui);
        await UiHarness.WaitUntilAsync(() => package.Build.FileLine.Length > 0);
        return (ui, package, files);
    }

    // The author's file of the game file's size is hashed, which the held file system keeps a comparison from finishing once the
    // test resets it.
    private HeldFileSystem HeldComparison()
    {
        var held = new HeldFileSystem(_work.Author("data/config.xml", "<config language=\"xx\" />"));
        held.Reads.Set();
        return held;
    }

    // A build is waited for until it ends, however it ends, so one that fails says why at once rather than once the wait for a
    // built package runs out, and one that never ends says where it stopped.
    private static Task BuildEndedAsync(ProjectPageViewModel package) =>
        UiHarness.WaitUntilAsync(() => !package.Build.BuildCommand.IsRunning, () => $"the build is at {package.Build.Stage} {package.Build.FileLine}");

    private static async Task BuiltAsync(ProjectPageViewModel package)
    {
        await BuildEndedAsync(package);
        (package.Build.State, package.Build.ProblemText).Should().Be((PackageState.Built, ""));
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

    private static async Task RenderAsync(UiHarness ui, string name, double? height)
    {
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render(name, height);
    }

    private static ProjectPageViewModel Page(UiHarness ui) => (ProjectPageViewModel)ui.Shell.Navigator.Current!;

    // Closes the project, lets the test change what lies on the disk meanwhile, and opens the project again from its card.
    private static async Task<ProjectPageViewModel> ReopenAsync(UiHarness ui, Action change)
    {
        ui.Click(Button(ui, Strings.Creator_Projects));
        var creator = (CreatorPageViewModel)ui.Shell.Navigator.Current!;
        (await creator.CloseProjectAsync()).Should().BeTrue();
        change();
        await UiHarness.WaitUntilAsync(() => creator.Cards.Any());
        creator.OpenCardCommand.Execute(creator.Cards.Single());
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { Cell: not null });
        return Page(ui);
    }

    // A plan is made again after every edit, and the bar lets the build go once it has the plan of the project as it is.
    private static async Task BuildAsync(UiHarness ui)
    {
        await UiHarness.WaitUntilAsync(() => Page(ui).CanBuild, () => Page(ui).BlockedText);
        ui.Click(Button(ui, Page(ui).BuildText));
    }

    private static Button Button(UiHarness ui, string name) =>
        ui.Find<Button>().First(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled
            && ControlAutomationPeer.CreatePeerForElement(button).GetName() == name);

    private static bool Shows(UiHarness ui, string name) =>
        ui.Find<Button>().Any(button => button.IsEffectivelyVisible && ControlAutomationPeer.CreatePeerForElement(button).GetName() == name);
}
