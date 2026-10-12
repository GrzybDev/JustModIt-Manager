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
    public async Task A_run_that_was_stopped_from_outside_says_so_and_without_a_place_for_reports_offers_none()
    {
        using var ui = new UiHarness(TestStates.Idle() with { UncleanExit = new UncleanExitView("0.1.0", null) });
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));

        var dialog = (UncleanExitDialogViewModel)ui.Shell.Dialogs.Current!;
        dialog.Detail.Should().Be(Text.Format(Strings.Shell_UncleanExitKilled, ("version", "0.1.0")));
        ui.Find<Button>("ReportProblem").IsVisible.Should().BeFalse();
        ui.Click(ui.Find<Button>("CloseDialog"));
        (await dialog.Result).Should().BeFalse();
    }
}
