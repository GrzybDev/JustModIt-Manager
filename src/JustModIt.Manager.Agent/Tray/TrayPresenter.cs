using JustModIt.Manager.Agent.Ipc;
using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Agent.Updates;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Tray;

/// <summary>
/// Keeps the tray icon in step with the state, and the state with how the session brings the window back, and carries out
/// what the player picks in the tray or in a notification, on the thread pool, since the tray's and the notifications'
/// events arrive on threads of the system's.
/// </summary>
public sealed partial class TrayPresenter : IAgentService, IDisposable
{
    private readonly ITrayHost _tray;
    private readonly INotificationHost _notifications;
    private readonly StatePublisher _publisher;
    private readonly AgentStatus _status;
    private readonly UiLauncher _ui;
    private readonly AgentLifetime _lifetime;
    private readonly Checks _checks;
    private readonly UpdateService _updates;
    private readonly ActivationHandler _activation;
    private readonly AppLinks _links;
    private readonly AgentOptions _options;
    private readonly ILogger<TrayPresenter> _logger;
    private readonly Lock _gate = new();
    private (string Tooltip, IReadOnlyList<TrayMenuItem> Menu)? _shown;

    /// <summary>Creates the presenter, which follows the tray and the state from then on.</summary>
    /// <param name="tray">The tray icon.</param>
    /// <param name="notifications">The system's notifications, whose clicks open what they link to.</param>
    /// <param name="publisher">Announces every new snapshot.</param>
    /// <param name="status">Carries how the player gets the window back, which the tray tells, to the window.</param>
    /// <param name="ui">Shows the window.</param>
    /// <param name="lifetime">Exits the app, and tells whether it is leaving.</param>
    /// <param name="checks">Runs a check the player asks for.</param>
    /// <param name="updates">Restarts into a ready update.</param>
    /// <param name="activation">Opens what a notification links to.</param>
    /// <param name="links">Reads a notification's link.</param>
    /// <param name="options">The app's name as players see it.</param>
    /// <param name="logger">Receives failed commands.</param>
    public TrayPresenter(ITrayHost tray, INotificationHost notifications, StatePublisher publisher, AgentStatus status, UiLauncher ui,
        AgentLifetime lifetime, Checks checks, UpdateService updates, ActivationHandler activation, AppLinks links, AgentOptions options,
        ILogger<TrayPresenter> logger)
    {
        _tray = tray;
        _notifications = notifications;
        _publisher = publisher;
        _status = status;
        _ui = ui;
        _lifetime = lifetime;
        _checks = checks;
        _updates = updates;
        _activation = activation;
        _links = links;
        _options = options;
        _logger = logger;
        // Subscribed from the start, so nothing the player does while the agent starts is lost.
        _tray.Activated += OnActivated;
        _tray.CommandInvoked += OnCommand;
        _tray.WindowReturnChanged += OnWindowReturnChanged;
        _notifications.Clicked += OnNotificationClicked;
        _publisher.Published += OnPublished;
    }

    public Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        OnWindowReturnChanged(this, EventArgs.Empty);
        Show(_publisher.Latest);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _publisher.Published -= OnPublished;
        _tray.WindowReturnChanged -= OnWindowReturnChanged;
        _notifications.Clicked -= OnNotificationClicked;
        _tray.CommandInvoked -= OnCommand;
        _tray.Activated -= OnActivated;
    }

    private void Show(AppStateView state)
    {
        var tooltip = TrayContent.Tooltip(state, _options.ProductName);
        var menu = TrayContent.Menu(state, _options.ProductName);
        lock (_gate)
        {
            // Most changes of the state change nothing the tray shows.
            if (_shown is { } shown && shown.Tooltip == tooltip && shown.Menu.SequenceEqual(menu))
            {
                return;
            }

            _shown = (tooltip, menu);
            _tray.Show(tooltip, menu);
        }
    }

    private void OnPublished(object? sender, AppStateView state) => Show(state);

    private void OnWindowReturnChanged(object? sender, EventArgs e) =>
        _status.ChangeActivity(activity => activity with { WindowReturn = _tray.WindowReturn });

    private void OnActivated(object? sender, EventArgs e) => Run(() => _ui.Show());

    private void OnCommand(object? sender, string command)
    {
        switch (command)
        {
            case TrayCommands.Open:
                Run(() => _ui.Show());
                break;
            case TrayCommands.CheckNow:
                Run(() => _ = _checks.CheckNowAsync());
                break;
            case TrayCommands.RestartToUpdate:
                Run(() => _ = _updates.RestartToUpdateAsync());
                break;
            case TrayCommands.Exit:
                Run(() => _ = _lifetime.RequestExitAsync(ExitMode.AfterJobs));
                break;
            case TrayCommands.ExitNow:
                Run(() => _ = _lifetime.RequestExitAsync(ExitMode.Now));
                break;
        }
    }

    private void OnNotificationClicked(object? sender, string? link) =>
        Run(() => _activation.Handle(LaunchArguments.Parse(link is null ? [] : [link], _links)));

    private void Run(Action action) => _ = Task.Run(() =>
    {
        // Once the agent leaves, a window it started would be left without it.
        if (_lifetime.IsLeaving)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception exception)
        {
            LogCommandFailed(exception);
        }
    });

    [LoggerMessage(Level = LogLevel.Error, Message = "A tray command failed")]
    private partial void LogCommandFailed(Exception exception);
}
