using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
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

    private static string Name(Control control) => ControlAutomationPeer.CreatePeerForElement(control).GetName();

    private sealed class TitleBar(int height) : IWindowFrames
    {
        public int TitleBarHeight(nint window) => height;
    }
}
