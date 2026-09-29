using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Ui;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent.Notifications;

/// <summary>
/// Tells the player what the automation did or found, either in the window while it is in front or from the tray, whose
/// notification opens the game it is about.
/// </summary>
public sealed class Notifier : IAgentService, IDisposable
{
    private readonly StateCoordinator _coordinator;
    private readonly UiLauncher _ui;
    private readonly ITrayHost _tray;
    private readonly ISettingsService _settings;
    private readonly AgentLifetime _lifetime;
    private readonly AppLinks _links;
    private readonly AgentOptions _options;
    private readonly Lock _closeGate = new();

    /// <summary>Creates the notifier, which follows the window's closing from then on.</summary>
    /// <param name="coordinator">Hands out the automation's notices.</param>
    /// <param name="ui">The window, whether it is in front, and its closing.</param>
    /// <param name="tray">Shows notifications while the window is not in front.</param>
    /// <param name="settings">Whether notifications are on, and whether the player heard about the tray.</param>
    /// <param name="lifetime">Tells whether the agent is exiting.</param>
    /// <param name="links">Makes the link a notification opens.</param>
    /// <param name="options">The product name the notices use.</param>
    public Notifier(StateCoordinator coordinator, UiLauncher ui, ITrayHost tray, ISettingsService settings, AgentLifetime lifetime,
        AppLinks links, AgentOptions options)
    {
        _coordinator = coordinator;
        _ui = ui;
        _tray = tray;
        _settings = settings;
        _lifetime = lifetime;
        _links = links;
        _options = options;
        // Subscribed from the start, so a window that closes while the agent starts is still noticed.
        _ui.Closed += OnWindowClosed;
    }

    public async Task RunAsync(LaunchArguments start, CancellationToken cancellationToken)
    {
        await foreach (var notice in _coordinator.Notices.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            Notify(notice);
        }
    }

    /// <summary>Shows a notice in the window while it is in front, otherwise from the tray.</summary>
    /// <param name="notice">A typed notice of the automation.</param>
    public void Notify(AutomationNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (_ui.IsActive && _ui.Window is { } window)
        {
            window.Post(new Notify(NoticeView.From(notice)));
            return;
        }

        var (title, message) = NoticeTexts.For(notice);
        ShowFromTray(title, message, notice.Game is { } game
            ? _links.For(new GameTarget(game.Store.Value, game.StoreGameId))
            : _links.For(new LibraryTarget()));
    }

    public void Dispose() => _ui.Closed -= OnWindowClosed;

    // False when the player turned system notifications off.
    private bool ShowFromTray(string title, string message, string link)
    {
        if (!_settings.Current.ShowNotifications)
        {
            return false;
        }

        _tray.ShowNotification(title, message, link);
        return true;
    }

    // Closing the window keeps the app in the tray, which the player hears once; before the first-run setup is done, at
    // every close, since until then the app does nothing for them.
    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_lifetime.IsExiting)
        {
            return;
        }

        var product = ("product", (object?)_options.ProductName);
        // Two closes in quick succession must not both tell what only the first should.
        lock (_closeGate)
        {
            var current = _settings.Current;
            if (!current.OnboardingCompleted)
            {
                ShowFromTray(Text.Format(Strings.SetupPendingTitle, product), Strings.SetupPending, _links.Open);
            }
            else if (!current.TrayNoticeShown
                && ShowFromTray(Text.Format(Strings.WindowClosedTitle, product), Strings.WindowClosed, _links.Open))
            {
                _settings.Update(updated => updated with { TrayNoticeShown = true });
            }
        }
    }
}
