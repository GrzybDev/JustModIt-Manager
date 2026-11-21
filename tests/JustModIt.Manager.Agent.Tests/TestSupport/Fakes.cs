using System.Collections.Concurrent;
using System.Threading.Channels;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent.Tests.TestSupport;

internal sealed class FakeForeground : IForegroundHandoff
{
    public ConcurrentQueue<int> Allowed { get; } = new();

    public void Allow(int processId) => Allowed.Enqueue(processId);
}

internal sealed class FakeShell : IDesktopShell
{
    public ConcurrentQueue<string> Opened { get; } = new();

    public ConcurrentQueue<ProgramStart> Started { get; } = new();

    public ConcurrentQueue<(string Executable, IReadOnlyList<string> Arguments)> Ran { get; } = new();

    /// <summary>How many times the agent started its window's process.</summary>
    public int WindowStarts => Ran.Count(run => run.Arguments is ["--ui"]);

    public bool Succeeds { get; set; } = true;

    public bool Open(string target)
    {
        Opened.Enqueue(target);
        return Succeeds;
    }

    public bool Start(ProgramStart program)
    {
        Started.Enqueue(program);
        return Succeeds;
    }

    // Ids the test's own processes cannot have, numbered in the order of the starts.
    public int? Run(string executable, IReadOnlyList<string> arguments, string? workingDirectory = null)
    {
        Ran.Enqueue((executable, arguments));
        return Succeeds ? 50_000 + Ran.Count : null;
    }
}

internal sealed class FakeShellRegistration : IAutostart, ILinkRegistration
{
    private readonly Lock _gate = new();

    public AutostartState State { get; set; }

    public ConcurrentDictionary<string, (string DisplayName, string Executable)> Schemes { get; } = new();

    public AutostartState GetState()
    {
        lock (_gate)
        {
            return State;
        }
    }

    public void Enable()
    {
        lock (_gate)
        {
            State = AutostartState.Enabled;
        }
    }

    public void Disable()
    {
        lock (_gate)
        {
            State = AutostartState.Disabled;
        }
    }

    public void Register(string scheme, string displayName, string executable) => Schemes[scheme] = (displayName, executable);

    public void Unregister(string scheme) => Schemes.TryRemove(scheme, out _);
}

internal sealed class FakeTray : ITrayHost, INotificationHost
{
    private readonly Lock _gate = new();

    public event EventHandler? Activated;

    public event EventHandler<string>? CommandInvoked;

    public event EventHandler<string?>? Clicked;

    public event EventHandler? SessionEnding;

    public event EventHandler? WindowReturnChanged;

    public WindowReturn WindowReturn { get; private set; }

    public string? Tooltip { get; private set; }

    public IReadOnlyList<TrayMenuItem> Menu { get; private set; } = [];

    /// <summary>Every notification the agent asked for, also those a session without notifications did not show.</summary>
    public Channel<(string Title, string Message, string? Tag)> Notifications { get; } = Channel.CreateUnbounded<(string, string, string?)>();

    /// <summary>Whether the session shows notifications.</summary>
    public bool ShowsNotifications { get; set; } = true;

    public bool IsClickable { get; set; } = true;

    public Channel<string> Tooltips { get; } = Channel.CreateUnbounded<string>();

    public bool IsDisposed { get; private set; }

    public void Show(string tooltip, IReadOnlyList<TrayMenuItem> menu)
    {
        lock (_gate)
        {
            Tooltip = tooltip;
            Menu = menu;
        }

        Tooltips.Writer.TryWrite(tooltip);
    }

    public bool Notify(string title, string message, string? tag) => Notifications.Writer.TryWrite((title, message, tag)) && ShowsNotifications;

    public void Click() => Activated?.Invoke(this, EventArgs.Empty);

    public void Pick(string command) => CommandInvoked?.Invoke(this, command);

    public void ClickNotification(string? tag) => Clicked?.Invoke(this, tag);

    public void EndSession() => SessionEnding?.Invoke(this, EventArgs.Empty);

    /// <summary>Changes how the window comes back, as a session does when its tray comes or goes.</summary>
    /// <param name="windowReturn">The new way back.</param>
    public void ReturnBy(WindowReturn windowReturn)
    {
        WindowReturn = windowReturn;
        WindowReturnChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => IsDisposed = true;
}

/// <summary>A window process as the agent sees it: it records what it is sent and answers when asked to close.</summary>
internal sealed class FakeWindow : IConnectionHandler
{
    private readonly ConcurrentQueue<Task> _requests = new();

    public PipeConnection Connection { get; set; } = null!;

    public bool AcceptsShutdown { get; init; } = true;

    public Channel<Message> Events { get; } = Channel.CreateUnbounded<Message>();

    public Channel<ShutdownReason> ShutdownRequests { get; } = Channel.CreateUnbounded<ShutdownReason>();

    public async ValueTask<Message?> HandleRequestAsync(PipeConnection connection, Message request, CancellationToken cancellationToken)
    {
        if (request is not ShutdownRequested shutdown)
        {
            throw new RequestFailedException(ErrorCode.Unsupported);
        }

        ShutdownRequests.Writer.TryWrite(shutdown.Reason);
        if (AcceptsShutdown)
        {
            // Like a window that closes once it has answered. The agent answers a request that makes it exit after asking
            // the window to close, so the window waits for that answer as well.
            _ = Task.Run(async () =>
            {
                await Task.Delay(50, CancellationToken.None);
                await Task.WhenAll(_requests).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                await connection.DisposeAsync();
            }, CancellationToken.None);
        }

        return await ValueTask.FromResult<Message?>(new ShutdownAnswer(AcceptsShutdown));
    }

