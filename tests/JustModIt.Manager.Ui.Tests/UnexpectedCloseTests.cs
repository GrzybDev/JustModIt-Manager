using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class UnexpectedCloseTests
{
    private static readonly UncleanExitView Crash = new("0.1.0", "System.InvalidOperationException");

    private static readonly UncleanExitView Reported = Crash with
    {
        CrashedAt = TestStates.Now.AddMinutes(-20),
        Report = new CrashReportView(
            [
                new CrashError("System.InvalidOperationException", "Collection was modified; enumeration operation may not execute.",
                    "   at Example.State.StateCoordinator.Publish() + 0x4c\n   at Example.State.StateCoordinator.RunAsync() + 0x1f2"),
            ],
            ["14:31:58 INF Example.Library: Scanned steam: 12 games", "14:32:06 INF Example.Catalog: Catalogue refreshed in 2.4 s",
                "14:32:07 WRN Example.Library: Library %USERPROFILE%\\Games offline"]),
    };

    [AvaloniaFact]
    public async Task An_unexpected_close_is_told_once_with_a_report_to_save_and_a_problem_to_report()
    {
        var state = TestStates.Idle() with { UncleanExit = Crash };
        using var ui = new UiHarness(state, context: context => context with { IssuesAddress = new Uri("https://code.example.com/issues/new") });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        var dialog = ui.Shell.Dialogs.Current.Should().BeOfType<UncleanExitDialogViewModel>().Subject;
        ui.Agent.Requests.OfType<DismissUncleanExit>().Should().ContainSingle("the next window opening does not tell it again");
        dialog.Title.Should().Be("Example App został nieoczekiwanie zamknięty");
        dialog.Detail.Should().Be("Przyczyna: błąd System.InvalidOperationException w wersji 0.1.0.");
        ui.Push(state with { Version = 2 });
        ui.Shell.Dialogs.Current.Should().BeSameAs(dialog, "a state sent before the agent forgot the exit opens no second dialog");
        ui.Unnamed().Should().BeEmpty();
        ui.Render("unexpected-close");

        ui.Pickers.SaveFile = @"D:\Reports\report.zip";
        ui.Click(ui.Find<Button>("SaveReport"));
        ui.Pickers.Suggested.Should().Equal("Example App – raport 2026-09-29.zip");
        ui.Agent.Requests.OfType<SaveDiagnosticReport>().Should().Equal(new SaveDiagnosticReport(@"D:\Reports\report.zip"));
        dialog.SaveResult.Should().Be("Raport zapisano jako report.zip.");
        ui.Agent.Answer = request => request is SaveDiagnosticReport ? throw new RequestFailedException(ErrorCode.Failed) : null;
        ui.Click(ui.Find<Button>("SaveReport"));
        dialog.SaveResult.Should().Be(Strings.Shell_ReportFailed);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Render("unexpected-close-failed-report");

        ui.Click(ui.Find<Button>("ReportProblem"));
        ui.Links.Opened.Should().ContainSingle().Which.AbsoluteUri.Should().StartWith("https://code.example.com/issues/new?body=");
        (await dialog.Result).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task A_crash_report_shows_what_goes_and_is_sent_only_when_the_player_asks()
    {
        using var ui = new UiHarness(TestStates.Idle() with { UncleanExit = Reported }, errorReports: true);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        var dialog = (UncleanExitDialogViewModel)ui.Shell.Dialogs.Current!;

        dialog.CanSendReport.Should().BeTrue();
        ui.Reports.Sent.Should().BeEmpty("nothing goes without the player's consent");
        ui.Find<Border>("ReportPreview").IsVisible.Should().BeFalse();
        ui.Click(ui.Find<Button>("ReportPreviewToggle"));
        dialog.Preview.Select(field => field.Label).Should().Equal("Aplikacja", "Proces", "System", "Kiedy", "Błąd", "Stos", "Dziennik");
        dialog.Preview[1].Value.Should().Be("działanie w tle");
        dialog.Preview[5].Value.Should().Be(string.Join(Environment.NewLine, "Example.State.StateCoordinator.Publish() + 0x4c",
            "Example.State.StateCoordinator.RunAsync() + 0x1f2"));
        ui.Unnamed().Should().BeEmpty();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("unexpected-close-report");
        (ui.Window.Width, ui.Window.Height) = (960, 600);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        var send = ui.Find<Button>("SendReport");
        send.TranslatePoint(new Point(0, send.Bounds.Height), ui.Window)!.Value.Y.Should().BeLessThan(600, "the buttons stay in the smallest window");
        ui.Render("unexpected-close-report-960");
        (ui.Window.Width, ui.Window.Height) = (1280, 800);

        ui.Reports.Number = null;
        ui.Click(ui.Find<Button>("SendReport"));
        dialog.ReportState.Should().Be(ErrorReportState.Failed);
        dialog.SendText.Should().Be("Wyślij ponownie");
        ui.Reports.Number = "7c1e9a42";
        ui.Click(ui.Find<Button>("SendReport"));
        ui.Reports.Sent.Should().HaveCount(2).And.OnlyContain(report => report.Errors[0].Type == "System.InvalidOperationException");
        dialog.ReportState.Should().Be(ErrorReportState.Sent);
        dialog.OutcomeDetail.Should().Be("Numer raportu: 7c1e9a42. Warto go podać, zgłaszając problem.");
        ui.Find<Button>("SendReport").IsVisible.Should().BeFalse();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("unexpected-close-report-sent");

        ui.Click(ui.Find<ToggleSwitch>("SendReportsAutomatically"));
        ui.Click(ui.Find<ToggleSwitch>("SendReportsAutomatically"));
        ui.Agent.Requests.OfType<ChangeSettings>().Select(change => change.Settings.SendErrorReports).Should()
            .Equal([true, false], "turning it back before the agent's state has the first turn is a change of its own");
    }

    [AvaloniaFact]
    public async Task With_reports_sent_automatically_the_windows_own_crash_is_reported_at_once()
    {
        var state = TestStates.Idle() with { Settings = TestStates.Idle().Settings with { SendErrorReports = true } };
        using var ui = new UiHarness(state, context: context => context with { PreviousCrash = Reported with { IsWindow = true } },
            errorReports: true);
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        var dialog = (UncleanExitDialogViewModel)ui.Shell.Dialogs.Current!;

        dialog.Title.Should().Be("Okno Menadżera Spolszczeń zostało nieoczekiwanie zamknięte");
        ui.Agent.Requests.OfType<DismissUncleanExit>().Should().BeEmpty("the agent's exit is not the one told");
        ui.Reports.Sent.Should().ContainSingle().Which.IsWindow.Should().BeTrue();
        dialog.ReportState.Should().Be(ErrorReportState.SentAutomatically);
        dialog.Outcome.Should().Be("Raport o błędzie wysłano automatycznie.");
        dialog.SendsAutomatically.Should().BeTrue();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("unexpected-close-window-reported");
    }

    [AvaloniaFact]
    public async Task An_agent_that_crashes_while_the_window_is_open_is_told_by_that_window()
    {
        using var ui = new UiHarness(TestStates.Idle());
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));
        ui.Shell.Dialogs.Current.Should().BeNull();

        ui.Push(TestStates.Idle() with { Version = 2, UncleanExit = Crash });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(300));

        ui.Shell.Dialogs.Current.Should().BeOfType<UncleanExitDialogViewModel>();
    }

    [AvaloniaFact]
    public async Task A_run_that_was_stopped_from_outside_says_so_and_without_a_place_for_reports_offers_none()
    {
        using var ui = new UiHarness(TestStates.Idle() with { UncleanExit = new UncleanExitView("0.1.0", null) });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        var dialog = (UncleanExitDialogViewModel)ui.Shell.Dialogs.Current!;
        dialog.Detail.Should().Be(Text.Format(Strings.Shell_UncleanExitKilled, ("version", "0.1.0")));
        ui.Find<Button>("ReportProblem").IsVisible.Should().BeFalse();
        ui.Find<Border>("ErrorReport").IsVisible.Should().BeFalse("a kill leaves no error to report");
        ui.Click(ui.Find<Button>("CloseDialog"));
        (await dialog.Result).Should().BeFalse();
    }
}
