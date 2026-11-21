using System.ComponentModel;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Theme;
using Microsoft.Extensions.Logging;

namespace JustModIt.Manager.Ui.Shell;

public sealed partial class ShellViewModel
{
    // The agent's version the window starts anew for; null while it does not.
    private string? _restartFor;
    private bool _restartAtActivation;
    private bool _closingToRestart;
    private PageViewModel? _busyPage;
    private string? _loggedVersion;
    private bool _toldRestart;
    private ILogger? _logger;

    /// <summary>
    /// The window closes to start anew in the agent's version, as when the agent asks it to close, without the close question;
    /// the window adds where it stands.
    /// </summary>
    public event EventHandler<WindowRestart>? Restarting;

    /// <summary>Tells the shell that the window came to the front, which a start anew waiting for it takes, then the page shown.</summary>
    public void OnWindowActivated()
    {
        if (_restartAtActivation)
        {
            _restartAtActivation = false;
            _ = StartAnewAsync();
        }

        Navigator.Current?.OnWindowActivated();
    }

    // The window starts anew in the agent's version where it can, and otherwise works on, which its log says once.
    private void FollowAgentVersion(AgentLink link, string? agent)
    {
        if (agent == _services.Context.Version)
        {
            _restartFor = null;
        }
        else if (agent is not null && CanStartAnewFor(agent))
        {
            StartAnew(agent);
        }
        else if (agent is not null && link == AgentLink.Connected && _loggedVersion != agent)
        {
            _loggedVersion = agent;
            LogOtherVersion(_logger ??= _services.Context.Loggers.CreateLogger<ShellViewModel>(), agent, _services.Context.Version);
        }
    }

    // The window runs from a file that an update may have replaced, so starting anew brings the agent's version; it does so
    // once per version, since a file that brings the same version again is another copy.
    private bool CanStartAnewFor(string agent) =>
        _services.Context.RestartWindow is not null && _services.Context.Restarted?.Version != agent;

    private void StartAnew(string agent)
    {
        if (_restartFor != agent)
        {
            _restartFor = agent;
            _ = StartAnewAsync();
        }
    }

    // A build that closing would cut short holds the start until it ends, without a question; a page that keeps the author's
    // unsaved work holds it until the window next comes to the front.
    private async Task StartAnewAsync()
    {
        if (_restartFor is not { } version || _closingToRestart)
        {
            return;
        }

        if (Navigator.Pages.FirstOrDefault(page => page.IsWorking) is { } busy)
        {
            WaitForWork(busy);
            return;
        }

        _closingToRestart = true;
        // Starting anew ends the window's session cleanly, so a crash of its own not told yet would be lost.
        await TellUntoldExitsAsync().ConfigureAwait(true);
        var agreed = await Navigator.CanCloseAsync().ConfigureAwait(true);
        _closingToRestart = false;
        if (_restartFor != version)
        {
            return;
        }

        if (agreed)
        {
            Agent.ReportRestarting();
            Restarting?.Invoke(this, new WindowRestart(version));
        }
        else
        {
            _restartAtActivation = true;
        }
    }

    private void WaitForWork(PageViewModel page)
    {
        if (_busyPage != page)
        {
            _busyPage?.PropertyChanged -= OnBusyPageChanged;
            _busyPage = page;
            page.PropertyChanged += OnBusyPageChanged;
            ShowConnecting();
        }
    }

    // Under another protocol the pages are hidden, so the window starts anew as soon as the build ends; under the same one they
    // stay in front of the author, so it goes only when it next comes to the front.
    private void OnBusyPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageViewModel.IsWorking) || _busyPage is not { IsWorking: false } page)
        {
            return;
        }

        page.PropertyChanged -= OnBusyPageChanged;
        _busyPage = null;
        if (Agent.Link == AgentLink.Connected)
        {
            _restartAtActivation = true;
        }
        else
        {
            _ = StartAnewAsync();
        }

        ShowConnecting();
    }

    // Once, after a start anew that reached the agent's version; vanishing and coming back would otherwise look like a crash.
    private void TellRestarted()
    {
        if (!_toldRestart && _services.Context.Restarted is { } restarted && restarted.Version == _services.Context.Version)
        {
            Toasts.Show(new ToastViewModel(ToastTone.Info, Icons.RotateCcw,
                Text.Format(Strings.Shell_WindowRestarted, ("version", restarted.Version)), null));
        }

        _toldRestart = true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The agent runs version {Agent} and this window {Window}, which works on as it is")]
    private static partial void LogOtherVersion(ILogger logger, string agent, string window);
}
