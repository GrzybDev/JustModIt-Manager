using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Onboarding;
using JustModIt.Manager.Ui.Shell;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class ShellTests
{
    [AvaloniaFact]
    public async Task The_window_opens_on_the_players_games_with_a_sidebar_screen_readers_can_name()
    {
        using var ui = new UiHarness(TestStates.Idle());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        ui.Window.Title.Should().Be("Example App");
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute());
        NavigationButtons(ui).Select(Name).Should().Equal(Strings.Shell_Library, Strings.Shell_Catalogue, Strings.Shell_Settings);
        ui.Shell.Primary[0].IsActive.Should().BeTrue();
        ui.Find<Button>("BackButton").IsVisible.Should().BeFalse();
        ui.Shell.Status.AccessibleText.Should().Be($"{Text.Format(Strings.Shell_StatusVersion, ("version", "0.1.0"))} {Strings.Shell_StatusUpToDate}");
        ui.Render("shell-library");
    }

    [AvaloniaFact]
    public void Creator_mode_brings_the_package_creator_and_turning_it_off_leaves_its_page()
    {
        using var ui = new UiHarness(TestStates.Idle());

        ui.Push(TestStates.Idle(creatorMode: true));
        NavigationButtons(ui).Select(Name).Should().Contain(Strings.Shell_Creator);
        ui.Click(NavigationButtons(ui).Single(button => Name(button) == Strings.Shell_Creator));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CreatorRoute());

        ui.Push(TestStates.Idle(creatorMode: false));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute());
        NavigationButtons(ui).Select(Name).Should().NotContain(Strings.Shell_Creator);
    }

    [AvaloniaFact]
    public async Task The_status_card_follows_a_job_and_offers_the_apps_ready_update()
    {
        using var ui = new UiHarness(TestStates.Idle());
        var status = ui.Shell.Status;

        ui.Push(TestStates.Idle().With(TestStates.Job()));
        (status.Title, status.Detail, status.Progress, status.IsBusy).Should()
            .Be((Strings.Shell_JobInstalling, "Ember Hollow · 42%", 42.0, true));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("status-job");

        ui.Agent.Progress(TestStates.Job(fraction: 0.8, sequence: 2));
        UiHarness.Settle();
        (status.Detail, status.Progress).Should().Be(("Ember Hollow · 80%", 80.0));

        ui.Push(TestStates.Idle().With(TestStates.Job(stage: JobStage.Waiting, fraction: null) with { WaitReason = JobWaitReason.GameRunning }));
        (status.Title, status.ShowsProgress, status.IsBusy).Should().Be((Strings.Shell_JobWaitingForGame, false, false));

        ui.Push(TestStates.Idle().With(new AppUpdateView { State = AppUpdateState.Ready, CurrentVersion = "0.1.0", Version = "0.2.0" }));
        var restart = ui.Find<Button>().Single(button => button.IsEffectivelyVisible && Name(button) == Strings.Shell_StatusRestart);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("status-update-ready");
        ui.Click(restart);
        ui.Agent.Requests.Should().ContainSingle().Which.Should().BeOfType<ApplyAppUpdate>();
    }

    [AvaloniaFact]
    public async Task The_smallest_window_keeps_the_sidebar_and_its_status_in_view()
    {
        using var ui = new UiHarness(TestStates.Idle(creatorMode: true).With(TestStates.Job()), width: 960, height: 600,
            context: context => context with { WindowFrames = new TitleBar(31) });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        (ui.Window.MinWidth, ui.Window.MinHeight).Should().Be((960, 569),
            "the system adds its own title bar to the smallest window, which draws its title bar inside its content");

        var window = new Rect(ui.Window.Bounds.Size);
        foreach (var control in NavigationButtons(ui).Cast<Control>().Append(ui.Find<StatusCard>().Single()))
        {
            var place = control.TranslatePoint(new Point(), ui.Window);
            place.Should().NotBeNull();
            window.Contains(new Rect(place!.Value, control.Bounds.Size)).Should().BeTrue();
        }

        ui.Render("shell-minimum");
    }

    [AvaloniaFact]
    public async Task A_window_whose_settings_chose_the_systems_title_bar_opens_in_the_systems_frame_and_keeps_its_header()
    {
        using var ui = new UiHarness(context: context => context with { Frame = WindowFrame.System });
        var frames = new List<WindowFrame>();
        ui.Shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.Frame))
            {
                frames.Add(ui.Shell.Frame);
            }
        };
        (ui.Window.WindowDecorations, CaptionButtons(ui).Count).Should().Be((WindowDecorations.Full, 0),
            "the settings file tells the frame before the agent's first state");

        var state = TestStates.Idle();
        ui.Push(state with { Settings = state.Settings with { SystemTitleBar = true } });
        ui.Shell.Navigator.Navigate(new UndetectedGamesRoute());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        frames.Should().BeEmpty("the window never shows one frame and then the other");
        (ui.Shell.Frame, ui.Window.WindowDecorations).Should().Be((WindowFrame.System, WindowDecorations.Full));
        (Grips(ui).Count, Outline(ui).IsEffectivelyVisible).Should().Be((0, false), "the system's frame resizes and marks the window");
        ui.Find<Button>("BackButton").IsEffectivelyVisible.Should().BeTrue();
        ui.Find<TextBlock>().Should().Contain(text => text.Classes.Contains("wordmark") && text.IsEffectivelyVisible);
        ui.Window.WindowState.Should().Be(WindowState.Normal);
        ui.Render("shell-system-frame");
    }

    [AvaloniaFact]
    public async Task Without_the_systems_frame_the_window_draws_its_bar_edges_and_an_outline_that_fades_with_its_focus()
    {
        // Mapped behind another window, as focus-stealing prevention leaves it, so it never hears of a focus it never had.
        using var ui = new UiHarness(TestStates.Idle(), context: context => context with { Frame = WindowFrame.Frameless },
            activated: false);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        ui.Window.WindowDecorations.Should().Be(WindowDecorations.None);
        CaptionButtons(ui).Select(Name).Should().Equal(Strings.Shell_Minimize, Strings.Shell_Maximize, Strings.Shell_Close);
        CaptionButtons(ui).Should().OnlyContain(button => !button.IsTabStop, "the system's own keys move and close the window");
        Grips(ui).Select(grip => grip.Edge).Should().BeEquivalentTo(Enum.GetValues<WindowEdge>());
        (GripAt(ui, 0, 0), GripAt(ui, 15, 15), GripAt(ui, 640, 5), GripAt(ui, 1274, 400), GripAt(ui, 1279, 799), GripAt(ui, 1257, 3))
            .Should().Be((WindowEdge.NorthWest, WindowEdge.NorthWest, WindowEdge.North, WindowEdge.East, WindowEdge.SouthEast,
                WindowEdge.North), "the edges lie over everything, the top one over the caption buttons' top too");
        (ui.Window.InputHitTest(new Point(1257, 20)) as Visual)?.FindAncestorOfType<Button>(includeSelf: true).Should()
            .BeSameAs(ui.Find<Button>("CloseButton"), "below the edge the caption button takes the pointer");
        (ui.Window.IsActive, Outline(ui).IsEffectivelyVisible, OutlineColor(ui)).Should().Be((false, true, Color.Parse("#12FFFFFF")),
            "the outline of an inactive window fades to 7% and never takes the accent");
        CaptionColors(ui).Should().AllBeEquivalentTo(Color.Parse("#A1A1AA"), "the caption icons dim as the desktop's own windows' do");
        ui.Render("shell-own-frame-inactive");

        ui.Window.Activate();
        UiHarness.Settle();
        (ui.Window.IsActive, OutlineColor(ui)).Should().Be((true, Color.Parse("#24FFFFFF")),
            "KWin shadows only decorated windows, so an outline of 14% white marks the active one");
        CaptionColors(ui).Should().AllBeEquivalentTo(Color.Parse("#D4D4D8"));
        ui.Window.SetRenderScaling(1.5);
        UiHarness.Settle();
        (Outline(ui).BorderThickness.Top * 1.5).Should().BeApproximately(1, 0.001,
            "the outline stays one device pixel at any display scale");
        ui.Window.SetRenderScaling(1);
        UiHarness.Settle();
        ui.Render("shell-own-frame");

        ui.Window.MouseMove(new Point(1257, 20));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("shell-own-frame-close-hover");
        ui.Window.MouseMove(new Point(700, 300));

        // Shown again without being brought to the front, as another window on top leaves it.
        ui.Window.Hide();
        UiHarness.Settle();
        ui.Window.Show();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        (ui.Window.IsActive, OutlineColor(ui)).Should().Be((false, Color.Parse("#12FFFFFF")));
        CaptionColors(ui).Should().AllBeEquivalentTo(Color.Parse("#A1A1AA"));
        ui.Window.Activate();
        UiHarness.Settle();
        (ui.Window.IsActive, OutlineColor(ui)).Should().Be((true, Color.Parse("#24FFFFFF")));

        DoubleClick(ui, new Point(700, 20));
        ui.Window.WindowState.Should().Be(WindowState.Maximized);
        Grips(ui).Should().BeEmpty("a maximized window has no edges to drag, so the close button in the screen's corner closes it");
        Outline(ui).IsEffectivelyVisible.Should().BeFalse();
        Name(ui.Find<Button>("MaximizeButton")).Should().Be(Strings.Shell_Restore);
        ui.Window.MouseMove(new Point(ui.Window.ClientSize.Width - 69, 20));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("shell-own-frame-maximized");

        DoubleClick(ui, new Point(700, 20));
        ui.Window.WindowState.Should().Be(WindowState.Normal);
        Grips(ui).Should().HaveCount(8);
    }

    [AvaloniaFact]
    public async Task The_players_choice_of_the_systems_title_bar_switches_the_frame_at_once_both_ways_and_leaves_an_unreported_frame_where_it_was()
    {
        var state = TestStates.Idle();
        using var ui = new UiHarness(state, context: context => context with { Frame = WindowFrame.Frameless });
        ui.Window.Position = new PixelPoint(120, 80);
        UiHarness.Settle();

        ui.Push(state with { Version = 2, Settings = state.Settings with { SystemTitleBar = true } });
        (ui.Window.WindowDecorations, ui.Shell.Frame).Should().Be((WindowDecorations.Full, WindowFrame.System));
        CaptionButtons(ui).Should().BeEmpty();
        Grips(ui).Should().BeEmpty();
        Outline(ui).IsEffectivelyVisible.Should().BeFalse();
        ui.Find<TextBlock>().Should().Contain(text => text.Classes.Contains("wordmark") && text.IsEffectivelyVisible);
        (ui.Window.Position, ui.Window.ClientSize).Should().Be((new PixelPoint(120, 80), new Size(1280, 800)),
            "a system that reports no frame of its own leaves nothing to make room for");
        DoubleClick(ui, new Point(700, 20));
        ui.Window.WindowState.Should().Be(WindowState.Normal, "the system's frame maximizes the window from its own title bar");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("shell-own-frame-switched-to-system");

        ui.Push(state with { Version = 3 });
        (ui.Window.WindowDecorations, ui.Shell.Frame).Should().Be((WindowDecorations.None, WindowFrame.Frameless));
        CaptionButtons(ui).Should().HaveCount(3);
        Grips(ui).Should().HaveCount(8);
        (ui.Window.Position, ui.Window.ClientSize).Should().Be((new PixelPoint(120, 80), new Size(1280, 800)));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("shell-system-frame-switched-to-own");
    }

    [Fact]
    public void The_systems_title_bar_takes_its_height_from_the_pages_unless_they_would_go_under_the_smallest_window()
    {
        var smallest = new Size(960, 600);
        var breeze = new Size(0, 30);

        MainWindow.ClientSizeWithin(new Size(1280, 800), breeze, smallest).Should().Be(new Size(1280, 770));
        MainWindow.ClientSizeWithin(new Size(960, 615), breeze, smallest).Should().Be(new Size(960, 600),
            "the window grows by what is missing");
        MainWindow.ClientSizeWithin(new Size(960, 600), new Size(8, 34), smallest).Should().Be(smallest);
        MainWindow.ClientSizeWithin(new Size(1280, 830), default, smallest).Should().Be(new Size(1280, 830),
            "without the system's frame the pages take the whole outer frame back");
    }

    [AvaloniaFact]
    public void Windows_own_frame_ignores_the_title_bar_setting_and_keeps_its_caption_icons_whatever_the_focus()
    {
        var state = TestStates.Idle();
        using var ui = new UiHarness(state with { Settings = state.Settings with { SystemTitleBar = true } }, activated: false);

        (ui.Shell.Frame, ui.Window.WindowDecorations).Should().Be((WindowFrame.Own, WindowDecorations.Full));
        CaptionButtons(ui).Should().HaveCount(3, "Windows' frame keeps its own caption buttons");
        ui.Window.IsActive.Should().BeFalse();
        CaptionColors(ui).Should().AllBeEquivalentTo(Color.Parse("#D4D4D8"), "Windows' bar keeps its icons whatever the focus");
        Grips(ui).Should().BeEmpty("the system's frame resizes the window");
    }

    [AvaloniaFact]
    public async Task A_Game_Mode_window_fills_its_screen_and_offers_its_bar_hint_and_way_back()
    {
        // Larger than its screen, as on gamescope's screen of a Deck docked to a monitor.
        using var ui = new UiHarness(width: 4000, height: 3000, context: context => context with { Frame = WindowFrame.Screen });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        var screen = ui.Window.Screens.Primary!;
        (ui.Window.Position, ui.Window.ClientSize, ui.Window.WindowState, ui.Window.CanResize).Should().Be(
            (screen.Bounds.Position, screen.Bounds.Size.ToSize(screen.Scaling), WindowState.Normal, false),
            "gamescope resizes no window for a full-screen state and centres a larger one with its edges off the screen");
        ui.SizeToDeck();
        CaptionButtons(ui).Should().BeEmpty("gamescope shows the window full screen, with nothing to minimise");
        BarButtons(ui).Select(Name).Should().Equal([Strings.Shell_Close], "the controls wait for the pages");
        ui.Render("gamemode-connecting");

        var setup = GameMode(TestStates.Idle(creatorMode: true));
        ui.Push(setup with { Settings = setup.Settings with { OnboardingCompleted = false } });
        BarButtons(ui).Select(Name).Should().Equal(Strings.Shell_Controls, Strings.Shell_Close);
        ui.Shell.Dialogs.Current.Should().BeNull("the hint covers none of the setup's steps");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(600));
        ui.Render("gamemode-setup");
        var onboarding = ui.Shell.FirstRun.Should().BeOfType<OnboardingViewModel>().Subject;
        onboarding.NextCommand.Execute(null);
        ui.Press(Key.Escape);
        onboarding.IsFirstStep.Should().BeTrue("Esc is the controller's way back through the setup's steps too");
        ui.Press(Key.Escape);
        (ui.Shell.FirstRun, onboarding.IsFirstStep).Should().Be((onboarding, true), "the first step has nothing to go back to");

        ui.Push(setup with { Version = 2 });
        ui.Shell.Dialogs.Current.Should().BeOfType<ControlsHintDialogViewModel>("the setup is done")
            .Which.Footer.Should().NotBeNull("a hint that opened by itself says where it waits");
        await ui.RenderBothAsync("gamemode-controls");
        ui.Click(ui.Find<Button>().Single(button => button.IsEffectivelyVisible && Name(button) == Strings.Shell_Close
            && button.FindAncestorOfType<DialogLayer>() is not null));
        await UiHarness.WaitAsync(DialogHost.LeaveDuration * 2);
        ui.Agent.Requests.OfType<ChangeSettings>().Where(request => request.Settings.ControlsHintShown).Should().ContainSingle();
        ui.Push(setup with { Version = 3, Settings = setup.Settings with { ControlsHintShown = true, SystemTitleBar = true } });
        ui.Shell.Dialogs.Current.Should().BeNull("the hint showed by itself once");
        ui.Shell.Frame.Should().Be(WindowFrame.Screen, "the title bar chosen on the desktop waits for the desktop");

        NavigationButtons(ui).Select(Name).Should().NotContain(Strings.Shell_Creator, "Game Mode has no room for the creator's pickers");
        ui.Agent.Navigate(new CreatorTarget());
        UiHarness.Settle();
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute());
        await ui.RenderBothAsync("gamemode-library");

        ui.Shell.Navigator.Navigate(new GameRoute(TestStates.Ember));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("gamemode-game-page");
        ui.Press(Key.Escape);
        ui.Shell.Navigator.CurrentRoute.Should().Be(new LibraryRoute(), "Esc is the controller's way back");

        ui.Click(BarButtons(ui).Single(button => Name(button) == Strings.Shell_Controls));
        ui.Shell.Dialogs.Current.Should().BeOfType<ControlsHintDialogViewModel>("the hint stays under its button")
            .Which.Footer.Should().BeNull("the player has just pressed that button");
        ui.Press(Key.Escape);
        await UiHarness.WaitAsync(DialogHost.LeaveDuration * 2);
        ui.Shell.Dialogs.Current.Should().BeNull();
        ui.Agent.Requests.OfType<ChangeSettings>().Where(request => request.Settings.ControlsHintShown).Should()
            .ContainSingle("the hint was remembered already");

        ui.Click(BarButtons(ui).Single(button => Name(button) == Strings.Shell_Close));
        ui.Shell.Dialogs.Current.Should().BeOfType<ChoiceDialogViewModel>().Which.Title.Should().Be(Strings.Shell_CloseTitle);
    }

    [AvaloniaFact]
    public void Esc_on_the_desktop_keeps_the_page()
    {
        using var ui = new UiHarness(TestStates.Idle());

        ui.Shell.Navigator.Navigate(new GameRoute(TestStates.Ember));
        ui.Press(Key.Escape);

        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(TestStates.Ember), "a keyboard's Esc closes, it does not go back");
    }

    [AvaloniaFact]
    public void A_window_larger_than_its_screen_opens_maximized()
    {
        using var ui = new UiHarness(TestStates.Idle(), width: 4000, height: 3000);

        ui.Window.WindowState.Should().Be(WindowState.Maximized);
    }

    [AvaloniaFact]
    public void The_keyboard_reaches_every_sidebar_entry_in_order_and_opens_it()
    {
        using var ui = new UiHarness(TestStates.Idle(creatorMode: true));

        var visited = new List<string>();
        for (var step = 0; step < 6; step++)
        {
            ui.Press(Key.Tab);
            if (ui.Window.FocusManager?.GetFocusedElement() is Button { } focused && focused.Classes.Contains("nav"))
            {
                visited.Add(Name(focused));
            }
        }

        visited.Should().StartWith([Strings.Shell_Library, Strings.Shell_Catalogue, Strings.Shell_Creator, Strings.Shell_Settings]);
        for (var step = 0; step < 6 && (ui.Window.FocusManager?.GetFocusedElement() is not Button { } button || Name(button) != Strings.Shell_Catalogue); step++)
        {
            ui.Press(Key.Tab, RawInputModifiers.Shift);
        }

        ui.Press(Key.Enter);
        ui.Shell.Navigator.CurrentRoute.Should().Be(new CatalogueRoute());
    }

    private static List<Button> NavigationButtons(UiHarness ui) =>
        [.. ui.Find<Button>().Where(button => button.Classes.Contains("nav") && button.IsEffectivelyVisible)];

    private static List<Button> CaptionButtons(UiHarness ui) =>
        [.. ui.Find<Button>().Where(button => button.Classes.Contains("caption") && button.IsEffectivelyVisible)];

    private static List<ResizeGrip> Grips(UiHarness ui) => [.. ui.Find<ResizeGrip>().Where(grip => grip.IsEffectivelyVisible)];

    private static WindowEdge? GripAt(UiHarness ui, double x, double y) =>
        ui.Window.InputHitTest(new Point(x, y)) is ResizeGrip grip ? grip.Edge : null;

    private static Border Outline(UiHarness ui) => ui.Find<Border>("Outline");

    private static Color OutlineColor(UiHarness ui) => ((ISolidColorBrush)Outline(ui).BorderBrush!).Color;

    private static List<Color> CaptionColors(UiHarness ui) =>
        [.. CaptionButtons(ui).Select(button => ((ISolidColorBrush)button.Foreground!).Color)];

    // The headless system runs the dispatcher and renders a frame around every press, which a busy machine can stretch past
    // the double-click time, so the pointer clicks on until a click counts as a double click's second. A click beside the
    // point first ends a count that an earlier double click there left running.
    private static void DoubleClick(UiHarness ui, Point point)
    {
        var doubled = false;
        EventHandler<PointerPressedEventArgs> count = (_, e) => doubled |= e.ClickCount == 2;
        var beside = new Point(point.X + 10, point.Y);
        ui.Window.MouseMove(beside);
        ui.Window.MouseDown(beside, MouseButton.Left);
        ui.Window.MouseUp(beside, MouseButton.Left);
        ui.Window.MouseMove(point);
        ui.Window.AddHandler(InputElement.PointerPressedEvent, count, RoutingStrategies.Tunnel, handledEventsToo: true);
        for (var click = 0; !doubled && click < 20; click++)
        {
            ui.Window.MouseDown(point, MouseButton.Left);
            ui.Window.MouseUp(point, MouseButton.Left);
        }

        ui.Window.RemoveHandler(InputElement.PointerPressedEvent, count);
        doubled.Should().BeTrue("two quick clicks at one place make a double click");
        UiHarness.Settle();
    }

    private static List<Button> BarButtons(UiHarness ui) =>
        [.. ui.Find<StackPanel>("GameModeBar").GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible)];

    // What the agent of a Game Mode session sends: the window goes back to Steam's library.
    private static AppStateView GameMode(AppStateView state) =>
        state with { Activity = state.Activity with { WindowReturn = WindowReturn.StoreLibrary } };

    private static string Name(Control control) => ControlAutomationPeer.CreatePeerForElement(control).GetName();

    private sealed class TitleBar(int height) : IWindowFrames
    {
        public int TitleBarHeight(nint window) => height;
    }
}
