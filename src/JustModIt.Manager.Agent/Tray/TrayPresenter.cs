using JustModIt.Manager.Agent.Ipc;
using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Views;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Agent.Tray;

/// <summary>
/// Keeps the tray icon in step with the state and carries out what the player picks there, on the thread pool, since
/// the tray's events arrive on its own thread.
/// </summary>
public sealed partial class TrayPresenter : IAgentService, IDisposable
{
    private readonly ITrayHost _tray;
    private readonly StatePublisher _publisher;
    private readonly UiLauncher _ui;
    private readonly AgentLifetime _lifetime;
    private readonly Checks _checks;
    private readonly ActivationHandler _activation;
    private readonly AppLinks _links;
    private readonly AgentOptions _options;
    private readonly ILogger<TrayPresenter> _logger;
    private readonly Lock _gate = new();
    private (string Tooltip, IReadOnlyList<TrayMenuItem> Menu)? _shown;

    // Subscribed from the start, so nothing the player does while the agent starts is lost.
    public TrayPresenter(ITrayHost tray, StatePublisher publisher, UiLauncher ui, AgentLifetime lifetime, Checks checks,
        ActivationHandler activation, AppLinks links, AgentOptions options, ILogger<TrayPresenter> logger)
    {
        _tray = tray;
        _publisher = publisher;
        _ui = ui;
        _lifetime = lifetime;
        _checks = checks;
        _activation = activation;
        _links = links;
        _options = options;
        _logger = logger;
        // Subscribed from the start, so nothing the player does while the agent starts is lost.
        _tray.Activated += OnActivated;
        _tray.CommandInvoked += OnCommand;
        _tray.NotificationClicked += OnNotificationClicked;
        _publisher.Published += OnPublished;
    }

    public Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        Show(_publisher.Latest);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _publisher.Published -= OnPublished;
        _tray.NotificationClicked -= OnNotificationClicked;
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
            case TrayCommands.Exit:
                Run(_lifetime.ExitFromTray);
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
