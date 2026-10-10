using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Shell;

/// <summary>
/// The card at the foot of the sidebar: the one thing most worth knowing, in the tray's order (starting, an exit after
/// the jobs, the jobs, a check, the app's own update) and otherwise the app's version.
/// </summary>
public sealed partial class StatusCardViewModel : ObservableObject, IDisposable
{
    private readonly IAgent _agent;
    private readonly string _version;
    private JobView? _job;

    /// <summary>Creates the card, which follows the agent from then on.</summary>
    /// <param name="agent">The agent whose state the card sums up.</param>
    /// <param name="version">This app's version, shown until the agent reports its own.</param>
    public StatusCardViewModel(IAgent agent, string version)
    {
        ArgumentNullException.ThrowIfNull(agent);
        _agent = agent;
        _version = version;
        _agent.PropertyChanged += OnAgentChanged;
        _agent.JobsProgressed += OnJobsProgressed;
        Update();
    }

    [ObservableProperty]
    public partial Geometry Icon { get; private set; } = Icons.ShieldCheck;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleText))]
    public partial string Title { get; private set; } = "";

    /// <summary>A second line under the title; null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleText))]
    public partial string? Detail { get; private set; }

    /// <summary>A quieter word after the title on the same line, e.g. that the version is the newest.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleText))]
    public partial string? Suffix { get; private set; }

    /// <summary>The app is working: the icon turns in an accent-tinted square.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>The icon sits in an accent-tinted square without turning.</summary>
    [ObservableProperty]
    public partial bool IsAccent { get; private set; }

    /// <summary>A small accent dot on the icon calls for the action below.</summary>
    [ObservableProperty]
    public partial bool HasDot { get; private set; }

    [ObservableProperty]
    public partial bool ShowsProgress { get; private set; }

    /// <summary>From 0 to 100 while <see cref="ShowsProgress"/>.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial bool IsIndeterminate { get; private set; }

    /// <summary>The label of the card's one action; null when it has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAction))]
    public partial string? ActionText { get; private set; }

    [ObservableProperty]
    public partial Geometry? ActionIcon { get; private set; }

    /// <summary>What the action will do, for its tooltip.</summary>
    [ObservableProperty]
    public partial string? ActionTip { get; private set; }

    public bool HasAction => ActionText is not null;

    /// <summary>The card read out as one sentence.</summary>
    public string AccessibleText => string.Join(" ", new[] { Title, Suffix, Detail }.Where(part => !string.IsNullOrEmpty(part)));

    private Message? ActionMessage { get; set; }

    public void Dispose()
    {
        _agent.PropertyChanged -= OnAgentChanged;
        _agent.JobsProgressed -= OnJobsProgressed;
    }

    [RelayCommand]
    private async Task ActAsync()
    {
        if (ActionMessage is not { } message)
        {
            return;
        }

        try
        {
            await _agent.RequestAsync(message).ConfigureAwait(true);
        }
        // The card shows the outcome once the agent's state changes; a request that did not get through changes nothing.
        catch (Exception exception) when (exception is IOException or TimeoutException or Protocol.Transport.RequestFailedException)
        {
        }
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IAgent.State) or nameof(IAgent.HasState))
        {
            Update();
        }
    }

    private void OnJobsProgressed(object? sender, IReadOnlyList<JobView> jobs)
    {
        if (_job is not null && jobs.FirstOrDefault(job => job.Id == _job.Id) is { } moved && moved.Sequence > _job.Sequence)
        {
            ShowJob(moved);
        }
    }

    private void Update()
    {
        var state = _agent.State;
        _job = null;
        Reset();
        if (!_agent.HasState || !state.IsLoaded)
        {
            Show(Icons.LoaderCircle, Strings.Shell_StatusStarting, busy: true);
        }
        else if (state.Activity.IsExitPending)
        {
            Show(Icons.Clock, Strings.Shell_StatusExitPending, state.Jobs.Count > 0 ? string.Join(", ", state.Jobs.Select(job => job.GameName)) : null,
                accent: true);
        }
        else if (state.Jobs.Count > 1)
        {
            Show(Icons.LoaderCircle, Text.Format(Strings.Shell_StatusJobs, ("count", state.Jobs.Count)),
                string.Join(", ", state.Jobs.Select(job => job.GameName)), busy: true);
        }
        else if (state.Jobs.Count == 1)
        {
            ShowJob(state.Jobs[0]);
        }
        else if (state.Activity.IsChecking || state.Catalogue.State == CatalogueState.Refreshing)
        {
            Show(Icons.LoaderCircle, Strings.Shell_StatusChecking, Strings.Shell_StatusCheckingDetail, busy: true);
        }
        else
        {
            ShowUpdate(state.Update);
        }
    }

    private void ShowJob(JobView job)
    {
        _job = job;
        Reset();
        var waiting = job.Stage switch
        {
            JobStage.Queued => Strings.Shell_JobQueued,
            JobStage.Waiting => job.WaitReason switch
            {
                JobWaitReason.GameRunning => Strings.Shell_JobWaitingForGame,
                JobWaitReason.StoreBusy or JobWaitReason.QuietPeriod => Strings.Shell_JobWaitingForStore,
                JobWaitReason.Recovery => Strings.Shell_JobWaitingForRecovery,
                JobWaitReason.Download => Strings.Shell_JobWaitingForDownload,
                _ => Strings.Shell_JobQueued,
            },
            _ => null,
        };
        if (waiting is not null)
        {
            Show(job.Stage == JobStage.Queued ? Icons.Clock : Icons.Hourglass, waiting, job.GameName);
            return;
        }

        var title = job.Stage switch
        {
            JobStage.RollingBack => Strings.Shell_JobRollingBack,
            JobStage.RestoringPrevious => Strings.Shell_JobRestoringPrevious,
            _ => job.Kind switch
            {
                JobKind.Update => Strings.Shell_JobUpdating,
                JobKind.Repair => Strings.Shell_JobRepairing,
                JobKind.Uninstall => Strings.Shell_JobUninstalling,
                JobKind.TestInstall => Strings.Shell_JobTestInstalling,
                JobKind.MoveBackups => Strings.Shell_JobMovingBackups,
                JobKind.FollowMove => Strings.Shell_JobFollowingMove,
                _ => Strings.Shell_JobInstalling,
            },
        };
        var percent = job.Fraction is { } fraction ? Formats.Percent((int)Math.Floor(fraction * 100)) : null;
        Show(Icons.LoaderCircle, title,
            percent is null ? job.GameName : Text.Format(Strings.Shell_StatusDetailProgress, ("detail", job.GameName), ("percent", percent)),
            busy: true);
        ShowProgress(job.Fraction);
    }

    private void ShowUpdate(AppUpdateView update)
    {
        var version = update.Version ?? "";
        switch (update.State)
        {
            case AppUpdateState.Downloading:
                var detail = Text.Format(Strings.Shell_StatusVersion, ("version", version));
                Show(Icons.Download, Strings.Shell_StatusUpdateDownloading, update.Progress is { } progress
                    ? Text.Format(Strings.Shell_StatusDetailProgress, ("detail", detail), ("percent", Formats.Percent(progress)))
                    : detail, accent: true);
                ShowProgress(update.Progress / 100.0);
                break;
            case AppUpdateState.Ready:
                Show(Icons.Sparkles, Strings.Shell_StatusUpdateReady, Text.Format(Strings.Shell_StatusVersion, ("version", version)));
                HasDot = true;
                Offer(Strings.Shell_StatusRestart, Icons.RotateCcw, Text.Format(Strings.Shell_StatusRestartTip, ("version", version)),
                    new ApplyAppUpdate());
                break;
            case AppUpdateState.Available:
                Show(Icons.Download, Strings.Shell_StatusUpdateAvailable, Text.Format(Strings.Shell_StatusVersion, ("version", version)));
                HasDot = true;
                Offer(Strings.Shell_StatusDownload, Icons.Download, Strings.Shell_StatusDownloadTip, new CheckNow());
                break;
            default:
                Show(Icons.ShieldCheck, Text.Format(Strings.Shell_StatusVersion,
                    ("version", update.CurrentVersion is { Length: > 0 } current ? current : _version)));
                Suffix = update.State == AppUpdateState.UpToDate ? Strings.Shell_StatusUpToDate : null;
                break;
        }
    }

    private void Reset()
    {
        Suffix = null;
        HasDot = false;
        ShowsProgress = false;
        IsIndeterminate = false;
        Progress = 0;
        ActionText = null;
        ActionIcon = null;
        ActionTip = null;
        ActionMessage = null;
    }

    private void Show(Geometry icon, string title, string? detail = null, bool busy = false, bool accent = false)
    {
        Icon = icon;
        Title = title;
        Detail = detail;
        IsBusy = busy;
        IsAccent = accent || busy;
    }

    private void ShowProgress(double? fraction)
    {
        ShowsProgress = true;
        IsIndeterminate = fraction is null;
        Progress = Math.Clamp(fraction ?? 0, 0, 1) * 100;
    }

    private void Offer(string text, Geometry icon, string tip, Message message)
    {
        ActionText = text;
        ActionIcon = icon;
        ActionTip = tip;
        ActionMessage = message;
    }
}
