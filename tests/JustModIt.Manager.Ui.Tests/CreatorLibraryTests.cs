using System.Collections.Immutable;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Tests.TestSupport;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Creator;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class CreatorLibraryTests : IDisposable
{
    private static readonly DateTimeOffset BuiltAt = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly CreatorWorkspace _work = new();

    private string LibraryFolder => Path.Combine(_work.Root, "Library");

    public void Dispose() => _work.Dispose();

    [AvaloniaFact]
    public async Task The_library_lists_projects_and_their_states()
    {
        var sleeping = Directory.CreateDirectory(Path.Combine(_work.Root, "Work", "On a sleeping disk")).FullName;
        var gone = Path.Combine(_work.Root, "Work", "Gone");
        var ember = Project("Ember Isles", CreatorWorkspace.Lantern, _work.AuthorFolder) with
        {
            Site = new ProjectSite(Guid.NewGuid(), "ember-isles", "nimbus", "Nimbus", ""),
            Parts = [new ProjectPart { AuthorFolder = _work.AuthorFolder }, new ProjectPart { Name = "Voices" }],
        };
        CreatorProject[] projects =
        [
            ember,
            Project("Tidewater", CreatorWorkspace.Tidewater),
            Project("Ashfall", new GameKey(StoreId.Gog, "5399"), sleeping),
            Project("Lantern Isle", CreatorWorkspace.Ashfall, gone),
            Project("Saltmarsh", null),
        ];
        await SeedAsync(projects);
        using (var library = new ProjectLibrary(LibraryFolder, TimeProvider.System))
        {
            await library.UpdateBuildAsync(ember.Id, _ => new BuildRecord([new PartBuild("", "1.2", [],
                [new BuiltPackage(Path.Combine(_work.Root, "Ember Isles v1.2.zip"), PackageKind.Zip, 1024, [CreatorWorkspace.Lantern], "1.2", BuiltAt)])]), CancellationToken.None);
        }

        using var files = new HangingFileSystem(PhysicalFileSystem.Instance);
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), files);
        var creator = (CreatorPageViewModel)ui.Shell.Navigator.Current!;
        await UiHarness.WaitUntilAsync(() => creator.Cards.Count() == projects.Length && creator.Cards.All(shown => shown.Details.Length > 0)
            && Card(ui, "Lantern Isle")!.Line is not null);

        var card = (string name) => Card(ui, name)!;
        card("Ember Isles").Details.Should().Be(GameWording.Join(["Nimbus", Text.Format(Strings.Creator_LastPackage, ("hasVersion", "true"),
            ("version", GameWording.ShortVersion("1.2")), ("date", Formats.Date(BuiltAt, TimeZoneInfo.Utc)))]));
        card("Ember Isles").Stores.Should().Equal(new StoreChip("steam", Strings.Library_StoreSteam));
        card("Ember Isles").PartsText.Should().Be(Text.Format(Strings.Creator_CardParts, ("count", 1)));
        card("Tidewater").Details.Should().Be(Text.Format(Strings.Creator_NoPackageYet, ("date", Formats.Date(TestStates.Now.AddDays(-30), TimeZoneInfo.Utc))));
        card("Lantern Isle").Line!.Label.Should().Contain("Gone").And.NotContain(_work.Root, "a long path would fill the card");
        card("Lantern Isle").FolderTip.Should().Be(gone);
        card("Lantern Isle").AccessibleName.Should().Contain(gone, "screen readers get the whole path");
        ui.Find<StatusLine>().Single(line => line.DataContext == card("Lantern Isle")).Bounds.Height.Should().BeLessThanOrEqualTo(2 * 17, "a state takes two lines at most");
        card("Saltmarsh").Line!.Label.Should().Be(State("NoVersion"));
        card("Ashfall").Line.Should().BeNull("before the stores' first scan a missing game is not a game gone");
        ui.Agent.Requests.OfType<GetDetectedGames>().Should().BeEmpty();

        ui.Push(TestStates.Idle(creatorMode: true) with { Version = 2, Library = new LibraryView { ScannedAt = TestStates.Now, IsCached = true } });
        card("Ashfall").Line.Should().BeNull("a saved scan says nothing of today");

        ui.Push(TestStates.Idle(creatorMode: true) with
        {
            Version = 3,
            Library = new LibraryView { ScannedAt = TestStates.Now },
            Games = [new GameView
            {
                Key = CreatorWorkspace.Tidewater,
                Name = "Tidewater",
                Status = new GameStatusView(MyGameState.TestInstalled),
                Installs = [new InstallView { Id = Guid.NewGuid(), Status = new GameStatusView(MyGameState.TestInstalled), InstalledAt = TestStates.Now,
                    InstallDirectory = _work.GameFolder, BackupDirectory = _work.GameFolder, PackageFileName = "Tidewater.zip", IsTest = true }],
            }],
        });
        await UiHarness.WaitUntilAsync(() => card("Ashfall").Line is not null);
        card("Ashfall").Line!.Label.Should().Be(State("GameGone"));
        card("Tidewater").Line!.Label.Should().Be(State("TestInstalled"));
        ui.Unnamed().Should().BeEmpty();
        await ui.RenderBothAsync("creator-library");
        ui.Render("creator-library-whole", 1180);

        var first = ui.Find<Button>().First(button => button.Classes.Contains("gameCard"));
        first.Focus(NavigationMethod.Directional);
        ui.Press(Key.Right);
        ui.Window.FocusManager!.GetFocusedElement().Should().BeOfType<Button>().Which.DataContext.Should().NotBe(first.DataContext, "the arrows move between cards");

        files.Hang(sleeping);
        ui.Shell.Navigator.Navigate(new SettingsRoute());
        ui.Shell.Navigator.Navigate(new CreatorRoute());
        await UiHarness.WaitUntilAsync(() => files.Blocked > 0);
        ui.Clock.Advance(LibraryAccess.TimeLimit);
        await UiHarness.WaitUntilAsync(() => card("Ashfall").FolderTip == sleeping);
    }

    [AvaloniaFact]
    public async Task A_card_reads_only_the_folders_in_use_so_a_part_s_folder_every_version_replaced_counts_for_nothing()
    {
        var replaced = Directory.CreateDirectory(Path.Combine(_work.Root, "Work", "Replaced")).FullName;
        File.WriteAllText(Path.Combine(replaced, "strings.bin"), "a file no build read");
        var project = Project("Ember Isles", CreatorWorkspace.Lantern, replaced) with
        {
            Parts = [new ProjectPart { AuthorFolder = replaced, VersionFolders = new(ImmutableDictionary<GameKey, string>.Empty.Add(CreatorWorkspace.Lantern, _work.AuthorFolder)) }],
        };
        await SeedAsync(project);
        using (var library = new ProjectLibrary(LibraryFolder, TimeProvider.System))
        {
            await library.UpdateBuildAsync(project.Id, _ => Built(_work.AuthorFolder, ""), CancellationToken.None);
        }

        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        await UiHarness.WaitUntilAsync(() => Card(ui, "Ember Isles")?.Line is not null, () => Card(ui, "Ember Isles")?.Details ?? "");
        Card(ui, "Ember Isles")!.Line!.Label.Should().Be(State("Unchanged"));
    }

    [AvaloniaFact]
    public async Task A_card_tells_what_changed_without_reading_a_file()
    {
        var tidewaterFolder = Directory.CreateDirectory(Path.Combine(_work.Root, "Work", "Tidewater Files")).FullName;
        File.WriteAllText(Path.Combine(tidewaterFolder, "strings.bin"), "strings of the translation");
        var ember = Project("Ember Isles", CreatorWorkspace.Lantern, _work.AuthorFolder);
        var tidewater = Project("Tidewater", CreatorWorkspace.Tidewater, tidewaterFolder);
        await SeedAsync(ember, tidewater);
        using (var library = new ProjectLibrary(LibraryFolder, TimeProvider.System))
        {
            await library.UpdateBuildAsync(ember.Id, _ => Built(_work.AuthorFolder, "1.0"), CancellationToken.None);
            await library.UpdateBuildAsync(tidewater.Id, _ => Built(tidewaterFolder, ""), CancellationToken.None);
        }

        _work.Author("data/strings.bin", "strings changed by the author once more");
        _work.Author("data/new.txt", "a file the package did not have");
        // A file of the folders could only be read once the test lets it.
        using var files = new HangingFileSystem(new HeldFileSystem(_work.Root));
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), files);
        await UiHarness.WaitUntilAsync(() => Card(ui, "Ember Isles")?.Line is not null && Card(ui, "Tidewater")?.Line is not null);

        Card(ui, "Ember Isles")!.Line!.Label.Should().Be(State("Changed", count: 2, version: "1.0"));
        Card(ui, "Tidewater")!.Line!.Label.Should().Be(State("Unchanged"));

        // A project the card has not looked at yet says it looks, while its drive does not answer.
        var ashfallFolder = Directory.CreateDirectory(Path.Combine(_work.Root, "Work", "Ashfall Files")).FullName;
        var ashfall = Project("Ashfall", CreatorWorkspace.Ashfall, ashfallFolder);
        await SeedAsync(ashfall);
        using (var library = new ProjectLibrary(LibraryFolder, TimeProvider.System))
        {
            await library.UpdateBuildAsync(ashfall.Id, _ => Built(ashfallFolder, "2.0"), CancellationToken.None);
        }

        files.Hang(ashfallFolder);
        ui.Shell.Navigator.Navigate(new SettingsRoute());
        ui.Shell.Navigator.Navigate(new CreatorRoute());
        await UiHarness.WaitUntilAsync(() => Card(ui, "Ashfall")?.Line?.Label == State("Checking"));
        await ui.RenderBothAsync("creator-library-changes");
        await files.RunOutOfTimeAsync(ui.Clock);
        await UiHarness.WaitUntilAsync(() => Card(ui, "Ashfall")!.FolderTip == ashfallFolder);
    }

    [AvaloniaFact]
    public async Task A_drive_that_does_not_answer_holds_only_its_cell()
    {
        await SeedAsync(Project("Lantern Isle", CreatorWorkspace.Lantern, _work.AuthorFolder));
        using var files = new HangingFileSystem(PhysicalFileSystem.Instance);
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), files);
        await UiHarness.WaitUntilAsync(() => Card(ui, "Lantern Isle") is { Details.Length: > 0 });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        files.Hang(_work.AuthorFolder);

        ((CreatorPageViewModel)ui.Shell.Navigator.Current!).OpenCardCommand.Execute(Card(ui, "Lantern Isle"));
        await files.RunOutOfTimeAsync(ui.Clock);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { Cell.State: CellState.FolderGone });
        var page = (ProjectPageViewModel)ui.Shell.Navigator.Current!;

        page.Cell!.NoticeTitle.Should().Be(Text.Format(Strings.Creator_BlockTitle, ("reason", nameof(CreatorBlockReason.AuthorFolderUnavailable))),
            "a drive that does not answer does not prove the folder gone");
        page.Cell!.NoticeText.Should().Contain(CreatorWording.Wrappable(_work.AuthorFolder));
        await ui.RenderBothAsync("creator-folder-unanswered");
        page.Tab = ProjectTab.Description;
        page.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => page.BeforeItems.Any(item => item.IsMet && item.Text.Contains("1.0", StringComparison.Ordinal)));
        page.CanBuild.Should().BeFalse("the version's comparison waits for its drive");
    }

    [AvaloniaFact]
    public async Task A_comparison_under_way_shows_on_the_project_s_card()
    {
        // A file of the game's size is hashed, which the held file system keeps from finishing.
        _work.Author("data/strings.bin", "strings of the changed  game");
        var files = new HeldFileSystem(_work.Root);
        var project = Project("Lantern Isle", CreatorWorkspace.Lantern, _work.AuthorFolder);
        await SeedAsync(project);
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), files);
        await UiHarness.WaitUntilAsync(() => Card(ui, "Lantern Isle") is not null);
        var card = Card(ui, "Lantern Isle")!;

        ui.Click(ui.Find<Button>().First(button => button.DataContext == card && button.Classes.Contains("gameCard")));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { Work: ProjectWork.Comparing });
        ui.Click(Button(ui, Strings.Creator_Projects));

        await UiHarness.WaitUntilAsync(() => card.Line?.Label == State("Comparing"));
        files.Reads.Set();
        await UiHarness.WaitUntilAsync(() => card.Line is null);
    }

    [AvaloniaFact]
    public async Task Turning_creator_mode_off_leaves_the_project_page_too()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        ui.Shell.Navigator.Current.Should().BeOfType<ProjectPageViewModel>();

        ui.Push(TestStates.Idle(creatorMode: false));

        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute());
    }

    [AvaloniaFact]
    public async Task A_project_with_no_version_opens_with_the_way_to_one_and_stays_as_it_was_when_cancelled()
    {
        var project = Project("Saltmarsh", null, _work.AuthorFolder);
        await SeedAsync(project);
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        await UiHarness.WaitUntilAsync(() => Card(ui, "Saltmarsh") is not null);
        var creator = (CreatorPageViewModel)ui.Shell.Navigator.Current!;
        var card = ui.Find<Button>().First(button => button.DataContext == Card(ui, "Saltmarsh") && button.Classes.Contains("gameCard"));

        ui.Click(card);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { HasVersion: false });
        await ui.RenderBothAsync("creator-no-version");
        ui.Click(Button(ui, Strings.Creator_AddGameVersion));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is NewProjectDialogViewModel { IsListed: true });
        ((NewProjectDialogViewModel)ui.Shell.Dialogs.Current!).Folder.Should().Be(_work.AuthorFolder);
        ui.Press(Key.Escape);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null);
        ui.Shell.Navigator.Current.Should().BeOfType<ProjectPageViewModel>().Which.HasVersion.Should().BeFalse();

        ui.Click(Button(ui, Strings.Creator_AddGameVersion));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is NewProjectDialogViewModel { IsListed: true });
        var dialog = (NewProjectDialogViewModel)ui.Shell.Dialogs.Current!;
        dialog.PickCommand.Execute(dialog.Rows[0].Right);
        dialog.CreateCommand.Execute(null);

        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { Cell.IsCompared: true });
        creator.OpenPage.Should().NotBeNull();
        var opened = ((ProjectPageViewModel)ui.Shell.Navigator.Current!).Session!.Project;
        opened.Id.Should().Be(project.Id);
        opened.Versions.Single().Game.Key.Should().Be(CreatorWorkspace.Lantern);
        await ((ProjectPageViewModel)ui.Shell.Navigator.Current!).Session!.FlushAsync();
        using var library = new ProjectLibrary(LibraryFolder, TimeProvider.System);
        (await library.Store(project.Id).LoadAsync(CancellationToken.None))!.Versions.Should().Equal(opened.Versions);
    }

    [AvaloniaFact]
    public async Task A_project_whose_game_is_gone_says_so_and_goes_on_once_a_store_reports_it()
    {
        var gone = new GameKey(StoreId.Gog, "5399");
        await SeedAsync(Project("Lantern Isle", gone, _work.AuthorFolder));
        // Only a whole look of the stores tells that a game is gone.
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true) with { Library = new LibraryView { ScannedAt = TestStates.Now } });
        await UiHarness.WaitUntilAsync(() => Card(ui, "Lantern Isle") is not null);
        var creator = (CreatorPageViewModel)ui.Shell.Navigator.Current!;

        creator.OpenCardCommand.Execute(Card(ui, "Lantern Isle"));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { Cell.State: CellState.GameGone });
        var page = (ProjectPageViewModel)ui.Shell.Navigator.Current!;
        page.Cell!.NoticeTitle.Should().Be(Strings.Creator_GameGoneTitle);
        page.Session!.Comparison.Should().BeNull("a part has no game to compare with");
        page.BlockedText.Should().Contain(Text.Format(Strings.Creator_BlockedVersion, ("reason", "Gone"), ("store", "GOG")));
        await ui.RenderBothAsync("creator-game-gone");

        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([_work.Detected[1] with { Key = gone }]) : null;
        ui.Click(Button(ui, Strings.Creator_Projects));
        creator.OpenCardCommand.Execute(Card(ui, "Lantern Isle"));

        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared);
        page.Cell!.HasNotice.Should().BeFalse();
        page.Session!.Version!.GameFolder.Should().Be(_work.GameFolder, "the store says where the game is now");
    }

    [AvaloniaFact]
    public async Task Duplicate_opens_as_a_project_of_its_own()
    {
        var original = Project("Lantern Isle", CreatorWorkspace.Lantern, _work.AuthorFolder);
        await SeedAsync(original);
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        await UiHarness.WaitUntilAsync(() => Card(ui, "Lantern Isle") is not null);

        ui.Click(MoreButton(ui, Card(ui, "Lantern Isle")!));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel { IsClosing: false });
        await ui.RenderBothAsync("creator-card-tiles");
        await ChooseAsync(ui, Strings.Creator_Duplicate);

        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel);
        var copy = ((ProjectPageViewModel)ui.Shell.Navigator.Current!).Session!.Project;
        copy.Id.Should().NotBe(original.Id);
        copy.Name.Should().Be(Text.Format(Strings.Creator_CopyName, ("name", "Lantern Isle")));
        copy.Parts[0].AuthorFolder.Should().Be(_work.AuthorFolder, "a duplicate keeps the same folders");
        using var library = new ProjectLibrary(LibraryFolder, TimeProvider.System);
        (await library.Store(original.Id).LoadAsync(CancellationToken.None)).Should().Be(original);
        (await library.ReadBuildAsync(copy.Id, CancellationToken.None)).Should().BeNull();

        ui.Click(Button(ui, Strings.Creator_Projects));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is CreatorPageViewModel creator && creator.Cards.Count() == 2);
    }

    [AvaloniaFact]
    public async Task A_card_s_tiles_export_and_remove_a_project_and_a_refused_question_keeps_it()
    {
        var project = Project("Lantern Isle", CreatorWorkspace.Lantern, _work.AuthorFolder) with { OutputFolder = Path.Combine(_work.Root, "Packages") };
        await SeedAsync(project);
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        await UiHarness.WaitUntilAsync(() => Card(ui, "Lantern Isle") is not null);
        var creator = (CreatorPageViewModel)ui.Shell.Navigator.Current!;

        ui.Pickers.SaveFile = Path.Combine(_work.Root, "Lantern Isle.exampleproj");
        ui.Click(MoreButton(ui, Card(ui, "Lantern Isle")!));
        await ChooseAsync(ui, Strings.Creator_Export);
        await UiHarness.WaitUntilAsync(() => File.Exists(ui.Pickers.SaveFile));
        File.ReadAllText(ui.Pickers.SaveFile).Should().NotContain("Packages", "a file handed over never chooses where a build writes");

        ui.Click(MoreButton(ui, Card(ui, "Lantern Isle")!));
        await ChooseAsync(ui, Strings.Creator_Remove);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false, Title: var title } && title == Strings.Creator_RemoveTitle);
        ((ConfirmDialogViewModel)ui.Shell.Dialogs.Current!).Message.Should().Be(Text.Format(Strings.Creator_RemoveMessage, ("name", "Lantern Isle")));
        ui.Shell.Dialogs.CancelCurrent();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null);
        Directory.Exists(Path.Combine(LibraryFolder, project.Id.ToString())).Should().BeTrue();

        ui.Click(MoreButton(ui, Card(ui, "Lantern Isle")!));
        await ChooseAsync(ui, Strings.Creator_Remove);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await ui.RenderBothAsync("creator-remove");
        using (File.Open(Path.Combine(LibraryFolder, project.Id.ToString(), "project.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ui.Click(Button(ui, Strings.Creator_Remove));
            await UiHarness.WaitUntilAsync(() => creator.HasProblem);
        }

        creator.ProblemText.Should().Be(Strings.Creator_RemoveFailed, "another program holds the project's file");
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null);
        ui.Shell.Navigator.Navigate(new SettingsRoute());
        ui.Shell.Navigator.Navigate(new CreatorRoute());
        await UiHarness.WaitUntilAsync(() => Card(ui, "Lantern Isle") is not null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        ui.Click(MoreButton(ui, Card(ui, "Lantern Isle")!));
        await ChooseAsync(ui, Strings.Creator_Remove);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, Strings.Creator_Remove));

        await UiHarness.WaitUntilAsync(() => !creator.Cards.Any());
        Directory.Exists(Path.Combine(LibraryFolder, project.Id.ToString())).Should().BeFalse();
        Directory.Exists(_work.AuthorFolder).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_project_that_cannot_be_read_is_told_and_kept()
    {
        await SeedAsync(Project("Lantern Isle", CreatorWorkspace.Lantern, _work.AuthorFolder));
        var damaged = Path.Combine(LibraryFolder, Guid.NewGuid().ToString());
        Directory.CreateDirectory(damaged);
        File.WriteAllText(Path.Combine(damaged, "project.json"), "{ not a project");

        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var creator = (CreatorPageViewModel)ui.Shell.Navigator.Current!;
        await UiHarness.WaitUntilAsync(() => creator.HasUnreadable && creator.Cards.Any());

        creator.UnreadableText.Should().Be(Text.Format(Strings.Creator_UnreadableProjects, ("count", 1)));
        await ui.RenderBothAsync("creator-unreadable");
        ui.Click(Button(ui, Strings.Creator_Reveal));
        ui.Links.Folders.Should().Equal(damaged);
        File.ReadAllText(Path.Combine(damaged, "project.json")).Should().Be("{ not a project");
    }

    private static CreatorProject Project(string name, GameKey? game, string folder = "") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CreatedAt = TestStates.Now.AddDays(-30),
        Versions = game is { } key ? [new ProjectVersion(new ProjectGame(key, name))] : [],
        Parts = [new ProjectPart { AuthorFolder = folder }],
    };

    private async Task SeedAsync(params CreatorProject[] projects)
    {
        using var library = new ProjectLibrary(LibraryFolder, TimeProvider.System);
        foreach (var project in projects)
        {
            await library.Store(project.Id).SaveAsync(project, CancellationToken.None);
        }
    }

    private async Task<UiHarness> OpenAsync(AppStateView state, IFileSystem? files = null)
    {
        var ui = new UiHarness(state, CreatorWorkspace.Compose(files), context: context => context with { CreatorDirectory = LibraryFolder });
        ui.Agent.Answer = _work.Answer;
        ui.Shell.Navigator.Navigate(new CreatorRoute());
        await UiHarness.WaitUntilAsync(() => ui.Find<CreatorPage>().Any(page => page.IsEffectivelyVisible));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        return ui;
    }

    private static ProjectCard? Card(UiHarness ui, string name) =>
        (ui.Shell.Navigator.Current as CreatorPageViewModel)?.Cards.FirstOrDefault(card => card.Name == name);

    private static string State(string state, string path = "", int count = 0, string version = "") => Text.Format(Strings.Creator_CardState,
        ("state", state), ("path", path), ("count", count), ("hasVersion", version.Length > 0 ? "true" : "false"), ("version", GameWording.ShortVersion(version)));

    // A record of a build that read the folder as it is now.
    private BuildRecord Built(string folder, string version) => new([new PartBuild("", version,
        [new FolderBuild(folder, Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/'), file => new BuiltFile(new FileInfo(file).Length, File.GetLastWriteTimeUtc(file))))],
        [new BuiltPackage(Path.Combine(_work.Root, "Packages", "package.zip"), PackageKind.Zip, 1024, [], version, BuiltAt)])]);

    private static Button MoreButton(UiHarness ui, ProjectCard card) =>
        ui.Find<Button>().First(button => button.DataContext == card && button.Classes.Contains("cardMore"));

    private static Button Button(UiHarness ui, string name) =>
        ui.Find<Button>().First(button => button.IsEffectivelyVisible && ControlAutomationPeer.CreatePeerForElement(button).GetName() == name);

    // The dialog's scrim takes the clicks meant for the page until the dialog has faded out.
    private static async Task ChooseAsync(UiHarness ui, string tile)
    {
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, tile));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is not ChoiceDialogViewModel);
    }
}
