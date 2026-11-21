using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Creator;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Shell;

public sealed partial class ShellViewModel
{
    /// <summary>
    /// How long the window waits for the agent before it offers to start it again, or, where it cannot start anew itself,
    /// before it shows that the agent runs another version.
    /// </summary>
    public static readonly TimeSpan SlowConnection = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How long the window that stays through an update waits before it offers to start the agent again, so the offer does not
    /// race the updater, which replaces the file and starts the agent itself.
    /// </summary>
    public static readonly TimeSpan SlowUpdate = TimeSpan.FromSeconds(60);

    private static readonly string[] ConnectingTexts =
    [
        nameof(ConnectingTitle), nameof(ConnectingMessage), nameof(IsWaiting), nameof(ShowsNoAnswer), nameof(ShowsStartAgent),
        nameof(StartAgentText), nameof(OffersLogsFolder), nameof(MismatchCopy), nameof(ShowsShortcutHint),
    ];

    private ITimer? _slowConnection;
    private AgentLink _shownLink;

    // The agent answers in another protocol, and a window that cannot start anew has waited in vain for it to change.
    private bool _mismatchSettled;

    // The agent's version when the player had it started again, so that version coming back tells another copy of the app.
    private string? _agentBeforeStart;

    /// <summary>The window waits for the agent: the calm connecting state takes the place of the pages.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPages))]
    public partial bool IsConnecting { get; private set; } = true;

    /// <summary>The agent has not answered for a while, so the window offers to start it again.</summary>
    [ObservableProperty]
    public partial bool IsSlowToConnect { get; private set; }

    /// <summary>The window asked the system to start the agent and waits for it; the button stays where it is, taking no presses.</summary>
    [ObservableProperty]
    public partial bool IsStartingAgent { get; private set; }

    /// <summary>The system refused to start the agent, so the window says so rather than that the agent does not answer.</summary>
    [ObservableProperty]
    public partial bool StartRefused { get; private set; }

    /// <summary>The line saying the system refused, which comes in anew with every refusal, so a screen reader reads it again.</summary>
    [ObservableProperty]
    public partial bool ShowsRefusal { get; private set; }

    public string ConnectingTitle => Mode switch
    {
        Waiting.Update => Strings.Shell_UpdatingTitle,
        Waiting.Versions => Strings.Shell_VersionMismatchTitle,
        _ => StartRefused ? Strings.Shell_StartAgentFailedTitle : Strings.Shell_ConnectingTitle,
    };

    public string ConnectingMessage => Mode switch
    {
        Waiting.Update when _busyPage is not null => Strings.Shell_UpdatingAfterBuild,
        Waiting.Update => Text.Format(Strings.Shell_UpdatingMessage,
            ("mode", _restartFor is null && _keptUpdate != KeptUpdate.Away ? "other" : "inplace")),
        Waiting.Versions => Text.Format(Strings.Shell_VersionMismatch, ("window", _services.Context.Version), ("agent", Agent.AgentVersion ?? "")),
        _ => Strings.Shell_ConnectingMessage,
    };

    /// <summary>Something is still under way while the window waits, so the connecting view moves and shows its bar.</summary>
    public bool IsWaiting => Mode == Waiting.Update || (Mode == Waiting.Agent && !StartRefused);

    /// <summary>The agent does not answer at all, which is never said of one that answers in another version.</summary>
    public bool ShowsNoAnswer => IsSlowToConnect && !StartRefused && Mode != Waiting.Versions;

    /// <summary>The button that starts the agent again shows: offered, waiting for the system and the agent, or after a refusal.</summary>
    public bool ShowsStartAgent => StartRefused || IsStartingAgent || (Mode == Waiting.Versions ? OffersAgentRestart : IsSlowToConnect);

    /// <summary>The button's label: after a refusal it promises no start, only another try.</summary>
    public string StartAgentText => StartRefused ? Strings.Library_Retry : Strings.Shell_StartAgent;

    /// <summary>The logs folder is offered after a refusal, whose reason only the logs know, except in Game Mode, which opens no folders.</summary>
    public bool OffersLogsFolder => StartRefused && !_services.Context.InGameMode;

    /// <summary>Which copy of the app the window runs from, where starting the agent again would not bring the versions together; null otherwise.</summary>
    public string? MismatchCopy => Mode == Waiting.Versions && !IsStartingAgent && (IsOlder || _agentBeforeStart == Agent.AgentVersion)
        ? Text.Format(Strings.Shell_VersionMismatchCopy, ("age", IsOlder ? "older" : "other"),
            ("file", CreatorWording.Wrappable(GameWording.FromHome(_services.Context.ProgramFile))))
        : null;

