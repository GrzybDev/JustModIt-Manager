using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Game;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Shell;
using JustModIt.Manager.Ui.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Ui.Tests;

public sealed class ConnectionTests
{
    private static readonly IpcOptions FastRetries = new() { RetryDelay = TimeSpan.FromMilliseconds(10), MaxRetryDelay = TimeSpan.FromMilliseconds(100) };

    [AvaloniaFact]
    public async Task The_window_waits_calmly_for_the_agent_and_says_when_the_app_is_updating()
    {
        using var ui = new UiHarness();
        ui.Agent.Link = AgentLink.Connecting;
        UiHarness.Settle();

        ui.Shell.ShowsPages.Should().BeFalse();
        ui.Find<ConnectingView>().Single().Classes.Should().Contain("shown");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(1000));
        ui.Render("connecting");
        ui.Find<Border>("ContentHost").IsVisible.Should().BeFalse("the pages faded away under the waiting view");

        ui.Agent.Link = AgentLink.Incompatible;
        UiHarness.Settle();
        ui.Shell.ConnectingTitle.Should().Be(Strings.Shell_UpdatingTitle);

        ui.Agent.Link = AgentLink.Connected;
        ui.Push(TestStates.Idle());
        ui.Shell.ShowsPages.Should().BeTrue();
        ui.Find<Border>("ContentHost").IsVisible.Should().BeTrue("the pages come back at once and fade in");
        var waiting = ui.Find<ConnectingView>().Single();
        waiting.Classes.Should().NotContain("shown");
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(500));
        waiting.IsVisible.Should().BeFalse("a faded view and its loops would go on drawing under the pages, and a screen reader would read it");
    }

    [AvaloniaFact]
    public async Task The_window_follows_a_real_agent_over_its_pipe_and_closes_when_it_asks()
    {
        var name = $"ExampleApp.UiTests.{Guid.NewGuid():N}";
        var agent = new TestAgentHandler();
        await using var server = new PipeServer(name, new Hello(Hello.CurrentProtocolVersion, "0.1.0", ProcessRole.Agent, 4242), agent,
            NullLogger.Instance, FastRetries);
        server.Start();
        await using var connection = new AgentConnection(name, "0.1.0", NullLoggerFactory.Instance, FastRetries);
        using var session = UiSession.Create(Application.Current!, UiHarness.Context(), connection);
        var window = session.CreateWindow();
        window.Show();

        var pipe = await agent.Connected.Reader.ReadAsync(TestContext.Current.CancellationToken);
        pipe.Post(new StateChanged(TestStates.Idle()));
        await UiHarness.WaitUntilAsync(() => session.Shell.ShowsPages);

        window.Activate();
        await UiHarness.WaitUntilAsync(() => agent.Received(new WindowActivity(true)));

        pipe.Post(new Navigate(new SettingsTarget()));
        await UiHarness.WaitUntilAsync(() => session.Shell.Navigator.CurrentRoute == new SettingsRoute());

        var answer = pipe.RequestAsync(new ShutdownRequested(ShutdownReason.Update), cancellationToken: TestContext.Current.CancellationToken);
        await UiHarness.WaitUntilAsync(() => answer.IsCompleted && !window.IsVisible);
        (await answer).Should().Be(new ShutdownAnswer(Accepted: true));
    }

    [AvaloniaFact]
    public async Task A_window_closing_to_exit_the_app_accepts_the_agents_request_to_close_without_its_own_thread()
    {
        var name = $"ExampleApp.UiTests.{Guid.NewGuid():N}";
        var agent = new TestAgentHandler();
        await using var server = new PipeServer(name, new Hello(Hello.CurrentProtocolVersion, "0.1.0", ProcessRole.Agent, 4242), agent,
            NullLogger.Instance, FastRetries);
        server.Start();
        await using var connection = new AgentConnection(name, "0.1.0", NullLoggerFactory.Instance, FastRetries);
        using var session = UiSession.Create(Application.Current!, UiHarness.Context(), connection);
        var window = session.CreateWindow();
        window.Show();
        var pipe = await agent.Connected.Reader.ReadAsync(TestContext.Current.CancellationToken);
        pipe.Post(new StateChanged(TestStates.Idle() with { Settings = TestStates.Idle().Settings with { CloseBehavior = CloseBehavior.Exit } }));
        await UiHarness.WaitUntilAsync(() => session.Shell.ShowsPages);

        window.Close();
        (await agent.Requests.Reader.ReadAsync(TestContext.Current.CancellationToken)).Should().Be(new ExitApp(ExitMode.Now));
        var answer = pipe.RequestAsync(new ShutdownRequested(ShutdownReason.Exit), cancellationToken: TestContext.Current.CancellationToken);

        // In the app the window's thread ends as the window closes, so here it is held up while the agent waits.
        SpinWait.SpinUntil(() => answer.IsCompleted, TimeSpan.FromSeconds(5)).Should().BeTrue("the answer does not wait for the window");
        (await answer).Should().Be(new ShutdownAnswer(Accepted: true));
        await UiHarness.WaitUntilAsync(() => !window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Every_view_shows_a_job_at_its_newest_report_until_the_state_that_ends_it()
    {
        var name = $"ExampleApp.UiTests.{Guid.NewGuid():N}";
        var agent = new TestAgentHandler();
        await using var server = new PipeServer(name, new Hello(Hello.CurrentProtocolVersion, "0.1.0", ProcessRole.Agent, 4242), agent,
            NullLogger.Instance, FastRetries);
        server.Start();
        await using var connection = new AgentConnection(name, "0.1.0", NullLoggerFactory.Instance, FastRetries);
        using var session = UiSession.Create(Application.Current!, UiHarness.Context(), connection, new FakeImages());
        var window = session.CreateWindow();
        window.Width = 1280;
        window.Height = 800;
        window.Show();
        var pipe = await agent.Connected.Reader.ReadAsync(TestContext.Current.CancellationToken);

        var game = TestGames.Available("Ember Hollow", "4100");
        var download = TestGames.Job(game, JobKind.Install, JobStage.Downloading, fraction: null) with { Sequence = 4 };
        var downloading = TestGames.State(TestGames.Busy(game, download));
        pipe.Post(new StateChanged(downloading));
        await UiHarness.WaitUntilAsync(() => session.Shell.ShowsPages);
        var library = (LibraryPageViewModel)session.Shell.Navigator.Current!;
        var card = library.GridItems.OfType<GameCardViewModel>().Single();
        var states = 0;
        connection.PropertyChanged += (_, e) => states += e.PropertyName == nameof(IAgent.State) ? 1 : 0;

        pipe.Post(new JobProgress([download with { Fraction = 0.3, BytesDone = 56_623_104, Sequence = 5 }]));
        await UiHarness.WaitUntilAsync(() => card.Progress > 0);
        library.Query = "ember";
        (card.Progress, card.IsIndeterminate, states).Should().Be((30.0, false, 0));
        // A state made before the report, e.g. when the store's library was read again, shows the download as it began.
        pipe.Post(new StateChanged(downloading with { Version = 2 }));
        await UiHarness.WaitUntilAsync(() => connection.State.Version == 2);
        connection.State.Games.Single().Job!.Fraction.Should().Be(0.3);
        (card.Progress, card.IsIndeterminate, session.Shell.Status.Progress).Should().Be((30.0, false, 30.0));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        UiHarness.Render(window, "library-downloading");

        pipe.Post(new JobProgress([download with { Fraction = 0.5, BytesDone = 94_371_840, Sequence = 6 }]));
        await UiHarness.WaitUntilAsync(() => card.Progress > 30);
        session.Shell.Navigator.Open([new LibraryRoute(), new GameRoute(game.Key)]);
        var page = (GamePageViewModel)session.Shell.Navigator.Current!;
        (page.Progress, page.IsIndeterminate).Should().Be((50.0, false));
        pipe.Post(new StateChanged(downloading with { Version = 3 }));
        await UiHarness.WaitUntilAsync(() => connection.State.Version == 3);
        (page.Progress, page.IsIndeterminate).Should().Be((50.0, false));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        UiHarness.Render(window, "game-downloading");

        pipe.Post(new StateChanged(TestGames.State(TestGames.Installed("Ember Hollow", "4100", offer: game.Project)) with { Version = 4 }));
        await UiHarness.WaitUntilAsync(() => connection.State.Version == 4);
        pipe.Post(new JobProgress([download with { Stage = JobStage.Installing, Fraction = 1, Sequence = 9 }]));
        // The window answers a ping as it reads it, so the report before it has been read by then.
        await pipe.RequestAsync(new Ping(), cancellationToken: TestContext.Current.CancellationToken);
        UiHarness.Settle();

        connection.State.Jobs.Should().BeEmpty();
        page.Step!.Kind.Should().Be(StepKind.Done);
        (card.ShowsProgress, session.Shell.Status.ShowsProgress).Should().Be((false, false));
        await UiHarness.WaitAsync(TimeSpan.FromMilliseconds(400));
        UiHarness.Render(window, "game-done-after-a-late-report");

        await pipe.RequestAsync(new ShutdownRequested(ShutdownReason.Exit), cancellationToken: TestContext.Current.CancellationToken);
        await UiHarness.WaitUntilAsync(() => !window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Unsaved_work_keeps_the_window_open_when_the_agent_asks_it_to_close()
    {
        var page = new CarefulPage();
        using var ui = new UiHarness(TestStates.Idle(), compose: app => app.Pages.Register<LibraryRoute>((_, _) => page));

        (await ui.Agent.ShutdownHandler!(ShutdownReason.Exit)).Should().BeFalse();
        UiHarness.Settle();
        ui.Window.IsVisible.Should().BeTrue();

        page.MayClose = true;
        (await ui.Agent.ShutdownHandler!(ShutdownReason.Exit)).Should().BeTrue();
        UiHarness.Settle();
        ui.Window.IsVisible.Should().BeFalse();
    }

    private sealed class CarefulPage : PageViewModel
    {
        public bool MayClose { get; set; }

        public override string Title => "Careful";

        public override Task<bool> CanCloseAsync() => Task.FromResult(MayClose);
    }

    private sealed class TestAgentHandler : IConnectionHandler
    {
        public Channel<PipeConnection> Connected { get; } = Channel.CreateUnbounded<PipeConnection>();

        private readonly List<Message> _events = [];

        public void OnConnected(PipeConnection connection) => Connected.Writer.TryWrite(connection);

        public void OnDisconnected(PipeConnection connection)
        {
        }

        public void OnEvent(PipeConnection connection, Message message)
        {
            lock (_events)
            {
                _events.Add(message);
            }
        }

        public bool Received(Message message)
        {
            lock (_events)
            {
                return _events.Contains(message);
            }
        }

        public Channel<Message> Requests { get; } = Channel.CreateUnbounded<Message>();

        public ValueTask<Message?> HandleRequestAsync(PipeConnection connection, Message request, CancellationToken cancellationToken)
        {
            Requests.Writer.TryWrite(request);
            return ValueTask.FromResult<Message?>(null);
        }
    }
}
