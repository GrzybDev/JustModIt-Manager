using JustModIt.Manager.Agent.Shell;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Agent.Tests.TestSupport;

/// <summary>
/// A whole agent over an empty data folder of its own, with no stores, an offline site and fakes for everything that
/// would reach the desktop: the tray, the registry entries, the window's process and the shell.
/// </summary>
internal sealed class AgentHarness : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<IAsyncDisposable> _connections = [];
    private readonly TaskCompletionSource<IServiceProvider> _services = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _run = Task.CompletedTask;

    public AgentHarness()
    {
        var identity = new AppIdentity("ExampleApp" + Guid.NewGuid().ToString("N")[..12], "ExampleVendor", "example-app",
            isDevelopmentBuild: false);
        var data = Path.Combine(Directory, "data");
        Options = new AgentOptions
        {
            Identity = identity,
            ProductName = "Example App",
            Version = "1.0.0",
            Locations = new AppDataLocations(data, Path.Combine(data, "cache"), Path.Combine(data, "logs")),
            Names = InstanceNames.ForCurrentUser(identity.InstanceName),
            Api = new CatalogueApiOptions(new Uri("https://api.example.com/"), null, "ExampleApp", "1.0.0"),
            PackageExtension = ".examplemod",
            ExecutablePath = Path.Combine(Directory, "app", "ExampleApp.exe"),
            InstallDirectory = Path.Combine(Directory, "app"),
        };
        Starter = new FakeUiStarter(this);
    }

    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>Changed by a test before it starts the agent.</summary>
    public AgentOptions Options { get; set; }

    public FakeUiStarter Starter { get; }

    public FakeForeground Foreground { get; } = new();

    public FakeTray Tray { get; } = new();

    public FakeShell Shell { get; } = new();

    public FakeShellRegistration Registration { get; } = new();

    public Task Run => _run;

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Registrations of the test's own, which replace the agent's.</summary>
    public Action<IServiceCollection>? Configure { get; init; }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        var store = new JsonDocumentStore<AppSettings>(Options.Locations.SettingsFile, SettingsFormat.Instance, TimeProvider.System,
            NullLogger.Instance);
        await store.SaveAsync(settings, Token);
    }

    public void Start(LaunchArguments? start = null)
    {
        _run = AgentHost.RunAsync(Options, start ?? new LaunchArguments(ProcessRole.Agent, Background: false, Target: null),
            NullLoggerFactory.Instance, services =>
            {
                services.RemoveAll<ILibraryProvider>();
                services.AddSingleton<ICatalogueApi>(new OfflineCatalogueApi());
                services.AddSingleton<IUiStarter>(Starter);
                services.AddSingleton<IForegroundHandoff>(Foreground);
                services.AddSingleton<ITrayHost>(Tray);
                services.AddSingleton<IDesktopShell>(Shell);
                services.AddSingleton<IAutostart>(Registration);
                services.AddSingleton<ILinkRegistration>(Registration);
                services.AddSingleton<IAgentService>(provider =>
                {
                    _services.TrySetResult(provider);
                    return new IdleService();
                });
                Configure?.Invoke(services);
            },
            _stop.Token);
    }

    /// <summary>One of the running agent's services, for a test to reach what no message does.</summary>
    public async Task<T> ServiceAsync<T>()
        where T : notnull =>
        (await _services.Task.WaitAsync(TimeSpan.FromSeconds(10), Token)).GetRequiredService<T>();

    /// <summary>Connects as the window, which the agent then treats as its one window.</summary>
    /// <param name="acceptsShutdown">Whether the window agrees to close when the agent asks; false acts like unsaved work.</param>
    public async Task<FakeWindow> ConnectWindowAsync(bool acceptsShutdown = true)
    {
        var window = new FakeWindow { AcceptsShutdown = acceptsShutdown };
        window.Connection = await Connect(ProcessRole.Ui, window);
        return window;
    }

    public async Task<PipeConnection> ConnectLauncherAsync() => await Connect(ProcessRole.Launcher, null);

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

        await _stop.CancelAsync();
        try
        {
            await _run.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private sealed class IdleService : IAgentService
    {
        public Task RunAsync(LaunchArguments start, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private async Task<PipeConnection> Connect(ProcessRole role, IConnectionHandler? handler)
    {
        var connection = await PipeClient.ConnectAsync(Options.Names.PipeName,
            new Hello(Hello.CurrentProtocolVersion, "1.0.0", role, Environment.ProcessId), TimeSpan.FromSeconds(10), handler,
            cancellationToken: Token);
        _connections.Add(connection);
        return connection;
    }
}
