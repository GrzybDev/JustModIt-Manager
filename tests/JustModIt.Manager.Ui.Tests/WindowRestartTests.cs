using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Shell;
using JustModIt.Manager.Ui.Tests.TestSupport;

namespace JustModIt.Manager.Ui.Tests;

public sealed class WindowRestartTests
{
    private static readonly string OtherCopy = Path.Join(Path.GetTempPath(), "Example Apps", "ExampleApp.AppImage");

    [AvaloniaFact]
    public async Task A_window_whose_agent_runs_another_version_closes_to_restart_once_its_pages_agree()
    {
        List<WindowRestart> restarts = [];
        var page = new WorkingPage { Busy = true, MayClose = false };
        using var ui = new UiHarness(TestStates.Idle(), compose: app => app.Pages.Register<LibraryRoute>((_, _) => page),
            context: context => context with { RestartWindow = restarts.Add });

        ui.Agent.AgentVersion = "0.2.0";
        UiHarness.Settle();
        page.Busy = false;
        UiHarness.Settle();
        (ui.Shell.ShowsPages, restarts.Count).Should().Be((true, 0), "a build ending leaves the pages in front of the author");

        // The window comes to the front, and a page keeps the author's unsaved work.
        ui.Shell.OnWindowActivated();
        UiHarness.Settle();
        (ui.Window.IsVisible, restarts.Count).Should().Be((true, 0));

        ui.Agent.SaidRestarting.Should().BeFalse();

        page.MayClose = true;
        ui.Shell.OnWindowActivated();
        await UiHarness.WaitUntilAsync(() => !ui.Window.IsVisible);
        restarts.Should().ContainSingle().Which.Should().Match<WindowRestart>(restart =>
            restart.Version == "0.2.0" && restart.Placement!.Width == 1280 && restart.Placement.Height == 800 && !restart.Placement.Maximized);
        ui.Agent.SaidRestarting.Should().BeTrue("the agent takes the window's going for no close of the player's only on its word");
    }

    [AvaloniaFact]
    public async Task A_window_tells_its_own_crash_before_it_starts_anew()
    {
        List<WindowRestart> restarts = [];
        using var ui = new UiHarness(context: context => context with
        {
            RestartWindow = restarts.Add,
            PreviousCrash = new UncleanExitView("0.1.0", "System.InvalidOperationException") { IsWindow = true },
        });

        (ui.Agent.AgentVersion, ui.Agent.Link) = ("0.4.0", AgentLink.Incompatible);
        UiHarness.Settle();
        ui.Shell.Dialogs.Current.Should().BeOfType<UncleanExitDialogViewModel>("starting anew ends the window's session cleanly");
        restarts.Should().BeEmpty();

        ui.Shell.Dialogs.CancelCurrent();
        await UiHarness.WaitUntilAsync(() => !ui.Window.IsVisible);
        restarts.Should().ContainSingle().Which.Version.Should().Be("0.4.0");
    }

    [AvaloniaFact]
    public async Task A_window_of_another_protocol_starts_anew_as_soon_as_a_build_ends_and_says_so_meanwhile()
    {
        List<WindowRestart> restarts = [];
        var page = new WorkingPage { Busy = true };
        using var ui = new UiHarness(TestStates.Idle(), compose: app => app.Pages.Register<LibraryRoute>((_, _) => page),
            context: context => context with { RestartWindow = restarts.Add });

        (ui.Agent.AgentVersion, ui.Agent.Link) = ("0.4.0", AgentLink.Incompatible);
        UiHarness.Settle();
        (ui.Shell.ConnectingTitle, ui.Shell.ConnectingMessage, ui.Shell.IsWaiting).Should()
            .Be((Strings.Shell_UpdatingTitle, Strings.Shell_UpdatingAfterBuild, true));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(900));
        ui.Render("versions-restart-after-build");

        page.Busy = false;

