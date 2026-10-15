using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Creator;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class CreatorTests : IDisposable
{
    private readonly CreatorWorkspace _work = new();

    [AvaloniaFact]
    public async Task A_new_project_compares_the_chosen_game_with_the_author_s_folder_and_opens_the_check()
    {
        using var ui = await OpenAsync(Installed(TestStates.Idle(creatorMode: true)));
        await RenderAsync(ui, "creator-start");

        ui.Click(Button(ui, Strings.Creator_New));
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.Content is GameStepViewModel { IsLoaded: true });
        var games = (GameStepViewModel)page.Content!;
        var options = games.Rows.SelectMany(row => new[] { row.Left, row.Right }).OfType<GameOption>().ToList();
        options.Select(option => (option.Name, option.Block)).Should().Equal(
            ("Ashfall Chronicles", GameBlock.NotReady), ("Lantern Isle", GameBlock.None), ("Tidewater", GameBlock.Installed));
        options[2].BlockText.Should().Contain("Lantern Team").And.EndWith(GameWording.ShortVersion("1"));
        games.ContinueCommand.CanExecute(null).Should().BeFalse("no game is chosen yet");
        ui.Click(Button(ui, Strings.Creator_MissingGameLink));
        ui.Shell.Secondary.Single(item => item.IsActive).Section.Should().Be(ShellSection.Creator, "the panel belongs to where it was opened from");
        ui.Shell.Navigator.GoBackCommand.Execute(null);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Click(Choice(ui, "Lantern Isle"));
        await RenderAsync(ui, "creator-game");

        ui.Click(Button(ui, Strings.Creator_ToFiles));
        page.Content.Should().BeOfType<FilesStepViewModel>();
        await RenderAsync(ui, "creator-files");
        ui.Pickers.Folder = _work.AuthorFolder;
        ui.Click(Button(ui, Strings.Creator_PickFolder));

        await UiHarness.WaitUntilAsync(() => page.Step == CreatorStep.Review);
        page.Session!.Comparison!.Files.Select(file => (file.Path, file.Difference)).Should().Equal(
            ("data/extra/credits.txt", FileDifference.New), ("data/strings.bin", FileDifference.Changed));
        page.Session.Project.Should().BeEquivalentTo(new
        {
            Game = new ProjectGame(CreatorWorkspace.Lantern, "Lantern Isle"),
            GameFolder = _work.GameFolder,
            AuthorFolder = _work.AuthorFolder,
        });
        page.Steps.Select(step => step.State).Should().Equal(StepState.Done, StepState.Done, StepState.Current, StepState.Locked);
        ui.Agent.Requests.OfType<GetDetectedGames>().Should().ContainSingle();
    }

    [AvaloniaFact]
    public async Task A_comparison_shows_its_stage_bytes_and_file_and_can_be_cancelled()
    {
        // A file of the game's size is hashed, which the held file system keeps from finishing.
        _work.Author("data/strings.bin", "strings of the changed  game");
        var files = new HeldFileSystem(_work.Root);
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), files);
        var step = await FilesStepAsync(ui);

        step.UseFolder(_work.AuthorFolder);
        await UiHarness.WaitUntilAsync(() => step.FileLine.Length > 0);
        step.IsComparing.Should().BeTrue();
        step.Stage.Should().Contain(Text.Format(Strings.Creator_CompareStage, ("stage", nameof(ComparisonStage.Comparing))));
        await RenderAsync(ui, "creator-comparing");
        ui.Click(Button(ui, Strings.Dialog_Cancel));
        files.Reads.Set();

        await UiHarness.WaitUntilAsync(() => step.IsStopped);
        step.ProblemTitle.Should().Be(Strings.Creator_CompareCancelledTitle);
        Page(ui).Session!.Comparison.Should().BeNull();
        Page(ui).Steps[(int)CreatorStep.Review].State.Should().Be(StepState.Locked);
    }

    [AvaloniaFact]
    public async Task A_comparison_that_fails_unexpectedly_says_so_and_leaves_the_creator_open()
    {
        _work.Author("data/strings.bin", "strings of the changed  game");
        var files = new HeldFileSystem(_work.Root) { Failure = new InvalidDataException("The disk returned nonsense.") };
        files.Reads.Set();
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), files);
        var step = await FilesStepAsync(ui);

        step.UseFolder(_work.AuthorFolder);
        await UiHarness.WaitUntilAsync(() => step.IsStopped);

        (step.ProblemTitle, step.ProblemText).Should().Be(
            (Strings.Creator_CompareStoppedTitle, Text.Format(Strings.Creator_Unexpected, ("error", nameof(InvalidDataException)))));
        files.Failure = null;
        step.UseFolder(_work.AuthorFolder);
        await UiHarness.WaitUntilAsync(() => Page(ui).Session!.Comparison is not null);
    }

    [AvaloniaFact]
    public async Task The_folder_is_compared_with_the_game_chosen_last_whether_the_earlier_comparison_ended_or_not()
    {
        // Files of the same size are hashed, which the held file system keeps from finishing while the test holds it.
        var changed = "strings of the changed  game";
        _work.Author("data/strings.bin", changed);
        var tidewater = Path.Combine(_work.Root, "Games", "Tidewater");
        Directory.CreateDirectory(Path.Combine(tidewater, "data"));
        File.WriteAllText(Path.Combine(tidewater, "data", "strings.bin"), new string('t', changed.Length));
        var files = new HeldFileSystem(_work.Root);
        files.Reads.Set();
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), files);
        var step = await FilesStepAsync(ui);
        var page = Page(ui);
        step.UseFolder(_work.AuthorFolder);
        await UiHarness.WaitUntilAsync(() => page.Step == CreatorStep.Review);

        page.OpenStepCommand.Execute(page.Steps[(int)CreatorStep.Game]);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Click(Choice(ui, "Tidewater"));
        files.Reads.Reset();
        ui.Click(Button(ui, Strings.Creator_ToFiles));
        await UiHarness.WaitUntilAsync(() => step.IsComparing);
        step.HasProblem.Should().BeFalse();
        await RenderAsync(ui, "creator-comparing-other-game");
        ui.Click(Button(ui, Strings.Creator_ChangeGame));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Click(Choice(ui, "Lantern Isle"));
        files.Reads.Set();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        page.Step.Should().Be(CreatorStep.Game, "the comparison with the game left behind no longer counts");
        page.Session!.Comparison.Should().BeNull();
        ui.Click(Button(ui, Strings.Creator_ToFiles));
        await UiHarness.WaitUntilAsync(() => page.Step == CreatorStep.Review);
        page.Session.Comparison!.GameDirectory.Should().Be(_work.GameFolder);
        page.Session.Project.GameFolder.Should().Be(_work.GameFolder);
    }

    [AvaloniaFact]
    public async Task The_game_s_own_folder_is_refused_before_anything_is_read()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var step = await FilesStepAsync(ui);

        step.UseFolder(_work.GameFolder);

        step.ShowsDropZone.Should().BeTrue();
        step.ProblemTitle.Should().Be(Strings.Creator_GameFolderTitle);
        Page(ui).Session!.Project.AuthorFolder.Should().BeEmpty();
        await RenderAsync(ui, "creator-game-folder");
    }

    [AvaloniaFact]
    public async Task Files_the_game_already_has_leave_nothing_to_package_and_keep_the_check_locked()
    {
        File.Delete(Path.Combine(_work.AuthorFolder, "data", "extra", "credits.txt"));
        _work.Author("data/strings.bin", "strings of the original game");
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var step = await FilesStepAsync(ui);

        step.UseFolder(_work.AuthorFolder);

        await UiHarness.WaitUntilAsync(() => step.HasNothingToPackage);
        step.NothingTitle.Should().Be(Strings.Creator_NothingTitle);
        step.IdenticalFiles.Should().Equal("data/strings.bin");
        Page(ui).Steps[(int)CreatorStep.Review].State.Should().Be(StepState.Locked);
        await RenderAsync(ui, "creator-identical");
    }

    [AvaloniaFact]
    public async Task A_project_from_a_newer_app_or_a_damaged_one_says_why_it_does_not_open()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var page = Page(ui);
        ui.Pickers.File = _work.Author("newer.exampleproj", """{ "formatVersion": 99, "mode": "galaxy" }""");

        ui.Click(Button(ui, Strings.Creator_Open));
        await UiHarness.WaitUntilAsync(() => page.HasProblem);
        page.ProblemText.Should().Be(Text.Format(Strings.Creator_OpenFailed, ("reason", nameof(ProjectLoadError.NewerVersion)),
            ("product", "Example App")));
        await RenderAsync(ui, "creator-open-newer");

        ui.Pickers.File = _work.Author("damaged.exampleproj", "{ not a project");
        ui.Click(Button(ui, Strings.Creator_Open));
        await UiHarness.WaitUntilAsync(() => page.ProblemText.Contains(Text.Format(Strings.Creator_OpenFailed,
            ("reason", nameof(ProjectLoadError.Damaged)), ("product", "Example App")), StringComparison.Ordinal));
        page.HasProject.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_saved_project_reopens_from_the_recent_ones_where_it_was_left()
    {
        var path = Path.Combine(_work.Root, "Lantern Isle.exampleproj");
        using (var ui = await OpenAsync(TestStates.Idle(creatorMode: true)))
        {
            var step = await FilesStepAsync(ui);
            step.UseFolder(_work.AuthorFolder);
            await UiHarness.WaitUntilAsync(() => Page(ui).Step == CreatorStep.Review);
            ui.Pickers.SaveFile = path;

            ui.Click(Button(ui, Strings.Creator_Save));
            await UiHarness.WaitUntilAsync(() => File.Exists(path) && !Page(ui).IsUnsaved);
            ui.Pickers.Suggested.Should().Equal("Lantern Isle.exampleproj");
            ui.Agent.Requests.OfType<RememberCreatorProject>().Should().Equal(new RememberCreatorProject(path));
        }

        File.SetLastWriteTimeUtc(path, TestStates.Now.AddDays(-1).UtcDateTime);
        using var reopened = await OpenAsync(TestStates.Idle(creatorMode: true) with
        {
            Settings = TestStates.Idle(creatorMode: true).Settings with { RecentCreatorProjects = [path] },
        });
        var recent = Page(reopened).Recent.Should().ContainSingle().Subject;
        recent.Name.Should().Be("Lantern Isle");
        await UiHarness.WaitUntilAsync(() => recent.Changed.Length > 0);
        recent.Changed.Should().Be(Text.Format(Strings.Creator_RecentChanged,
            ("when", Formats.RelativeTime(TestStates.Now.AddDays(-1), reopened.Clock))));
        await RenderAsync(reopened, "creator-recent");
        reopened.Click(Button(reopened, "Lantern Isle"));
        await UiHarness.WaitUntilAsync(() => Page(reopened).Step == CreatorStep.Review);
        Page(reopened).Session!.Path.Should().Be(path);
        Page(reopened).IsUnsaved.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Unsaved_work_keeps_the_window_open_until_the_author_saves_or_discards_it()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        await FilesStepAsync(ui);
        Page(ui).IsUnsaved.Should().BeTrue();

        var closing = ui.Agent.ShutdownHandler!(ShutdownReason.Update);
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel);
        await RenderAsync(ui, "creator-unsaved");
        ui.Shell.Dialogs.CancelCurrent();
        (await closing).Should().BeFalse("dismissing the question keeps the work");

        ui.Pickers.SaveFile = null;
        closing = ui.Agent.ShutdownHandler!(ShutdownReason.Exit);
        await Answer(ui, Strings.Creator_UnsavedSave);
        (await closing).Should().BeFalse("a save the author cancelled keeps the work");

        closing = ui.Agent.ShutdownHandler!(ShutdownReason.Exit);
        await Answer(ui, Strings.Creator_UnsavedDiscard);
        (await closing).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Every_control_of_the_start_and_the_first_steps_has_a_name_and_the_smallest_window_keeps_the_way_on()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), width: 960, height: 600);
        ui.Unnamed().Should().BeEmpty();
        Visible(ui, Button(ui, Strings.Creator_New)).Should().BeTrue();

        await FilesStepAsync(ui);
        ui.Unnamed().Should().BeEmpty();
        Visible(ui, Button(ui, Strings.Creator_PickFolder)).Should().BeTrue();
        await RenderAsync(ui, "creator-files-small");
    }

    public void Dispose() => _work.Dispose();

    private async Task<UiHarness> OpenAsync(AppStateView state, Core.Platform.IFileSystem? fileSystem = null, double width = 1280,
        double height = 800)
    {
        var ui = new UiHarness(state, CreatorWorkspace.Compose(fileSystem), width, height);
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

    private static async Task<FilesStepViewModel> FilesStepAsync(UiHarness ui)
    {
        ui.Click(Button(ui, Strings.Creator_New));
        var page = Page(ui);
        await UiHarness.WaitUntilAsync(() => page.Content is GameStepViewModel { IsLoaded: true });
        ui.Click(Choice(ui, "Lantern Isle"));
        ui.Click(Button(ui, Strings.Creator_ToFiles));
        return (FilesStepViewModel)page.Content!;
    }

    // Steps slide in, so a render waits for the slide to end.
    private static async Task RenderAsync(UiHarness ui, string name)
    {
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render(name);
    }

    private static CreatorPageViewModel Page(UiHarness ui) => (CreatorPageViewModel)ui.Shell.Navigator.Current!;

    private static Button Button(UiHarness ui, string name) =>
        ui.Find<Button>().First(button => button.IsEffectivelyVisible
            && Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(button).GetName() == name);

    private static RadioButton Choice(UiHarness ui, string name) =>
        ui.Find<RadioButton>().First(choice => choice.IsEffectivelyVisible
            && Avalonia.Automation.AutomationProperties.GetName(choice) == name);

    private static async Task Answer(UiHarness ui, string choice)
    {
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel { IsClosing: false });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(250));
        ui.Click(Button(ui, choice));
    }

    private static bool Visible(UiHarness ui, Control control)
    {
        var scroller = control.FindAncestorOfType<ScrollViewer>()!;
        var top = control.TranslatePoint(new Point(0, 0), scroller)!.Value.Y;
        return top >= 0 && top + control.Bounds.Height <= scroller.Bounds.Height && ui.Window.Bounds.Contains(
            control.TranslatePoint(new Point(0, 0), ui.Window)!.Value);
    }
}
