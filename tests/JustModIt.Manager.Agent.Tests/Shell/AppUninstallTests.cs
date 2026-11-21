using JustModIt.Manager.Agent.Shell;
using JustModIt.Manager.Agent.Tests.TestSupport;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Agent.Tests.Shell;

public sealed class AppUninstallTests : IAsyncDisposable
{
    private readonly FakeProgram _program = new();
    private readonly HeldRecoveryEngine _engine = new();
    private AgentHarness? _agent;

    private AgentHarness Agent => _agent!;

    private static AppSettings Saved { get; } = AppSettings.Default with { InstallationId = Guid.NewGuid(), StartWithSystem = true, CreatorMode = true };

    [Fact]
    public async Task Uninstalling_takes_starting_with_the_system_then_the_links_then_the_program_and_the_agent_leaves_without_the_installations_id()
    {
        var window = await StartAsync();
        (await window.StateAsync(state => state.AppUninstall is not null)).AppUninstall!.ProgramFile.Should().Be(_program.File);

        (await window.RequestAsync(new UninstallApp())).Should().BeNull("the steps run after the answer");

        (await window.NextAsync<AppUninstallProgress>()).Should().Be(new AppUninstallProgress(AppUninstallStep.Autostart));
        (await window.NextAsync<AppUninstallProgress>()).Should().Be(new AppUninstallProgress(AppUninstallStep.Integration));
        (await window.NextAsync<AppUninstallProgress>()).Should().Be(new AppUninstallProgress(AppUninstallStep.Program));
        await Agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);
        _program.RemovedAfter.Should().Be((AutostartState.Disabled, false), "the program goes last, so a failure leaves one that runs");
        Agent.Registration.Schemes.Should().BeEmpty();
        (await SettingsServiceReader.ReadAsync(Agent.Options.Locations)).Should().Be(Saved with { InstallationId = null },
            "an install of the app later is another installation for the site, and every other setting stays");
    }

    [Fact]
    public async Task A_program_that_cannot_go_puts_back_the_links_and_starting_with_the_system_and_the_agent_stays_to_try_again()
    {
        var window = await StartAsync();
        _program.Failure = new UnauthorizedAccessException();

        await window.RequestAsync(new UninstallApp());

        (await window.NextAsync<AppUninstallProgress>(progress => progress.Failure is not null)).Should().Be(new AppUninstallProgress(
            AppUninstallStep.Program, new AppUninstallFailure(AppUninstallProblem.AccessDenied, Path.GetDirectoryName(_program.File), Restored: true)));
        Agent.Registration.State.Should().Be(AutostartState.Enabled);
        Agent.Registration.Schemes.Should().ContainKey("example-app");
        Agent.Run.IsCompleted.Should().BeFalse();

        _program.Failure = null;
        await window.RequestAsync(new UninstallApp());

        (await window.NextAsync<AppUninstallProgress>(progress => progress.Step == AppUninstallStep.Program)).Failure.Should().BeNull();
        await Agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);
    }

    [Fact]
    public async Task A_running_job_holds_the_uninstall_back_since_leaving_would_stop_it_halfway()
    {
        var window = await StartAsync();
        await HeldRecoveryEngine.StartJobAsync(Agent);

        await window.Awaiting(connection => connection.RequestAsync(new UninstallApp())).Should().ThrowAsync<RequestFailedException>()
            .Where(refusal => refusal.Code == ErrorCode.Failed);

        Agent.Registration.State.Should().Be(AutostartState.Enabled);
        _program.RemovedAfter.Should().BeNull();
        _engine.Release();
    }

    [Fact]
    public async Task A_job_of_the_players_that_started_meanwhile_ends_before_the_agent_leaves_rather_than_being_cancelled()
    {
        var window = await StartAsync();
        var jobs = await Agent.ServiceAsync<JobRunner>();
        JobHandle? started = null;
        _program.Removing = () => started = jobs.Start(new TestInstallJobRequest(new GameKey(StoreId.Steam, "1000"), "Example Game",
            [Path.Combine(Agent.Directory, "example.zip")]));

        await window.RequestAsync(new UninstallApp());
        (await window.NextAsync<AppUninstallProgress>(progress => progress.Step == AppUninstallStep.Program)).Failure.Should().BeNull();
        _engine.Release();

        (await started!.Completion.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token)).Outcome.Should().NotBe(JobOutcome.Cancelled);
        await Agent.Run.WaitAsync(TimeSpan.FromSeconds(30), AgentHarness.Token);
    }

    [Fact]
    public async Task The_apps_menu_entry_hands_its_uninstall_to_the_window_as_the_place_that_asks()
    {
        var window = await StartAsync();
        var launcher = await Agent.ConnectLauncherAsync();

        await launcher.RequestAsync(new Activate([LaunchArguments.UninstallSwitch]), cancellationToken: AgentHarness.Token);

        (await window.NextAsync<Navigate>()).Target.Should().Be(new UninstallAppTarget());
        _program.RemovedAfter.Should().BeNull("only the window's question starts the uninstall");
    }

    public async ValueTask DisposeAsync()
    {
        _engine.Release();
        if (_agent is not null)
        {
            await _agent.DisposeAsync();
        }
    }

    private async Task<FakeWindow> StartAsync()
    {
        _agent = new AgentHarness
        {
            Configure = services =>
            {
                services.AddSingleton<IInstallEngine>(_engine);
                services.AddSingleton<IAppProgram>(_program);
                services.AddSingleton(new AppUninstallView(_program.File, "data", "cache", "logs"));
                services.AddSingleton<AppUninstaller>();
            },
        };
        _program.Registration = Agent.Registration;
        await Agent.SaveSettingsAsync(Saved);
        Agent.Registration.Enable();
        Agent.Registration.Register("example-app", "Example App", Agent.Options.ExecutablePath);
        Agent.Start();
        return await Agent.ConnectWindowAsync();
    }

    private sealed class FakeProgram : IAppProgram
    {
        public string File { get; } = Path.Combine(Path.GetTempPath(), "Applications", "ExampleApp.AppImage");

        public FakeShellRegistration? Registration { get; set; }

        public Exception? Failure { get; set; }

        /// <summary>Runs as the program goes, as something the agent does meanwhile.</summary>
        public Action? Removing { get; set; }

        /// <summary>Starting with the system and whether a link scheme was left when the program went; null until it did.</summary>
        public (AutostartState Autostart, bool HasScheme)? RemovedAfter { get; private set; }

        public void Remove()
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Removing?.Invoke();
            RemovedAfter = (Registration!.State, !Registration.Schemes.IsEmpty);
        }
    }
}