        await UiHarness.WaitUntilAsync(() => !ui.Window.IsVisible);
        restarts.Should().ContainSingle().Which.Version.Should().Be("0.4.0");
    }

    [AvaloniaFact]
    public async Task A_window_that_already_restarted_for_that_version_shows_the_versions_when_their_protocols_differ()
    {
        using (var gameMode = new UiHarness(context: context => context with
        {
            Frame = WindowFrame.Screen,
            RestartWindow = _ => throw new InvalidOperationException("The window started anew for this version already."),
            Restarted = new WindowRestart("0.2.0"),
            ProgramFile = OtherCopy,
        }))
        {
            gameMode.SizeToDeck();
            (gameMode.Agent.AgentVersion, gameMode.Agent.Link) = ("0.2.0", AgentLink.Incompatible);
            UiHarness.Settle();

            (gameMode.Shell.ConnectingTitle, gameMode.Shell.ShowsStartAgent, gameMode.Shell.ShowsShortcutHint).Should()
                .Be((Strings.Shell_VersionMismatchTitle, false, true), "starting the agent again would bring back the newer version");
            gameMode.Shell.MismatchCopy.Should().Contain("ExampleApp.AppImage");
            await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(900));
            gameMode.Render("versions-older-window-game-mode");
        }

        // A copy that cannot start anew shows the versions once the update it waits for does not come.
        using var windows = new UiHarness();
        (windows.Agent.AgentVersion, windows.Agent.Link) = ("0.2.0", AgentLink.Incompatible);
        UiHarness.Settle();
        windows.Shell.ConnectingTitle.Should().Be(Strings.Shell_UpdatingTitle);
        windows.Clock.Advance(ShellViewModel.SlowConnection);
        UiHarness.Settle();
        (windows.Shell.ConnectingTitle, windows.Shell.ShowsNoAnswer).Should().Be((Strings.Shell_VersionMismatchTitle, false));
    }

    [AvaloniaFact]
    public async Task A_window_newer_than_its_agent_offers_the_agents_restart_until_that_version_comes_back()
    {
        var answers = Channel.CreateUnbounded<bool>();
        using var ui = new UiHarness(context: context => context with
        {
            RestartWindow = _ => throw new InvalidOperationException("The window started anew for this version already."),
            Restarted = new WindowRestart("0.0.9"),
            AgentRunsAsUnit = true,
            ProgramFile = OtherCopy,
            StartAgent = () => answers.Reader.ReadAsync().AsTask().GetAwaiter().GetResult(),
        });
        (ui.Agent.AgentVersion, ui.Agent.Link) = ("0.0.9", AgentLink.Incompatible);
        UiHarness.Settle();
        (ui.Shell.ShowsStartAgent, ui.Shell.MismatchCopy).Should().Be((true, null));
        ui.Find<Button>("StartAgentButton").IsFocused.Should().BeTrue();
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(900));
        ui.Render("versions-newer-window");

        ui.Shell.StartAgentCommand.Execute(null);
        answers.Writer.TryWrite(false);
        await UiHarness.WaitUntilAsync(() => ui.Shell.ShowsRefusal);
        (ui.Shell.ConnectingTitle, ui.Shell.StartAgentText, ui.Shell.MismatchCopy).Should()
            .Be((Strings.Shell_VersionMismatchTitle, Strings.Library_Retry, null), "the agent did not start again, so nothing came back");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("versions-restart-refused");

        ui.Shell.StartAgentCommand.Execute(null);
        answers.Writer.TryWrite(true);
        await UiHarness.WaitUntilAsync(() => !ui.Shell.StartRefused);
        (ui.Agent.AgentVersion, ui.Agent.Link) = (null, AgentLink.Connecting);
        UiHarness.Settle();
        (ui.Shell.ConnectingTitle, ui.Shell.IsStartingAgent, ui.Shell.ShowsNoAnswer).Should().Be((Strings.Shell_ConnectingTitle, true, false));

        // The unit keeps its own file, which brings the same older version back.
        (ui.Agent.AgentVersion, ui.Agent.Link) = ("0.0.9", AgentLink.Incompatible);
        UiHarness.Settle();
        (ui.Shell.ShowsStartAgent, ui.Shell.MismatchCopy).Should().Be((false,
            Text.Format(Strings.Shell_VersionMismatchCopy, ("age", "other"),
                ("file", Pages.Creator.CreatorWording.Wrappable(Pages.Library.GameWording.FromHome(OtherCopy))))));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        ui.Render("versions-same-agent-came-back");
    }

    [AvaloniaFact]
    public void A_window_that_started_anew_in_the_agents_version_says_so_once()
    {
        using var ui = new UiHarness(TestStates.Idle(), context: context => context with { Restarted = new WindowRestart("0.1.0") });

        ui.Session.Services.Toasts.Items.Should().ContainSingle().Which.Title.Should()
            .Be(Text.Format(Strings.Shell_WindowRestarted, ("version", "0.1.0")));
        ui.Agent.Link = AgentLink.Connecting;
        UiHarness.Settle();
        ui.Agent.Link = AgentLink.Connected;
        UiHarness.Settle();
        ui.Session.Services.Toasts.Items.Should().ContainSingle();
    }

    [Theory]
    [InlineData("0.3.0", "0.4.0")]
    [InlineData("0.9.0", "0.10.0")]
    [InlineData("1.3.0-beta.2", "1.3.0")]
    [InlineData("1.3.0-beta.2", "1.3.0-beta.10")]
    [InlineData("1.3.0-alpha.4", "1.3.0-beta.1")]
    [InlineData("1.3.0-beta", "1.3.0-beta.1")]
    [InlineData("0.0.2-0.ci.17", "0.0.2-alpha.1")]
    public void Versions_are_ordered_as_semantic_versioning_orders_them(string older, string newer)
    {
        AppVersions.Compare(older, newer).Should().BeNegative();
        AppVersions.Compare(newer, older).Should().BePositive();
        AppVersions.Compare(newer, newer + "+3f2a9c1").Should().Be(0);
    }

    private sealed class WorkingPage : PageViewModel
    {
        public override string Title => "Working";

        public bool MayClose { get; set; } = true;

        public bool Busy
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged(nameof(IsWorking));
            }
        }

        public override bool IsWorking => Busy;

        public override Task<bool> CanCloseAsync() => Task.FromResult(MayClose);
    }
}