    public void OnEvent(PipeConnection connection, Message message) => Events.Writer.TryWrite(message);

    public Task<Message?> RequestAsync(Message request)
    {
        var answer = Connection.RequestAsync(request, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        _requests.Enqueue(answer);
        return answer;
    }

    /// <summary>The next event of type <typeparamref name="T"/> that satisfies <paramref name="condition"/>; others are skipped.</summary>
    /// <param name="condition">What the event must satisfy; null takes the next event of the type.</param>
    public async Task<T> NextAsync<T>(Func<T, bool>? condition = null)
        where T : Message
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (true)
        {
            var message = await Events.Reader.ReadAsync(timeout.Token);
            if (message is T typed && (condition is null || condition(typed)))
            {
                return typed;
            }
        }
    }

    public async Task<AppStateView> StateAsync(Func<AppStateView, bool> condition) =>
        (await NextAsync<StateChanged>(changed => condition(changed.State))).State;
}

/// <summary>A store whose one library holds one installed, idle game.</summary>
/// <param name="game">The game's key.</param>
/// <param name="folder">The game's folder.</param>
internal sealed class OneGameLibrary(GameKey game, string folder) : ILibraryProvider
{
    private InstalledGame Game => new()
    {
        Key = game,
        Name = "Example Game",
        InstallDirectory = folder,
        LibraryId = "library",
        State = GameInstallState.Installed,
    };

    public StoreId Store => game.Store;

    public Task<StoreScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken) => Task.FromResult(new StoreScan
    {
        Store = game.Store,
        IsPresent = true,
        Libraries = [new LibraryLocation("library", "test", Path.GetDirectoryName(folder), LibraryState.Online)],
        Games = [Game],
    });

    public Task<InstalledGame?> ProbeAsync(InstalledGame known, LibraryScanContext context, CancellationToken cancellationToken) =>
        Task.FromResult<InstalledGame?>(Game);

    public Task<LibraryFolderEvidence> InspectFolderAsync(string path, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>A site that cannot be reached.</summary>
internal sealed class OfflineCatalogueApi : ICatalogueApi
{
    private static CatalogueException Offline => new(CatalogueErrorReason.Offline, "The test site is offline.");

    public Task<ApiPage<CatalogueGame>> GetGamesAsync(int page, CancellationToken cancellationToken) => throw Offline;

    public Task<ApiPage<GameDownloads>> GetGameDownloadsAsync(int page, CancellationToken cancellationToken) => throw Offline;

    public Task<IReadOnlyList<ProjectSummary>> GetGameProjectsAsync(string gameSlug, CancellationToken cancellationToken) => throw Offline;

    public Task<ProjectPageDocument> GetProjectPageAsync(string gameSlug, string teamSlug, CancellationToken cancellationToken) =>
        throw Offline;

    public Task<IReadOnlyList<AchievementTexts>> GetAchievementTextsAsync(string teamSlug, string projectSlug,
        CancellationToken cancellationToken) => throw Offline;

    public Task<ApiPage<Team>> GetTeamsAsync(int page, CancellationToken cancellationToken) => throw Offline;

    public Task<IReadOnlyList<ProjectStatus>> GetStatusesAsync(CancellationToken cancellationToken) => throw Offline;

    public Task<IReadOnlyList<ProjectType>> GetProjectTypesAsync(CancellationToken cancellationToken) => throw Offline;

    public Task<IReadOnlyList<Genre>> GetGenresAsync(CancellationToken cancellationToken) => throw Offline;

    public Task<IReadOnlyList<GamePlatform>> GetGamePlatformsAsync(CancellationToken cancellationToken) => throw Offline;

    public Task<IReadOnlyList<StorefrontPlatform>> GetStorefrontPlatformsAsync(CancellationToken cancellationToken) => throw Offline;

    public Task<SiteConfiguration> GetSiteConfigurationAsync(CancellationToken cancellationToken) => throw Offline;

    public Task<Uri> ResolveDownloadAsync(ReleaseFileLocator file, bool layouts, CancellationToken cancellationToken) => throw Offline;

    public Task<IReadOnlyDictionary<Guid, Uri>> ResolveDownloadsAsync(ReleaseLocator release, IReadOnlyList<Guid> files,
        CancellationToken cancellationToken) => throw Offline;
}

/// <summary>
/// A system with no registry, no processes, no programs holding files and no events, so no test reaches the developer's own.
/// </summary>
internal sealed class QuietSystem : IRegistry, IRegistryWatcher, ISystemEvents, IProcessSource, ILockHolderSource, IDisposable
{
    public static QuietSystem Instance { get; } = new();

    public event EventHandler? Resumed { add { } remove { } }

    public event EventHandler? VolumeArrived { add { } remove { } }

    public int CurrentSessionId => 1;

    public RegistryKeySnapshot? ReadKey(RegistryHive hive, RegistryView view, string path) => null;

    public IDisposable Watch(WatchedRegistryKey key, Action<WatchSignal> onSignal) => this;

    public IReadOnlyList<ProcessEntry> Snapshot() => [];

    public Task<bool> WaitForExitAsync(int processId, string name, CancellationToken cancellationToken) => Task.FromResult(true);

    public IReadOnlyList<FileHolder> FindHolders(IReadOnlyList<string> paths) => [];

    public void Dispose()
    {
    }
}
