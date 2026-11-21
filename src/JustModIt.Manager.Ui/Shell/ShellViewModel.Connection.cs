using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Connection;

namespace JustModIt.Manager.Ui.Shell;

public sealed partial class ShellViewModel
{
    /// <summary>How long the window waits for the agent before it offers to start it again.</summary>
    public static readonly TimeSpan SlowConnection = TimeSpan.FromSeconds(8);

    private ITimer? _slowConnection;

    /// <summary>The window waits for the agent: the calm connecting state takes the place of the pages.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsPages))]
    public partial bool IsConnecting { get; private set; } = true;

    /// <summary>The agent is being replaced by an update; the window reconnects once it is back.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectingTitle), nameof(ConnectingMessage))]
    public partial bool IsUpdating { get; private set; }

    public string ConnectingTitle => IsUpdating ? Strings.Shell_UpdatingTitle
        : StartRefused ? Strings.Shell_StartAgentFailedTitle
        : Strings.Shell_ConnectingTitle;

    public string ConnectingMessage => IsUpdating ? Strings.Shell_UpdatingMessage : Strings.Shell_ConnectingMessage;

    /// <summary>Something is still under way while the window waits, so the connecting view moves and shows its bar.</summary>
    public bool IsWaiting => IsUpdating || !StartRefused;

    /// <summary>The agent has not answered for a while, so the window offers to start it again.</summary>
    [ObservableProperty]
    public partial bool IsSlowToConnect { get; private set; }

    /// <summary>The window asked the system to start the agent and waits for it; the button stays where it is, taking no presses.</summary>
    [ObservableProperty]
    public partial bool IsStartingAgent { get; private set; }

    /// <summary>The system refused to start the agent, so the window says so rather than that the agent does not answer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectingTitle), nameof(IsWaiting), nameof(StartAgentText), nameof(OffersLogsFolder))]
    public partial bool StartRefused { get; private set; }

    /// <summary>The line saying the system refused, which comes in anew with every refusal, so a screen reader reads it again.</summary>
    [ObservableProperty]
    public partial bool ShowsRefusal { get; private set; }

    /// <summary>The button's label: after a refusal it promises no start, only another try.</summary>
    public string StartAgentText => StartRefused ? Strings.Library_Retry : Strings.Shell_StartAgent;

    /// <summary>The logs folder is offered after a refusal, whose reason only the logs know, except in Game Mode, which opens no folders.</summary>
    public bool OffersLogsFolder => StartRefused && !_services.Context.InGameMode;

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
        StopCountingSlowConnection();
        var started = await Task.Run(_services.Context.StartAgent).ConfigureAwait(true);
        if (!IsConnecting)
        {
            return;
        }

        if (started)
        {
            StartRefused = false;
            ShowsRefusal = false;
            CountSlowConnection();
            return;
        }

        IsStartingAgent = false;
        StartRefused = true;
        ShowsRefusal = false;
        Dispatcher.UIThread.Post(() => ShowsRefusal = StartRefused, DispatcherPriority.Background);
    }

    [RelayCommand]
    private void OpenLogsFolder() => _services.Links.OpenFolder(_services.Context.LogsDirectory);

    private void CountSlowConnection()
    {
        _slowConnection?.Dispose();
        ITimer? timer = null;
        timer = _services.Clock.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
        {
            if (timer == _slowConnection)
            {
                StopCountingSlowConnection();
                IsSlowToConnect = true;
                IsStartingAgent = false;
            }
        }), null, SlowConnection, Timeout.InfiniteTimeSpan);
        _slowConnection = timer;
    }

    private void StopCountingSlowConnection()
    {
        _slowConnection?.Dispose();
        _slowConnection = null;
    }

    private void ApplyLink()
    {
        var connected = Agent.Link == AgentLink.Connected && Agent.HasState;
        IsConnecting = !connected && !IsUninstalled;
        IsUpdating = Agent.Link == AgentLink.Incompatible;
        if (connected)
        {
            StopCountingSlowConnection();
            IsSlowToConnect = false;
            IsStartingAgent = false;
            StartRefused = false;
            ShowsRefusal = false;
        }
        else if (_slowConnection is null && !IsSlowToConnect)
        {
            CountSlowConnection();
        }
    }
}
