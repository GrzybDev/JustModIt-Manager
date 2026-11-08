using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.IO;
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
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Ui.Tests;

public sealed class CreatorTests : IDisposable
{
    private readonly CreatorWorkspace _work = new();

    [AvaloniaFact]
    public async Task A_new_project_compares_the_chosen_game_with_the_author_s_folder_and_opens_the_check()
    {
        using var ui = await OpenAsync(Installed(TestStates.Idle(creatorMode: true)));
        await RenderBothAsync(ui, "creator-start");

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
        page.Session.Version.Should().Be(new ProjectVersion(new ProjectGame(CreatorWorkspace.Lantern, "Lantern Isle")) { GameFolder = _work.GameFolder });
        page.Session.Part.AuthorFolder.Should().Be(_work.AuthorFolder);
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
        page.Session.Version!.GameFolder.Should().Be(_work.GameFolder);
    }

    [AvaloniaFact]
    public async Task The_game_s_own_folder_is_refused_before_anything_is_read()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var step = await FilesStepAsync(ui);

        step.UseFolder(_work.GameFolder);

        step.ShowsDropZone.Should().BeTrue();
        step.ProblemTitle.Should().Be(Strings.Creator_GameFolderTitle);
        Page(ui).Session!.Part.AuthorFolder.Should().BeEmpty();
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
    public async Task An_import_failure_adds_nothing_to_the_library()
    {
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true));
        var page = Page(ui);
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
        page.HasProject.Should().BeFalse();
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

        await UiHarness.WaitUntilAsync(() => Page(ui).Step == CreatorStep.Review);
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
        await UiHarness.WaitUntilAsync(() => Page(ui).HasRecent);
        File.Delete(Path.Combine(library, id.ToString(), "project.json"));

        ui.Click(Button(ui, "Lantern Isle"));

        await UiHarness.WaitUntilAsync(() => Page(ui).HasProblem);
        Page(ui).ProblemTitle.Should().Be(Strings.Creator_ProjectOpenFailedTitle);
    }

    [AvaloniaFact]
    public async Task A_project_saves_by_itself_and_reopens_as_it_was()
    {
        var library = Path.Combine(_work.Root, "Library");
        CreatorProject saved;
        using (var ui = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library))
        {
            var step = await FilesStepAsync(ui);
            step.UseFolder(_work.AuthorFolder);
            await UiHarness.WaitUntilAsync(() => Page(ui).Step == CreatorStep.Review);
            Page(ui).Session!.Describe(project => project with { Metadata = project.Metadata with { Title = "Lantern Isle revised" } });
            saved = Page(ui).Session!.Project;
            await UiHarness.WaitUntilAsync(() => Stored(library, saved.Id) == saved);
            await RenderBothAsync(ui, "creator-header");
        }

        File.SetLastWriteTimeUtc(Path.Combine(library, saved.Id.ToString(), "project.json"), TestStates.Now.AddDays(-1).UtcDateTime);
        using var reopened = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library);
        await UiHarness.WaitUntilAsync(() => Page(reopened).HasRecent);
        var recent = Page(reopened).Recent.Should().ContainSingle().Subject;
        (recent.Name, recent.Folder).Should().Be(("Lantern Isle", _work.AuthorFolder));
        recent.Changed.Should().Be(Text.Format(Strings.Creator_RecentChanged,
            ("when", Formats.RelativeTime(TestStates.Now.AddDays(-1), reopened.Clock))));
        await RenderBothAsync(reopened, "creator-start-projects");

        reopened.Click(Button(reopened, "Lantern Isle"));

        await UiHarness.WaitUntilAsync(() => Page(reopened).Step == CreatorStep.Review);
        Page(reopened).Session!.Project.Should().Be(saved);
    }

    [AvaloniaFact]
    public async Task A_change_made_just_before_closing_is_on_the_disk()
    {
        var library = Path.Combine(_work.Root, "Library");
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library);
        await FilesStepAsync(ui);
        var session = Page(ui).Session!;
        session.Describe(project => project with { Metadata = project.Metadata with { Title = "Lantern Isle revised" } });

        (await ui.Agent.ShutdownHandler!(ShutdownReason.Exit)).Should().BeTrue();

        Stored(library, session.Project.Id).Should().Be(session.Project);
    }

    [AvaloniaFact]
    public async Task A_change_after_a_refused_close_is_saved_too()
    {
        var library = Path.Combine(_work.Root, "Library");
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library);
        await FilesStepAsync(ui);
        var session = Page(ui).Session!;
        var closing = ui.Shell.CloseFlow.DecideAsync();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel { IsClosing: false });
        Stored(library, session.Project.Id).Should().Be(session.Project, "the close flushed before it asked what closing means");
        ui.Shell.Dialogs.CancelCurrent();
        (await closing).Should().BeFalse();

        session.Describe(project => project with { Metadata = project.Metadata with { Title = "Lantern Isle revised" } });

        await UiHarness.WaitUntilAsync(() => Stored(library, session.Project.Id) == session.Project);
    }

    [AvaloniaFact]
    public async Task A_change_made_during_the_flush_is_saved_too()
    {
        var store = new HeldStore();
        await using var session = new CreatorSession(UiHarness.Context().Creator, new CreatorProject(), store, NullLogger.Instance);
        session.Describe(project => project with { Name = "first" });

        var flushing = session.FlushAsync();
        await store.Saving.WaitAsync(UiHarness.TimeLimit);
        session.Describe(project => project with { Name = "second" });
        store.Release();

        (await flushing).Should().BeTrue();
        store.Saved.Select(project => project.Name).Should().Equal("first", "second");
    }

    [AvaloniaFact]
    public async Task A_save_that_failed_is_told_and_the_next_change_saves_again()
    {
        var library = Path.Combine(_work.Root, "Library");
        using var ui = await OpenAsync(TestStates.Idle(creatorMode: true), creatorDirectory: library);
        await FilesStepAsync(ui);
        var session = Page(ui).Session!;
        var file = Path.Combine(library, session.Project.Id.ToString(), "project.json");
        await UiHarness.WaitUntilAsync(() => File.Exists(file));
        var name = (string title) => session.Describe(project => project with { Metadata = project.Metadata with { Title = title } });

        // A folder in the file's place refuses the save, as a disk that is full or read-only does.
        File.Delete(file);
        Directory.CreateDirectory(file);
        name("refused");
        await UiHarness.WaitUntilAsync(() => session.SaveFailed);
        ui.Find<Grid>("SaveFailedLine").IsEffectivelyVisible.Should().BeTrue();
        await RenderBothAsync(ui, "creator-save-failed");

        Directory.Delete(file);
        name("saved");
        await UiHarness.WaitUntilAsync(() => !session.SaveFailed && Stored(library, session.Project.Id) == session.Project);
        ui.Find<Grid>("SaveFailedLine").IsEffectivelyVisible.Should().BeFalse();

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
        var step = await FilesStepAsync(ui);
        step.UseFolder(_work.AuthorFolder);
        await UiHarness.WaitUntilAsync(() => step.FileLine.Length > 0);

        var closing = ui.Shell.CloseFlow.DecideAsync();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current is ChoiceDialogViewModel { IsClosing: false });
        ((ChoiceDialogViewModel)ui.Shell.Dialogs.Current!).Title.Should().Be(Strings.Shell_CloseTitle, "only what closing means is asked");
        ui.Shell.Dialogs.CancelCurrent();
        (await closing).Should().BeFalse();
        step.IsComparing.Should().BeTrue();

        files.Reads.Set();
        await UiHarness.WaitUntilAsync(() => Page(ui).Step == CreatorStep.Review);
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

        await UiHarness.WaitUntilAsync(() => Page(ui).HasRecent);
        Page(ui).Recent.Should().ContainSingle().Which.Name.Should().Be("Good");
        ui.Agent.Requests.OfType<ForgetRecentCreatorProjects>().Should().ContainSingle().Which.Paths.Should().Equal(good, damaged);
        Page(ui).ProblemText.Should().Contain("Damaged.exampleproj").And.Contain(Text.Format(Strings.Creator_ImportWaiting, ("count", 1)));
        File.ReadAllText(damaged).Should().Be("{ not a project");
        await RenderBothAsync(ui, "creator-start-imported");

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

        await FilesStepAsync(ui);
        ui.Unnamed().Should().BeEmpty();
        Visible(ui, Button(ui, Strings.Creator_PickFolder)).Should().BeTrue();
        await RenderAsync(ui, "creator-files-small");
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

    private static async Task RenderBothAsync(UiHarness ui, string name)
    {
        await RenderAsync(ui, name);
        var (width, height) = (ui.Window.Width, ui.Window.Height);
        (ui.Window.Width, ui.Window.Height) = (960, 600);
        await RenderAsync(ui, name + "-960");
        (ui.Window.Width, ui.Window.Height) = (width, height);
        UiHarness.Settle();
    }

    // The project as a window opening it now would read it; null while its file is not there.
    private static CreatorProject? Stored(string library, Guid id)
    {
        using var projects = new ProjectLibrary(library, TimeProvider.System);
        return projects.Store(id).LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private static CreatorPageViewModel Page(UiHarness ui) => (CreatorPageViewModel)ui.Shell.Navigator.Current!;

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