    /// <summary>In Game Mode the window comes from Steam's shortcut, which the settings on the desktop put right.</summary>
    public bool ShowsShortcutHint => MismatchCopy is not null && _services.Context.InGameMode;

    private Waiting Mode => _keptUpdate == KeptUpdate.Away ? Waiting.Update
        : Agent.Link != AgentLink.Incompatible ? Waiting.Agent
        : _restartFor is not null || (_services.Context.RestartWindow is null && !_mismatchSettled) ? Waiting.Update
        : Waiting.Versions;

    // Starting the agent's unit again helps only an agent older than the window, whose file may be newer than its code; one
    // that came back as it was runs from another copy.
    private bool OffersAgentRestart =>
        !IsOlder && _services.Context.AgentRunsAsUnit && _agentBeforeStart != Agent.AgentVersion;

    private bool IsOlder => Agent.AgentVersion is { } agent && AppVersions.Compare(_services.Context.Version, agent) < 0;

    // The button stays where it is, with the focus, while the system answers and then while the agent has its usual time to
    // connect; a press meanwhile does nothing, so it never starts the agent twice.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task StartAgentAsync()
    {
        if (IsStartingAgent)
        {
            return;
        }

        IsStartingAgent = true;
        var before = Agent.Link == AgentLink.Incompatible ? Agent.AgentVersion : null;
        StopCountingSlowConnection();
        ShowConnecting();
        var started = await Task.Run(_services.Context.StartAgent).ConfigureAwait(true);
        if (!IsConnecting)
        {
            return;
        }

        if (started)
        {
            StartRefused = false;
            ShowsRefusal = false;
            _agentBeforeStart = before;
            CountSlowConnection(SlowConnection);
        }
        else
        {
            IsStartingAgent = false;
            StartRefused = true;
            ShowsRefusal = false;
            Dispatcher.UIThread.Post(() => ShowsRefusal = StartRefused, DispatcherPriority.Background);
        }

        ShowConnecting();
    }

    [RelayCommand]
    private void OpenLogsFolder() => _services.Links.OpenFolder(_services.Context.LogsDirectory);

    private void ApplyLink()
    {
        var link = Agent.Link;
        var agent = Agent.AgentVersion;
        var connected = link == AgentLink.Connected && Agent.HasState;
        IsConnecting = !connected && !IsUninstalled;
        FollowKeptUpdate(link, agent);
        FollowAgentVersion(link, agent);
        if (connected)
        {
            StopCountingSlowConnection();
            IsSlowToConnect = false;
            IsStartingAgent = false;
            StartRefused = false;
            ShowsRefusal = false;
            _mismatchSettled = false;
            _agentBeforeStart = null;
            TellRestarted();
        }
        else if (link == AgentLink.Connecting)
        {
            _mismatchSettled = false;
            if (_slowConnection is null && !IsSlowToConnect && !IsStartingAgent)
            {
                CountSlowConnection(_keptUpdate == KeptUpdate.Away ? SlowUpdate : SlowConnection);
            }
        }
        else
        {
            // The agent answers, only in another version, so it is never said not to.
            IsSlowToConnect = false;
            if (_shownLink == AgentLink.Connecting)
            {
                IsStartingAgent = false;
            }

            if (Mode == Waiting.Update && _restartFor is null && _slowConnection is null)
            {
                CountSlowConnection(SlowConnection);
            }
        }

        _shownLink = link;
        ShowConnecting();
    }

    private void ShowConnecting()
    {
        foreach (var name in ConnectingTexts)
        {
            OnPropertyChanged(name);
        }
    }

    private void CountSlowConnection(TimeSpan after)
    {
        _slowConnection?.Dispose();
        ITimer? timer = null;
        timer = _services.Clock.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
        {
            if (timer != _slowConnection)
            {
                return;
            }

            StopCountingSlowConnection();
            IsStartingAgent = false;
            if (Agent.Link == AgentLink.Incompatible)
            {
                _mismatchSettled = true;
            }
            else
            {
                IsSlowToConnect = true;
            }

            ShowConnecting();
        }), null, after, Timeout.InfiniteTimeSpan);
        _slowConnection = timer;
    }

    private void StopCountingSlowConnection()
    {
        _slowConnection?.Dispose();
        _slowConnection = null;
    }

    private enum Waiting
    {
        Agent,
        Update,
        Versions,
    }
}
