using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Tests.TestSupport;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Tests;

public sealed class OverlayTests
{
    [AvaloniaFact]
    public async Task A_question_starts_on_the_way_out_keeps_the_keyboard_inside_and_is_dismissed_by_escape_or_a_click_beside_it()
    {
        using var ui = new UiHarness(TestStates.Idle());

        var answer = Ask(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        Focused(ui).Should().Be(Strings.Dialog_Cancel);
        ShowsFocusRing(ui).Should().BeFalse("a question that follows a click shows no focus ring");
        ui.Press(Key.Tab);
        ui.Press(Key.Tab);
        Focused(ui).Should().Be(Strings.Dialog_Cancel, "Tab goes round the dialog's two buttons");
        ShowsFocusRing(ui).Should().BeTrue();
        ui.Render("dialog-confirm");
        ui.Press(Key.Escape);
        (await Within(answer)).Should().BeFalse();

        answer = Ask(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.ClickAt(new Point(700, 120));
        (await Within(answer)).Should().BeFalse();

        answer = Ask(ui);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Click(Button(ui, Remove));
        (await Within(answer)).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task Questions_asked_together_come_one_after_another()
    {
        using var ui = new UiHarness(TestStates.Idle());
        var first = new ConfirmDialogViewModel("First", "One", "Yes", Icons.Info);
        var second = new ConfirmDialogViewModel("Second", "Two", "Yes", Icons.Info);

        var answers = (ui.Shell.Dialogs.ConfirmAsync(first), ui.Shell.Dialogs.ConfirmAsync(second));
        ui.Shell.Dialogs.Current.Should().BeSameAs(first);
        first.Cancel();
        await UiHarness.WaitUntilAsync(() => ui.Shell.Dialogs.Current == second);

        second.Cancel();
        await UiHarness.WaitUntilAsync(() => !ui.Shell.Dialogs.IsOpen);
        (await Within(answers.Item1), await Within(answers.Item2)).Should().Be((false, false));
    }

    [AvaloniaFact]
    public async Task Closing_the_window_asks_and_keeps_the_app_in_the_tray_when_the_player_says_so()
    {
        using var ui = new UiHarness(TestStates.Idle());

        ui.Window.Close();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Window.IsVisible.Should().BeTrue("the window waits for the player's answer");
        ui.Render("dialog-close");
        ui.Click(Button(ui, Strings.Shell_CloseToTray));

        ui.Window.IsVisible.Should().BeFalse();
        ui.Agent.Requests.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task Closing_to_exit_remembers_the_choice_and_exits_the_app()
    {
        using var ui = new UiHarness(TestStates.Idle());

        ui.Window.Close();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Click(ui.Find<ToggleSwitch>().Single(toggle => toggle.IsEffectivelyVisible));
        ui.Click(Button(ui, Strings.Shell_CloseExit));

        ui.Window.IsVisible.Should().BeFalse();
        ui.Agent.Requests.Should().HaveCount(2);
        ui.Agent.Requests[0].Should().BeOfType<ChangeSettings>().Which.Settings.Should().BeEquivalentTo(
            TestStates.Idle().Settings with { CloseBehavior = CloseBehavior.Exit });
        ui.Agent.Requests[1].Should().Be(new ExitApp(ExitMode.Now));
    }

    [AvaloniaFact]
    public async Task Exiting_while_a_job_runs_waits_for_it_only_when_the_player_agrees()
    {
        var state = TestStates.Idle().With(TestStates.Job()) with
        {
            Settings = TestStates.Idle().Settings with { CloseBehavior = CloseBehavior.Exit },
        };
        using var ui = new UiHarness(state);

        ui.Window.Close();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("dialog-exit-after-jobs");
        ui.Click(Button(ui, Strings.Dialog_Cancel));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Window.IsVisible.Should().BeTrue();
        ui.Agent.Requests.Should().BeEmpty();

        ui.Window.Close();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Click(Button(ui, Strings.Shell_ExitAfterJobs));
        ui.Window.IsVisible.Should().BeFalse();
        ui.Agent.Requests.Should().Equal(new ExitApp(ExitMode.AfterJobs));
    }

    [AvaloniaFact]
    public void Closing_with_the_tray_chosen_for_good_asks_nothing()
    {
        using var ui = new UiHarness(TestStates.Idle() with
        {
            Settings = TestStates.Idle().Settings with { CloseBehavior = CloseBehavior.MinimizeToTray },
        });

        ui.Window.Close();
        UiHarness.Settle();

        ui.Window.IsVisible.Should().BeFalse();
        ui.Agent.Requests.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task Notices_in_front_show_in_the_corner_and_lead_to_their_game()
    {
        using var ui = new UiHarness(TestStates.Idle());

        ui.Agent.Notice(new NoticeView(NoticeKind.Installed) { Game = TestStates.Ember, GameName = "Ember Hollow", TeamName = "Lantern Team" });
        ui.Agent.Notice(new NoticeView(NoticeKind.RepairNeeded) { Game = TestStates.Ember, GameName = "Ember Hollow" });
        ui.Agent.Notice(new NoticeView(NoticeKind.Failed)
        {
            Game = TestStates.Ember,
            GameName = "Ember Hollow",
            Job = JobKind.Update,
            Failure = new JobFailureView(JobFailureReason.NotEnoughSpace)
            {
                Location = @"D:\Games\Ember Hollow",
                RequiredBytes = 1_717_986_918,
                AvailableBytes = 966_367_642,
            },
        });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));

        ui.Shell.Toasts.Items.Select(toast => toast.Title).Should().Equal(Strings.Toast_InstalledTitle, Strings.Toast_RepairNeededTitle,
            Text.Format(Strings.Toast_FailedTitle, ("job", "Update")));
        ui.Shell.Toasts.Items[0].Message.Should().Be(Text.Format(Strings.Toast_Installed, ("game", "Ember Hollow"), ("team", "Lantern Team")));
        ui.Shell.Toasts.Items[0].HasAction.Should().BeFalse("nothing waits on the game once it is installed");
        ui.Shell.Toasts.Items[2].Message.Should().Be("Ember Hollow – Za ma\u0142o miejsca na dysku D: (potrzeba 1,6\u00A0GB, wolne 921,6\u00A0MB).",
            "a failure says why in the words of the game's page");
        ui.Render("toasts");

        ui.Click(ui.Find<Button>().Last(button => button.IsEffectivelyVisible && Name(button) == Strings.Toast_ShowGame));
        ui.Shell.Navigator.CurrentRoute.Should().Be(new GameRoute(TestStates.Ember));
    }

    [AvaloniaFact]
    public async Task At_most_three_notifications_show_and_each_leaves_unless_the_pointer_rests_on_it()
    {
        var toasts = new ToastHost(TimeSpan.FromMilliseconds(300));
        var shown = Enumerable.Range(1, 4).Select(index => new ToastViewModel(ToastTone.Info, Icons.Info, $"Notice {index}", null)).ToList();

        shown.ForEach(toasts.Show);
        shown[0].IsClosing.Should().BeTrue("a fourth notification sends the oldest away");
        shown[3].IsHeld = true;
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(800));

        toasts.Items.Should().Equal(shown[3]);
        shown[3].IsHeld = false;
        await UiHarness.WaitUntilAsync(() => toasts.Items.Count == 0);
    }

    private const string Remove = "Remove";

    private static Task<T> Within<T>(Task<T> answer) => answer.WaitAsync(TimeSpan.FromSeconds(5));

    private static Task<bool> Ask(UiHarness ui) => ui.Shell.Dialogs.ConfirmAsync(new ConfirmDialogViewModel(
        "Remove the project?", "The game gets its original files back; the project can be installed again at any time.", Remove,
        Icons.Trash, DialogTone.Danger)
    {
        ConfirmIcon = Icons.Trash,
    });

    private static Button Button(UiHarness ui, string name) =>
        ui.Find<Button>().Single(button => button.IsEffectivelyVisible && Name(button) == name);

    private static string? Focused(UiHarness ui) =>
        ui.Window.FocusManager?.GetFocusedElement() is Control focused ? Name(focused) : null;

    private static bool ShowsFocusRing(UiHarness ui) =>
        ui.Window.FocusManager?.GetFocusedElement() is Control focused && focused.Classes.Contains(":focus-visible");

    private static string Name(Control control) => ControlAutomationPeer.CreatePeerForElement(control).GetName();
}
