using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Ui.Tests;

public sealed class CreatorTests : IDisposable
{
    private readonly CreatorWorkspace _work = new();

    [AvaloniaFact]
    public async Task A_dropped_folder_and_a_chosen_game_make_a_project_that_compares_by_itself()
    {
        using var ui = await OpenAsync(Installed(TestStates.Idle(creatorMode: true)));
        await ui.RenderBothAsync("creator-library-empty");

        Drop(ui, DroppedItem.Of<IStorageFile>(Path.Combine(_work.AuthorFolder, "data", "strings.bin")));
        Library(ui).DroppedFiles.Should().BeTrue("a folder is what the creator compares");
        ui.Shell.Dialogs.Current.Should().BeNull();
        await ui.RenderBothAsync("creator-dropped-files");

        ui.Window.DragDrop(Middle(ui, ui.Find<Border>("DropZone")), RawDragEventType.DragEnter, Dragged(DroppedItem.Of<IStorageFolder>(_work.AuthorFolder)), DragDropEffects.Copy);
        UiHarness.Settle();
        ui.Find<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).Should().Contain(Strings.Creator_DropOverTitle);
        await RenderAsync(ui, "creator-library-drop-over");
        Drop(ui, DroppedItem.Of<IStorageFolder>(_work.AuthorFolder));

        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is NewProjectDialogViewModel { IsListed: true });
        var dialog = (NewProjectDialogViewModel)ui.Shell.Dialogs.Current!;
        dialog.Folder.Should().Be(_work.AuthorFolder);
        Library(ui).DroppedFiles.Should().BeFalse();
        var options = dialog.Rows.SelectMany(row => new[] { row.Left, row.Right }).OfType<GameOption>().ToList();
        options.Select(option => (option.Name, option.Block)).Should().Equal(
            ("Ashfall Chronicles", GameBlock.NotReady), ("Lantern Isle", GameBlock.None), ("Tidewater", GameBlock.Installed));
        options[2].BlockText.Should().Contain("Lantern Team").And.EndWith(GameWording.ShortVersion("1"));
        dialog.CreateCommand.CanExecute(null).Should().BeFalse("no game is chosen yet");
        ui.Unnamed().Should().BeEmpty();
        await ui.RenderBothAsync("creator-new");

        ui.Click(Choice(ui, "Lantern Isle"));
        await ui.RenderBothAsync("creator-new-other-game");
        ui.Click(Button(ui, Strings.Creator_Create));

        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { Cell.IsCompared: true });
        var page = Page(ui);
        page.Session!.Comparison!.Files.Select(file => (file.Path, file.Difference)).Should().Equal(
            ("data/extra/credits.txt", FileDifference.New), ("data/strings.bin", FileDifference.Changed));
        page.Session.Version.Should().Be(new ProjectVersion(new ProjectGame(CreatorWorkspace.Lantern, "Lantern Isle")) { GameFolder = _work.GameFolder });
        page.Session.Part.AuthorFolder.Should().Be(_work.AuthorFolder);
    }

    [AvaloniaFact]
    public async Task No_game_and_a_failed_list_are_states_of_their_own()
    {
        var state = TestStates.Idle(creatorMode: true);
        IReadOnlyList<DetectedGameEntry>? games = null;
        using var ui = await OpenAsync(state);
        ui.Agent.Answer = request => request is GetDetectedGames && games is not null ? new DetectedGames(games) : null;
        ui.Pickers.Folder = _work.AuthorFolder;

        ui.Click(Button(ui, Strings.Creator_New));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is NewProjectDialogViewModel { HasFailed: true });
        var dialog = (NewProjectDialogViewModel)ui.Shell.Dialogs.Current!;
        ui.Find<Skeleton>().Where(skeleton => skeleton.IsEffectivelyVisible).Should().BeEmpty("no skeleton stays in the place of a list that failed");
        await ui.RenderBothAsync("creator-new-failed");

        games = [];
        ui.Click(Button(ui, Strings.Library_Retry));
        await UiHarness.WaitUntilAsync(() => dialog.IsLoading);
        dialog.HasNoGames.Should().BeFalse("the stores have not looked yet");

        ui.Push(state with { Version = 2, Library = new LibraryView { ScannedAt = TestStates.Now } });
        await UiHarness.WaitUntilAsync(() => dialog.HasNoGames);
        dialog.CreateCommand.CanExecute(null).Should().BeFalse();
        await ui.RenderBothAsync("creator-new-no-games");
        ui.Unnamed().Should().BeEmpty();
        Button(ui, Strings.Creator_CheckNow).Should().NotBeNull();

        games = _work.Detected;
        ui.Push(state with { Version = 3, Library = new LibraryView { ScannedAt = TestStates.Now.AddMinutes(1) } });
        await UiHarness.WaitUntilAsync(() => dialog.IsListed);
        dialog.Query = "no such title";
        dialog.ShowsNoResults.Should().BeTrue("a search that finds nothing is not a computer without games");
        dialog.HasNoGames.Should().BeFalse();
        dialog.Query = "";

        ui.Click(Button(ui, Strings.Creator_MissingGameLink));
        ui.Shell.Secondary.Single(item => item.IsActive).Section.Should().Be(ShellSection.Creator, "the panel belongs to where it was opened from");
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null);
        ui.Shell.Navigator.CurrentRoute.Should().Be(new UndetectedGamesRoute(ShellSection.Creator));
        ui.Shell.Navigator.Pages.OfType<CreatorPageViewModel>().Single().OpenPage.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task A_copy_with_an_install_is_refused_with_the_way_to_it()
    {
        using var ui = await OpenAsync(Installed(TestStates.Idle(creatorMode: true)));
        ui.Pickers.Folder = _work.AuthorFolder;
        ui.Click(Button(ui, Strings.Creator_New));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is NewProjectDialogViewModel { IsListed: true });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        var dialog = (NewProjectDialogViewModel)ui.Shell.Dialogs.Current!;
        ui.Find<RadioButton>().Where(choice => choice.IsEffectivelyVisible).Should().ContainSingle("only the game that is ready can be chosen");

        ui.Click(ui.Find<Button>().First(button => button.DataContext is GameOption { IsBlockedByInstall: true } && button.IsEffectivelyVisible));

        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is null);
        (await dialog.Result).Should().BeNull();
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(CreatorWorkspace.Tidewater));
        ui.Shell.Navigator.Pages.OfType<CreatorPageViewModel>().Single().OpenPage.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task A_comparison_shows_its_stage_bytes_and_file_and_can_be_cancelled()
    {
        // A file of the game's size is hashed, which the held file system keeps from finishing.
        _work.Author("data/strings.bin", "strings of the changed  game");
        var files = new HeldFileSystem(_work.Root);
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), files);
        var step = (await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle")).Cell!;

        await UiHarness.WaitUntilAsync(() => step.FileLine.Length > 0);
        step.IsComparing.Should().BeTrue();
        step.Stage.Should().Contain(Text.Format(Strings.Creator_CompareStage, ("stage", nameof(ComparisonStage.Comparing))));
        await RenderAsync(ui, "creator-comparing");
        ui.Click(Button(ui, Strings.Dialog_Cancel));
        files.Reads.Set();

        await UiHarness.WaitUntilAsync(() => step.State is CellState.Stopped or CellState.Failed);
        step.ProblemTitle.Should().Be(Strings.Creator_CompareCancelledTitle);
        Page(ui).Session!.Comparison.Should().BeNull();
        Page(ui).CanBuild.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_comparison_that_fails_unexpectedly_says_so_and_leaves_the_creator_open()
    {
        _work.Author("data/strings.bin", "strings of the changed  game");
        var files = new HeldFileSystem(_work.Root) { Failure = new InvalidDataException("The disk returned nonsense.") };
        files.Reads.Set();
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), files);
        var step = (await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle")).Cell!;

        await UiHarness.WaitUntilAsync(() => step.State is CellState.Stopped or CellState.Failed);

        (step.ProblemTitle, step.ProblemText).Should().Be(
            (Strings.Creator_CompareStoppedTitle, Text.Format(Strings.Creator_Unexpected, ("error", nameof(InvalidDataException)))));
        files.Failure = null;
        step.CompareCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => Page(ui).Session!.Comparison is not null);
    }

    [AvaloniaFact]
    public async Task The_game_s_own_folder_is_refused_before_anything_is_read()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var creator = Library(ui);
        ui.Pickers.Folder = _work.GameFolder;

        ui.Click(Button(ui, Strings.Creator_New));

        await UiHarness.WaitUntilAsync(() => creator.HasRefusal);
        creator.RefusalTitle.Should().Be(Strings.Creator_GameFolderTitle);
        ui.Shell.Dialogs.Current.Should().BeNull();
        ui.Shell.Navigator.Current.Should().BeSameAs(creator);
        await ui.RenderBothAsync("creator-game-folder");

        ui.Pickers.Folder = Path.Combine(_work.Detected[2].Game.InstallDirectory, "data");
        ui.Click(Button(ui, Strings.Creator_PickFolder));
        await UiHarness.WaitUntilAsync(() => creator.RefusalTitle == Strings.Creator_InsideGameTitle);

        ui.Pickers.Folder = Path.GetDirectoryName(_work.GameFolder);
        ui.Click(Button(ui, Strings.Creator_PickFolder));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        creator.RefusalTitle.Should().Be(Strings.Creator_InsideGameTitle, "a folder that holds a game overlaps it too");
        Directory.Exists(ui.CreatorDirectory).Should().BeFalse("no project came of it");
    }

    [AvaloniaFact]
    public async Task A_project_without_a_folder_takes_one_on_its_card_outside_its_game()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        ui.Pickers.File = Path.Combine(_work.Root, "no-folder.exampleproj");
        File.WriteAllText(ui.Pickers.File, """{ "formatVersion": 4, "game": { "store": "steam", "id": "5100", "name": "Lantern Isle" } }""");
        ui.Click(Button(ui, Strings.Creator_Import));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { Game: not null });
        var step = Page(ui).Cell!;
        step.State.Should().Be(CellState.NoFolder);
        await ui.RenderBothAsync("creator-files");

        step.DropCommand.Execute(_work.GameFolder);
        Page(ui).ProblemTitle.Should().Be(Strings.Creator_GameFolderTitle);
        Page(ui).Session!.Part.AuthorFolder.Should().BeEmpty();

        ui.Pickers.Folder = _work.AuthorFolder;
        ui.Click(Button(ui, Strings.Creator_ChangeFolder));
        await UiHarness.WaitUntilAsync(() => Page(ui).Cell!.IsCompared);
        Page(ui).HasProblem.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Files_the_game_already_has_leave_nothing_to_package_and_keep_the_check_locked()
    {
        File.Delete(Path.Combine(_work.AuthorFolder, "data", "extra", "credits.txt"));
        _work.Author("data/strings.bin", "strings of the original game");
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        var step = page.Cell!;

        await UiHarness.WaitUntilAsync(() => step.HasNothingToPackage);
        step.NothingTitle.Should().Be(Strings.Creator_NothingTitle);
        step.IdenticalFiles.Should().Equal("data/strings.bin");
        Page(ui).CanBuild.Should().BeFalse();
        await RenderAsync(ui, "creator-identical");
    }

    [AvaloniaFact]
    public async Task Going_back_from_the_operations_returns_to_the_table()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared && page.Session!.Review is not null);
        await ui.RenderBothAsync("creator-single");

        ui.Click(Button(ui, page.AllOperationsText));
        page.IsPartView.Should().BeTrue();
        await ui.RenderBothAsync("creator-operations");
        ui.Press(Key.Left, RawInputModifiers.Alt);
        page.IsPartView.Should().BeFalse("the way back leaves the operations before it leaves the project");
        ui.Shell.Navigator.Current.Should().BeSameAs(page);

        ui.Click(Button(ui, page.AllOperationsText));
        ui.Press(Key.Escape);
        page.IsPartView.Should().BeFalse();
        ui.Press(Key.Left, RawInputModifiers.Alt);
        ui.Shell.Navigator.Current.Should().BeOfType<CreatorPageViewModel>();
    }

    [AvaloniaFact]
    public async Task A_file_changed_while_the_window_was_away_compares_again_when_it_comes_back()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared && page.Session!.Review is not null);
        var compared = page.Session!.Comparison;

        ui.Window.Activate();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        page.Session.Comparison.Should().BeSameAs(compared, "nothing changed while the window was away");

        _work.Author("data/extra/notes.txt", "notes the author added in an editor");
        ui.Window.Activate();
        await UiHarness.WaitUntilAsync(() => page.Session!.Comparison != compared && page.Cell!.IsCompared);
        page.Session.Comparison!.FindAuthorFile("data/extra/notes.txt").Should().NotBeNull();

        compared = page.Session.Comparison;
        ui.Agent.Answer = request => request is GetDetectedGames
            ? new DetectedGames([.. _work.Detected.Select(entry => entry.Key == CreatorWorkspace.Lantern ? entry with { Game = entry.Game with { BuildId = "18002" } } : entry)])
            : null;
        ui.Window.Activate();
        await UiHarness.WaitUntilAsync(() => page.Session!.Comparison != compared && page.Cell!.IsCompared);
        page.VersionLine.Should().Contain(Text.Format(Strings.Creator_GameBuild, ("build", "18002")), "the store updated the game meanwhile");
    }

    [AvaloniaFact]
    public async Task A_version_of_another_title_can_be_added_and_the_first_removed_keeping_the_part_s_work()
    {
        var tidewater = Path.Combine(_work.Root, "Games", "Tidewater");
        Directory.CreateDirectory(Path.Combine(tidewater, "data"));
        File.WriteAllText(Path.Combine(tidewater, "data", "strings.bin"), "strings of another game");
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared);
        page.Session!.ChangePart(part => part.WithRuntimeFile(new RuntimeFileChoice("data/movies.idx", RuntimeFileKind.CreatesFile)));
        ui.Find<Button>().Should().NotContain(button => button.IsEffectivelyVisible && Equals(button.Content, page.Cell!.RemoveText), "the only version goes only once another took its place");

        ui.Click(Button(ui, Strings.Creator_AddGameVersion));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is NewProjectDialogViewModel { IsListed: true });
        var dialog = (NewProjectDialogViewModel)ui.Shell.Dialogs.Current!;
        dialog.Title.Should().Be(Strings.Creator_AddGameVersion);
        var options = dialog.Rows.SelectMany(row => new[] { row.Left, row.Right }).OfType<GameOption>().ToList();
        options.Select(option => option.Name).Should().Equal("Ashfall Chronicles", "Tidewater");
        dialog.PickCommand.Execute(options[1]);
        dialog.CreateCommand.Execute(null);
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.Cells.All(cell => cell.State is CellState.Compared or CellState.NothingToPackage));

        ui.Click(Button(ui, page.Cells[0].Name));
        ui.Click(Button(ui, page.Cells[0].RemoveText));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel);
        ((ConfirmDialogViewModel)ui.Shell.Dialogs.Current!).Title.Should().Be(Text.Format(Strings.Creator_RemoveVersionTitle, ("store", "Steam")));
        await ui.RenderBothAsync("creator-remove-version");
        ui.Click(Button(ui, Strings.Creator_RemoveVersionConfirm));

        await UiHarness.WaitUntilAsync(() => !page.IsTable && page.Cell is { IsCompared: true });
        page.Session.Project.Versions.Select(version => version.Game.Key).Should().Equal(CreatorWorkspace.Tidewater);
        (page.Session.Part.AuthorFolder, page.Session.Part.RuntimeFiles.Single().Path).Should().Be((_work.AuthorFolder, "data/movies.idx"));
        page.Session.Comparison!.GameDirectory.Should().Be(tidewater);
    }

    [AvaloniaFact]
    public async Task The_panel_overlays_the_table_in_the_smallest_window_and_Esc_closes_it()
    {
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), width: 960, height: 600);
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : null;
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        page.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => page.HasOffer);
        ui.Click(Button(ui, page.OfferAction));
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.CanBuild);
        page.Cells[1].Brief.Label.Should().Be(Text.Format(Strings.Creator_CellState, ("state", "SameAs"), ("store", "Steam")), "the copy holds the same files");

        ui.Click(Button(ui, page.Cells[1].Name));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Find<ProjectPage>().Single().Classes.Should().Contain("overlay");
        ui.Window.InputHitTest(Middle(ui, ui.Find<Button>("BuildButton"))).Should().NotBe(ui.Find<Button>("BuildButton"), "the panel covers the bar");
        ui.Unnamed().Should().BeEmpty();
        ui.Render("creator-panel-overlay");

        ui.Press(Avalonia.Input.Key.Escape);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        page.IsPanelOpen.Should().BeFalse();
        ((Visual?)ui.Window.InputHitTest(Middle(ui, ui.Find<Button>("BuildButton"))))!.FindAncestorOfType<Button>(includeSelf: true)
            .Should().Be(ui.Find<Button>("BuildButton"), "the build is in reach once the panel closed");
    }

    [AvaloniaFact]
    public async Task A_comparison_runs_one_cell_at_a_time_and_a_stop_ends_only_its_cell()
    {
        var held = new HeldFileSystem(_work.Author("data/config.xml", "<config language=\"xx\" />"));
        held.Reads.Set();
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), held);
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : null;
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared && page.HasOffer);
        held.Reads.Reset();

        ui.Click(Button(ui, page.OfferAction));
        await UiHarness.WaitUntilAsync(() => page.Cells.Count == 2 && page.Cells[0].FileLine.Length > 0);
        (page.Cells[1].IsComparing, page.Cells[1].FileLine).Should().Be((true, ""), "the second cell waits for the first");
        (Comparison("Steam"), Comparison("GOG")).Should().Be(((false, true), (false, true)), "a comparison under way is neither a failure nor done");
        await ui.RenderBothAsync("creator-table-comparing");
        page.Cells[0].CancelCommand.Execute(null);

        await UiHarness.WaitUntilAsync(() => page.Cells[0].State == CellState.Stopped && page.Cells[1].FileLine.Length > 0);
        (Comparison("Steam"), Comparison("GOG")).Should().Be(((true, false), (false, true)), "a stopped comparison holds the build back as a failure");
        held.Reads.Set();
        await UiHarness.WaitUntilAsync(() => page.Cells[1].IsCompared);
        page.Cells[0].State.Should().Be(CellState.Stopped);
        await ui.RenderBothAsync("creator-cell-states");
        ui.Click(Button(ui, page.Cells[0].Name));
        ui.Find<Button>().Should().Contain(button => button.Name == "ToAdvanced" && button.IsEffectivelyVisible, "the stopped cell still has its plan to turn into steps");

        (bool Missing, bool Pending) Comparison(string store)
        {
            var item = page.BeforeItems.Single(item => item.Text == Text.Format(Strings.Creator_BeforeItem, ("item", "Comparison"), ("store", store)));
            return (item.IsMissing, item.IsPending);
        }
    }

    [AvaloniaFact]
    public async Task A_version_with_an_install_holds_the_build_and_says_why()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        page.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => page.CanBuild);

        ui.Push(TestStates.Idle(creatorMode: true) with
        {
            Version = 2,
            Games =
            [
                new GameView
                {
                    Key = CreatorWorkspace.Lantern,
                    Name = "Lantern Isle",
                    Status = new GameStatusView(MyGameState.Installed),
                    Detected = _work.Detected[1].Game,
                    Installs =
                    [
                        new InstallView
                        {
                            Id = new Guid("3b9e2c41-7d5a-4f60-9c18-a2e4b7d1f053"),
                            Status = new GameStatusView(MyGameState.Installed),
                            InstalledAt = TestStates.Now,
                            InstallDirectory = _work.GameFolder,
                            BackupDirectory = Path.Combine(_work.GameFolder, ".example-app"),
                            PackageFileName = "Lantern Isle.zip",
                        },
                    ],
                },
            ],
        });

        page.Cell!.State.Should().Be(CellState.Installed);
        page.CanBuild.Should().BeFalse();
        page.BlockedText.Should().Be(Text.Format(Strings.Creator_ConfirmBlocked,
            ("what", Text.Format(Strings.Creator_BlockedVersion, ("reason", "Install"), ("store", "Steam")))));
        Button(ui, Strings.Shell_Library).Should().NotBeNull("the install's way to its game stands under it");
        await ui.RenderBothAsync("creator-version-installed");

        ui.Push(TestStates.Idle(creatorMode: true) with { Version = 3 });
        await UiHarness.WaitUntilAsync(() => page.Cell!.IsCompared && page.CanBuild);
    }

    [AvaloniaFact]
    public async Task A_version_that_blocks_holds_the_build_and_says_why()
    {
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : null;
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        page.Description.Version = "1.0";
        await UiHarness.WaitUntilAsync(() => page.HasOffer);
        ui.Click(Button(ui, page.OfferAction));
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.CanBuild);

        var install = TestGames.Install(TestGames.Offer("Lantern Isle"), 1, new GameStatusView(Core.State.MyGameState.Installed)) with
        {
            InstallDirectory = gog.Game.InstallDirectory,
        };
        ui.Push(TestStates.Idle(creatorMode: true) with { Version = 2, Games = [TestGames.Available("Lantern Isle", gog.Key.StoreGameId) with { Key = gog.Key, Installs = [install] }] });

        (page.Cells[1].State, page.CanBuild, page.Cell).Should().Be((CellState.Installed, false, page.Cells[0]));
        var blocked = Text.Format(Strings.Creator_BeforeItem, ("item", "VersionBlocked"), ("store", "GOG"));
        page.BeforeItems.Should().Contain(item => item.IsMissing && item.Text == blocked);
        page.BlockedText.Should().Be(Text.Format(Strings.Creator_BlockedCell, ("part", Strings.Creator_PartBase), ("store", "GOG"),
            ("text", Text.Format(Strings.Creator_ConfirmBlocked, ("what", Text.Format(Strings.Creator_BlockedVersion, ("reason", "Install"), ("store", "GOG")))))),
            "the chosen cell holds nothing back, so the bar names the one that does");
        ui.Click(ui.Find<Button>("BlockedCell"));
        (page.Cell, page.IsPanelOpen).Should().Be((page.Cells[1], true), "the reasons lead to the cell they are about");

        ui.Push(TestStates.Idle(creatorMode: true) with { Version = 3 });
        await UiHarness.WaitUntilAsync(() => page.Cells.All(cell => cell.IsCompared) && page.CanBuild);
    }

    [AvaloniaFact]
    public async Task Switching_cells_while_a_plan_is_being_made_keeps_each_row_with_its_own_plan()
    {
        var gog = _work.CopyOfLantern(new GameKey(StoreId.Gog, "1205100"));
        // The copy holds another original of the file the author changed, so its row of that file differs.
        File.WriteAllText(Path.Combine(gog.Game.InstallDirectory, "data", "strings.bin"), "strings of the original game, as the other store ships them");
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([.. _work.Detected, gog]) : null;
        var page = await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
        await UiHarness.WaitUntilAsync(() => page.HasOffer);
        ui.Click(Button(ui, page.OfferAction));
        await UiHarness.WaitUntilAsync(() => page.IsTable && page.Cells.All(cell => cell.IsCompared && cell.Review is not null) && !page.Session!.IsPlanning);
        page.ShowOperationsCommand.Execute(null);

        foreach (var cell in new[] { page.Cells[1], page.Cells[0], page.Cells[1] })
        {
            page.Session!.Change(project => project with { Metadata = project.Metadata with { Title = project.Metadata.Title + "!" } });
            page.Session.IsPlanning.Should().BeTrue();
            page.SelectCellCommand.Execute(cell);
            await UiHarness.WaitUntilAsync(() => !page.Session.IsPlanning);

            page.Cell.Should().Be(cell);
            page.Review.Operations.OfType<OperationRow>().Single(row => row.Entry?.File.Path == "data/strings.bin").Entry
                .Should().BeSameAs(cell.Review!.Entries.Single(entry => entry.File.Path == "data/strings.bin"), "the rows show the chosen cell's plan made after the switch");
        }
    }

    [AvaloniaFact]
    public async Task An_import_failure_adds_nothing_to_the_library()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var page = Library(ui);
        ui.Pickers.File = Path.Combine(_work.Root, "newer.exampleproj");
        File.WriteAllText(ui.Pickers.File, """{ "formatVersion": 99, "mode": "galaxy" }""");

        ui.Click(Button(ui, Strings.Creator_Import));
        await UiHarness.WaitUntilAsync(() => page.HasProblem);
        page.ProblemTitle.Should().Be(Strings.Creator_OpenFailedTitle);
        page.ProblemText.Should().Be(Text.Format(Strings.Creator_OpenFailed, ("reason", nameof(ProjectLoadError.NewerVersion)),
            ("product", "Example App")));
        await RenderAsync(ui, "creator-import-newer");

        ui.Pickers.File = Path.Combine(_work.Root, "damaged.exampleproj");
        File.WriteAllText(ui.Pickers.File, "{ not a project");
        ui.Click(Button(ui, Strings.Creator_Import));
        await UiHarness.WaitUntilAsync(() => page.ProblemText.Contains(Text.Format(Strings.Creator_OpenFailed,
            ("reason", nameof(ProjectLoadError.Damaged)), ("product", "Example App")), StringComparison.Ordinal));
        page.OpenPage.Should().BeNull();
        Directory.Exists(ui.CreatorDirectory).Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("""{ "formatVersion": 2, "targets": [ { "store": "steam", "id": "5100" } ], "gameName": "Lantern Isle", "modFolder": "AUTHOR" }""")]
    [InlineData("""{ "formatVersion": 3, "game": { "store": "steam", "id": "5100", "name": "Lantern Isle" }, "authorFolder": "AUTHOR" }""")]
    [InlineData("""{ "formatVersion": 4, "game": { "store": "steam", "id": "5100", "name": "Lantern Isle" }, "authorFolder": "AUTHOR" }""")]
    public async Task A_project_file_of_an_earlier_format_goes_in_through_import_and_compares_at_once(string content)
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        ui.Pickers.File = Path.Combine(_work.Root, "earlier.exampleproj");
        File.WriteAllText(ui.Pickers.File, content.Replace("AUTHOR", _work.AuthorFolder.Replace(@"\", @"\\", StringComparison.Ordinal), StringComparison.Ordinal));

        ui.Click(Button(ui, Strings.Creator_Import));

        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { Cell.IsCompared: true });
        Page(ui).Session!.Comparison!.GameDirectory.Should().Be(_work.GameFolder);
        Page(ui).Session!.Part.AuthorFolder.Should().Be(_work.AuthorFolder);
        Page(ui).ProjectName.Should().Be("earlier");
    }

    [AvaloniaFact]
    public async Task A_project_of_the_list_that_fails_to_open_is_not_told_as_a_failed_import()
    {
        var library = Path.Combine(_work.Root, "Library");
        var id = Guid.NewGuid();
        using (var projects = new ProjectLibrary(library, TimeProvider.System))
        {
            await projects.Store(id).SaveAsync(new CreatorProject { Id = id, Name = "Lantern Isle" }, CancellationToken.None);
        }

        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library);
        await UiHarness.WaitUntilAsync(() => Library(ui).Cards.Any());
        File.Delete(Path.Combine(library, id.ToString(), "project.json"));

        ui.Click(Card(ui, "Lantern Isle"));

        await UiHarness.WaitUntilAsync(() => Library(ui).HasProblem);
        Library(ui).ProblemTitle.Should().Be(Strings.Creator_ProjectOpenFailedTitle);
    }

    [AvaloniaFact]
    public async Task A_project_saves_by_itself_and_reopens_as_it_was()
    {
        var library = Path.Combine(_work.Root, "Library");
        CreatorProject saved;
        using (var ui = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library))
        {
            await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle");
            await UiHarness.WaitUntilAsync(() => Page(ui).Cell!.IsCompared);
            Page(ui).Session!.Change(project => project with { Metadata = project.Metadata with { Title = "Lantern Isle revised" } });
            saved = Page(ui).Session!.Project;
            await UiHarness.WaitUntilAsync(() => Stored(library, saved.Id) == saved);
            await ui.RenderBothAsync("creator-project");
        }

        using var reopened = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library);
        await UiHarness.WaitUntilAsync(() => Library(reopened).Cards.Any());
        Library(reopened).Cards.Should().ContainSingle().Which.Name.Should().Be("Lantern Isle");

        reopened.Click(Card(reopened, "Lantern Isle"));

        await UiHarness.WaitUntilAsync(() => reopened.Shell.Navigator.Current is ProjectPageViewModel { Cell.IsCompared: true });
        Page(reopened).Session!.Project.Should().Be(saved);
    }

    [AvaloniaFact]
    public async Task A_change_made_just_before_closing_is_on_the_disk()
    {
        var library = Path.Combine(_work.Root, "Library");
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library);
        var session = (await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle")).Session!;
        session.Change(project => project with { Metadata = project.Metadata with { Title = "Lantern Isle revised" } });

        (await ui.Agent.ShutdownHandler!(ShutdownReason.Exit)).Should().BeTrue();

        Stored(library, session.Project.Id).Should().Be(session.Project);
    }

    [AvaloniaFact]
    public async Task A_change_after_a_refused_close_is_saved_too()
    {
        var library = Path.Combine(_work.Root, "Library");
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library);
        var session = (await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle")).Session!;
        var closing = ui.Shell.CloseFlow.DecideAsync();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel { IsClosing: false });
        Stored(library, session.Project.Id).Should().Be(session.Project, "the close flushed before it asked what closing means");
        ui.Shell.Dialogs.CancelCurrent();
        (await closing).Should().BeFalse();

        session.Change(project => project with { Metadata = project.Metadata with { Title = "Lantern Isle revised" } });

        await UiHarness.WaitUntilAsync(() => Stored(library, session.Project.Id) == session.Project);
    }

    [AvaloniaFact]
    public async Task A_change_made_during_the_flush_is_saved_too()
    {
        var store = new HeldStore();
        await using var session = new CreatorSession(UiHarness.Context().Creator, new CreatorProject(), store, NullLogger.Instance);
        session.Change(project => project with { Name = "first" });

        var flushing = session.FlushAsync();
        await store.Saving.WaitAsync(UiHarness.TimeLimit);
        session.Change(project => project with { Name = "second" });
        store.Release();

        (await flushing).Should().BeTrue();
        store.Saved.Select(project => project.Name).Should().Equal("first", "second");
    }

    [AvaloniaFact]
    public async Task A_save_that_failed_is_told_and_the_next_change_saves_again()
    {
        var library = Path.Combine(_work.Root, "Library");
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library);
        var session = (await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle")).Session!;
        var file = Path.Combine(library, session.Project.Id.ToString(), "project.json");
        await UiHarness.WaitUntilAsync(() => File.Exists(file));
        var name = (string title) => session.Change(project => project with { Metadata = project.Metadata with { Title = title } });

        // A folder in the file's place refuses the save, as a disk that is full or read-only does.
        File.Delete(file);
        Directory.CreateDirectory(file);
        name("refused");
        await UiHarness.WaitUntilAsync(() => session.SaveFailed);
        Page(ui).ProblemText.Should().Be(Strings.Creator_SaveFailed);
        await ui.RenderBothAsync("creator-save-failed");

        Directory.Delete(file);
        name("saved");
        await UiHarness.WaitUntilAsync(() => !session.SaveFailed && Stored(library, session.Project.Id) == session.Project);
        Page(ui).HasProblem.Should().BeFalse("a save that works takes the failure back");

        File.Delete(file);
        Directory.CreateDirectory(file);
        name("refused again");
        var closing = ui.Agent.ShutdownHandler!(ShutdownReason.Exit);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ConfirmDialogViewModel { IsClosing: false });
        var question = (ConfirmDialogViewModel)ui.Shell.Dialogs.Current!;
        (question.Title, question.Message).Should().Be((Strings.Creator_SaveFailedTitle, Strings.Creator_CloseUnsaved));
        ui.Shell.Dialogs.CancelCurrent();
        (await closing).Should().BeFalse("the changes would be lost");

        Directory.Delete(file);
        (await session.FlushAsync()).Should().BeTrue("a disk that recovered takes the project without another change");
        session.SaveFailed.Should().BeFalse();
        Stored(library, session.Project.Id).Should().Be(session.Project);
    }

    [AvaloniaFact]
    public async Task A_running_comparison_does_not_hold_the_close_and_goes_on_when_the_close_is_refused()
    {
        // A file of the game's size is hashed, which the held file system keeps from finishing.
        _work.Author("data/strings.bin", "strings of the changed  game");
        var files = new HeldFileSystem(_work.Root);
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), files);
        var step = (await CreatorWorkspace.NewProjectAsync(ui, _work.AuthorFolder, "Lantern Isle")).Cell!;
        await UiHarness.WaitUntilAsync(() => step.FileLine.Length > 0);

        var closing = ui.Shell.CloseFlow.DecideAsync();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel { IsClosing: false });
        ((ChoiceDialogViewModel)ui.Shell.Dialogs.Current!).Title.Should().Be(Strings.Shell_CloseTitle, "only what closing means is asked");
        ui.Shell.Dialogs.CancelCurrent();
        (await closing).Should().BeFalse();
        step.IsComparing.Should().BeTrue();

        files.Reads.Set();
        await UiHarness.WaitUntilAsync(() => Page(ui).Cell!.IsCompared);
    }

    [AvaloniaFact]
    public async Task The_recent_list_is_brought_over_once_and_a_damaged_file_is_named()
    {
        var good = Path.Combine(_work.Root, "Good.exampleproj");
        File.WriteAllText(good, """{ "formatVersion": 4, "game": { "store": "steam", "id": "5100", "name": "Lantern Isle" } }""");
        var damaged = Path.Combine(_work.Root, "Damaged.exampleproj");
        File.WriteAllText(damaged, "{ not a project");
        var newer = Path.Combine(_work.Root, "Newer.exampleproj");
        File.WriteAllText(newer, """{ "formatVersion": 99 }""");
        var state = TestStates.Idle(creatorMode: true);
        state = state with { Settings = state.Settings with { RecentCreatorProjects = [good, damaged, newer] } };

        using var ui = await OpenAsync(state);

        await UiHarness.WaitUntilAsync(() => Library(ui).Cards.Any());
        Library(ui).Cards.Should().ContainSingle().Which.Name.Should().Be("Good");
        ui.Agent.Requests.OfType<ForgetRecentCreatorProjects>().Should().ContainSingle().Which.Paths.Should().Equal(good, damaged);
        Library(ui).Notices.Should().BeEquivalentTo(new ImportNotice[]
        {
            new(Text.Format(Strings.Creator_ImportSkipped, ("count", 1)), [CreatorWording.Wrappable(damaged)]),
            new(Text.Format(Strings.Creator_ImportWaiting, ("count", 1)), []),
        });
        File.ReadAllText(damaged).Should().Be("{ not a project");
        await ui.RenderBothAsync("creator-library-imported");

        ui.Click(ui.Find<Button>().First(button => Equals(button.DataContext, Library(ui).Notices[0])));
        Library(ui).Notices.Should().ContainSingle().Which.Files.Should().BeEmpty();

        ui.Push(state with { Version = state.Version + 1 });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(100));
        ui.Agent.Requests.OfType<ForgetRecentCreatorProjects>().Should().ContainSingle("the next open tries what waits");
    }

    [AvaloniaFact]
    public async Task Every_control_of_the_start_and_the_first_steps_has_a_name_and_the_smallest_window_keeps_the_way_on()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), width: 960, height: 600);
        ui.Unnamed().Should().BeEmpty();
        Visible(ui, Button(ui, Strings.Creator_New)).Should().BeTrue();
        Visible(ui, Button(ui, Strings.Creator_PickFolder)).Should().BeTrue();

        var many = Enumerable.Range(0, 200).Select(index => _work.Detected[2] with { Key = new GameKey(StoreId.Steam, $"7{index}"), Name = $"Tidewater {index}" });
        ui.Agent.Answer = request => request is GetDetectedGames ? new DetectedGames([_work.Detected[1], .. many]) : null;
        ui.Pickers.Folder = _work.AuthorFolder;
        ui.Click(Button(ui, Strings.Creator_New));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is NewProjectDialogViewModel { IsListed: true });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Unnamed().Should().BeEmpty();
        ui.Find<RadioButton>().Count().Should().BeLessThan(40, "a long list has controls only for the rows in sight");
        ui.Render("creator-new-many-960");

        ui.Click(Choice(ui, "Lantern Isle"));
        ui.Click(Button(ui, Strings.Creator_Create));
        await UiHarness.WaitUntilAsync(() => ui.Shell.Navigator.Current is ProjectPageViewModel { Cell.IsCompared: true });
        ui.Unnamed().Should().BeEmpty();
    }

    public void Dispose() => _work.Dispose();

    private async Task<UiHarness> OpenAsync(AppStateView state, Core.Platform.IFileSystem? fileSystem = null, double width = 1280,
        double height = 800, string? creatorDirectory = null)
    {
        var ui = new UiHarness(state, CreatorWorkspace.Compose(fileSystem), width, height,
            context: creatorDirectory is null ? null : context => context with { CreatorDirectory = creatorDirectory });
        ui.Agent.Answer = _work.Answer;
        ui.Shell.Navigator.Navigate(new CreatorRoute());
        await UiHarness.WaitUntilAsync(() => ui.Find<CreatorPage>().Any(page => page.IsEffectivelyVisible));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        return ui;
    }

    // Tidewater's folder holds an install of another project, so the creator offers it only once it is gone.
    private AppStateView Installed(AppStateView state)
    {
        var install = TestGames.Install(TestGames.Offer("Tidewater"), 1, new GameStatusView(Core.State.MyGameState.Installed)) with
        {
            InstallDirectory = _work.Detected[2].Game.InstallDirectory,
        };
        return state with { Games = [TestGames.Available("Tidewater", "5200") with { Installs = [install] }] };
    }

    // What the file manager hands over: only an item's kind and path are read.
    private static DataTransfer Dragged(IStorageItem item)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(item));
        return data;
    }

    private static void Drop(UiHarness ui, IStorageItem item)
    {
        var (point, data) = (Middle(ui, ui.Find<Border>("DropZone")), Dragged(item));
        ui.Window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy);
        ui.Window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy);
        UiHarness.Settle();
    }

    private static Point Middle(UiHarness ui, Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ui.Window)!.Value;

    // Steps slide in, so a render waits for the slide to end.
    private static async Task RenderAsync(UiHarness ui, string name)
    {
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render(name);
    }

    // The project as a window opening it now would read it; null while its file is not there.
    private static CreatorProject? Stored(string library, Guid id)
    {
        using var projects = new ProjectLibrary(library, TimeProvider.System);
        return projects.Store(id).LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static CreatorPageViewModel Library(UiHarness ui) => (CreatorPageViewModel)ui.Shell.Navigator.Current!;


    private static ProjectPageViewModel Page(UiHarness ui) => (ProjectPageViewModel)ui.Shell.Navigator.Current!;

    private static Button Card(UiHarness ui, string name) =>
        ui.Find<Button>().First(button => button.DataContext is ProjectCard card && card.Name == name && button.Classes.Contains("gameCard"));

    private static Button Button(UiHarness ui, string name) =>
        ui.Find<Button>().First(button => button.IsEffectivelyVisible
            && Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(button).GetName() == name);

    private static RadioButton Choice(UiHarness ui, string name) =>
        ui.Find<RadioButton>().First(choice => choice.IsEffectivelyVisible
            && Avalonia.Automation.AutomationProperties.GetName(choice) == name);

    private static bool Visible(UiHarness ui, Control control)
    {
        var scroller = control.FindAncestorOfType<ScrollViewer>()!;
        var top = control.TranslatePoint(new Point(0, 0), scroller)!.Value.Y;
        return top >= 0 && top + control.Bounds.Height <= scroller.Bounds.Height && ui.Window.Bounds.Contains(
            control.TranslatePoint(new Point(0, 0), ui.Window)!.Value);
    }

    /// <summary>A dropped file or folder as the file manager hands it over, of which the creator reads only its kind and path.</summary>
    public class DroppedItem : System.Reflection.DispatchProxy
    {
        private Uri? _path;

        public static TItem Of<TItem>(string path) where TItem : class, IStorageItem
        {
            var item = Create<TItem, DroppedItem>();
            ((DroppedItem)(object)item)._path = new Uri(path);
            return item;
        }

        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.Name == "get_Path" ? _path : throw new NotSupportedException();
    }

    /// <summary>Keeps a save waiting until the test lets it end, so the test can act during a flush.</summary>
    private sealed class HeldStore : IDocumentStore<CreatorProject>
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SemaphoreSlim Saving { get; } = new(0);

        public List<CreatorProject> Saved { get; } = [];

        public void Release() => _released.TrySetResult();

        public Task<CreatorProject?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult<CreatorProject?>(null);

        public async Task SaveAsync(CreatorProject document, CancellationToken cancellationToken)
        {
            Saving.Release();
            await _released.Task;
            Saved.Add(document);
        }
    }
}
