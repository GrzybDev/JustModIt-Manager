using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Controls;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>
/// The settings: every change goes to the agent at once, with no button to save. The page shows the settings it sent until
/// the agent's state has them, then the agent's own, so a change made elsewhere shows here too.
/// </summary>
public sealed partial class SettingsPageViewModel : PageViewModel, IDisposable
{
    private readonly UiServices _services;
    private readonly SettingsEditor _editor;
    private bool _syncing;

    /// <summary>Creates the page, which follows the agent's state from then on.</summary>
    /// <param name="services">The window's services.</param>
    public SettingsPageViewModel(UiServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _editor = new SettingsEditor(services);
        Backups = new BackupsViewModel(services, _editor, Sync);
        Teams = new PreferredTeamsViewModel(services, _editor);
        Folders = new AddedFoldersViewModel(services);
        Data = new AppDataViewModel(services);
        Sync();
        _services.Agent.PropertyChanged += OnAgentChanged;
        _services.Agent.JobsProgressed += OnJobsProgressed;
    }

    public override string Title => Strings.Shell_Settings;

    public BackupsViewModel Backups { get; }

    public PreferredTeamsViewModel Teams { get; }

    public AddedFoldersViewModel Folders { get; }

    public AppDataViewModel Data { get; }

    public string GeneralHeading { get; } = Overline(Strings.Settings_General);

    public string ProjectsHeading { get; } = Overline(Strings.Settings_Projects);

    public string BackupsHeading { get; } = Overline(Strings.Settings_Backups);

    public string LibrariesHeading { get; } = Overline(Strings.Settings_Libraries);

    public string CreatorsHeading { get; } = Overline(Strings.Settings_ForCreators);

    public string AppDataHeading { get; } = Overline(Strings.Settings_AppData);

    public string AboutHeading { get; } = Overline(Strings.Settings_About);

    public string StartWithSystemDescription =>
        Text.Format(Strings.Settings_StartWithSystemDescription, ("product", _services.Context.ProductName));

    public string AutoUpdateAppDescription =>
        Text.Format(Strings.Settings_AutoUpdateAppDescription, ("product", _services.Context.ProductName));

    public string UpdatePrereleasesDescription =>
        Text.Format(Strings.Settings_UpdatePrereleasesDescription, ("product", _services.Context.ProductName));

    public string CreatorModeDescription { get; } = Text.Format(Strings.Settings_CreatorModeDescription, ("creator", Strings.Shell_Creator));

    [ObservableProperty]
    public partial bool StartWithSystem { get; set; }

    [ObservableProperty]
    public partial CloseBehavior CloseBehavior { get; set; }

    [ObservableProperty]
    public partial bool BlurAdultContent { get; set; }

    [ObservableProperty]
    public partial bool AutoInstallProjects { get; set; }

    [ObservableProperty]
    public partial bool AutoUpdateProjects { get; set; }

    [ObservableProperty]
    public partial bool ReapplyAfterGameUpdate { get; set; }

    [ObservableProperty]
    public partial bool ShowNotifications { get; set; }

    [ObservableProperty]
    public partial bool CreatorMode { get; set; }

    [ObservableProperty]
    public partial bool AutoUpdateApp { get; set; }

    [ObservableProperty]
    public partial bool UpdatePrereleases { get; set; }

    /// <inheritdoc cref="AppSettings.SendErrorReports"/>
    [ObservableProperty]
    public partial bool SendErrorReports { get; set; }

    /// <summary>The build has somewhere to send error reports, so the page offers them.</summary>
    public bool CanSendErrorReports => _services.ErrorReports.IsAvailable;

    public string ProductName => _services.Context.ProductName;

    /// <summary>The app's logo; null when its branding has none.</summary>
    public IImage? Logo => _services.Context.Branding.Logo;

    [ObservableProperty]
    public partial string VersionText { get; private set; } = "";

    /// <summary>How the app's own update stands, in a sentence.</summary>
    [ObservableProperty]
    public partial string UpdateText { get; private set; } = "";

    [ObservableProperty]
    public partial Geometry UpdateIcon { get; private set; } = Icons.Clock;

    [ObservableProperty]
    public partial Tone UpdateTone { get; private set; }

    [ObservableProperty]
    public partial bool IsUpdateBusy { get; private set; }

    [ObservableProperty]
    public partial bool ShowsUpdateProgress { get; private set; }

    /// <summary>From 0 to 100 while an update downloads.</summary>
    [ObservableProperty]
    public partial double UpdateProgress { get; private set; }

    /// <summary>The check is offered unless the update is ready, which the sidebar's card restarts into.</summary>
    [ObservableProperty]
    public partial bool ShowsCheck { get; private set; }

    /// <summary>This copy updates itself, so its automatic updates can be switched.</summary>
    [ObservableProperty]
    public partial bool CanUpdateApp { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    public partial bool CanCheck { get; private set; }

    [ObservableProperty]
    public partial string CheckText { get; private set; } = Strings.Settings_CheckUpdates;

    /// <summary>What the update found or ready changes, in Markdown; null when it carries no notes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
    public partial string? ReleaseNotes { get; private set; }

    public bool HasReleaseNotes => ReleaseNotes is not null;

    [ObservableProperty]
    public partial string WhatsNewText { get; private set; } = "";

    [ObservableProperty]
    public partial string SiteName { get; private set; } = "";

    public bool HasSite => _services.Context.Site.SiteAddress is not null;

    public bool HasSource => _services.Context.SourceAddress is not null;

    public bool HasIssues => _services.Context.IssuesAddress is not null;

    public string BrandingLine => Text.Format(Strings.Settings_BrandingLine, ("product", _services.Context.ProductName));

    public override void OnShown()
    {
        Backups.Load();
        Teams.Load();
        Folders.Load();
        Data.Load();
    }

    public void Dispose()
    {
        _services.Agent.PropertyChanged -= OnAgentChanged;
        _services.Agent.JobsProgressed -= OnJobsProgressed;
    }

    partial void OnStartWithSystemChanged(bool value) => Change(settings => settings with { StartWithSystem = value });

    partial void OnCloseBehaviorChanged(CloseBehavior value) => Change(settings => settings with { CloseBehavior = value });

    partial void OnBlurAdultContentChanged(bool value) => Change(settings => settings with { BlurAdultContent = value });

    partial void OnAutoInstallProjectsChanged(bool value) => Change(settings => settings with { AutoInstallProjects = value });

    partial void OnAutoUpdateProjectsChanged(bool value) => Change(settings => settings with { AutoUpdateProjects = value });

    partial void OnReapplyAfterGameUpdateChanged(bool value) => Change(settings => settings with { ReapplyAfterGameUpdate = value });

    partial void OnShowNotificationsChanged(bool value) => Change(settings => settings with { ShowNotifications = value });

    partial void OnCreatorModeChanged(bool value) => Change(settings => settings with { CreatorMode = value });

    partial void OnAutoUpdateAppChanged(bool value) => Change(settings => settings with { AutoUpdateApp = value });

    partial void OnUpdatePrereleasesChanged(bool value) => Change(settings => settings with { UpdatePrereleases = value });

    partial void OnSendErrorReportsChanged(bool value) => Change(settings => settings with { SendErrorReports = value });

    [RelayCommand]
    private async Task ShowErrorReportExampleAsync() => await _services.Dialogs.ShowAsync(new ErrorReportExampleViewModel(_services.Context.ProductName, _services.Context.Version)).ConfigureAwait(true);

    private static string Overline(string text) => text.ToUpper(Text.Culture);

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckForUpdatesAsync() => await AgentRequests.SendAsync(_services, new CheckNow()).ConfigureAwait(true);

    [RelayCommand]
    private async Task ShowReleaseNotesAsync()
    {
        if (ReleaseNotes is { } notes)
        {
            await _services.Dialogs.ShowAsync(new ReleaseNotesDialogViewModel(WhatsNewText, notes, _services.Links)).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void OpenSite()
    {
        if (_services.Context.Site.SiteAddress is { } site)
        {
            _services.Links.Open(site);
        }
    }

    [RelayCommand]
    private void OpenSource()
    {
        if (_services.Context.SourceAddress is { } source)
        {
            _services.Links.Open(source);
        }
    }

    [RelayCommand]
    private void ReportProblem()
    {
        if (ProblemReports.Address(_services.Context) is { } address)
        {
            _services.Links.Open(address);
        }
    }

    [RelayCommand]
    private async Task OpenLogsAsync() => await AgentRequests.OpenFolderAsync(_services, new OpenFolder(AppFolder.Logs)).ConfigureAwait(true);

    [RelayCommand]
    private void OpenLicenses() => _services.Navigator.Navigate(new LicensesRoute());

    // A choice the agent refused shows the agent's settings again once the control that made it has finished taking it.
    private void Change(Func<SettingsView, SettingsView> change)
    {
        if (_syncing)
        {
            return;
        }

        _ = ChangeAsync();

        async Task ChangeAsync()
        {
            if (!await _editor.ChangeAsync(change).ConfigureAwait(true))
            {
                Dispatcher.UIThread.Post(Sync);
            }
        }
    }

    private void Sync()
    {
        var settings = _editor.Current;
        _syncing = true;
        StartWithSystem = settings.StartWithSystem;
        CloseBehavior = settings.CloseBehavior;
        BlurAdultContent = settings.BlurAdultContent;
        AutoInstallProjects = settings.AutoInstallProjects;
        AutoUpdateProjects = settings.AutoUpdateProjects;
        ReapplyAfterGameUpdate = settings.ReapplyAfterGameUpdate;
        ShowNotifications = settings.ShowNotifications;
        CreatorMode = settings.CreatorMode;
        AutoUpdateApp = settings.AutoUpdateApp;
        UpdatePrereleases = settings.UpdatePrereleases;
        SendErrorReports = settings.SendErrorReports;
        _syncing = false;
        Backups.Refresh();
        Teams.Refresh();
        Data.Refresh();
        ShowUpdate();
        var state = _services.Agent.State;
        SiteName = state.Catalogue.SiteName is { Length: > 0 } name ? name : _services.Context.Site.SiteAddress?.Host ?? "";
        Folders.Refresh();
    }

    private void ShowUpdate()
    {
        var update = _services.Agent.State.Update;
        VersionText = Text.Format(Strings.Shell_StatusVersion,
            ("version", update.CurrentVersion is { Length: > 0 } current ? current : _services.Context.Version));
        var version = update.Version ?? "";
        (UpdateIcon, UpdateTone, UpdateText) = update.State switch
        {
            AppUpdateState.Checking => (Icons.LoaderCircle, Tone.Accent, Strings.Settings_UpdateChecking),
            AppUpdateState.Downloading => (Icons.Download, Tone.Accent, Text.Format(Strings.Settings_UpdateDownloading, ("version", version),
                ("percent", Formats.Percent(update.Progress ?? 0)))),
            AppUpdateState.Available => (Icons.Download, Tone.Info, Text.Format(Strings.Settings_UpdateAvailable, ("version", version))),
            AppUpdateState.Ready => (Icons.Sparkles, Tone.Accent, Text.Format(Strings.Settings_UpdateReady, ("version", version))),
            AppUpdateState.Failed => (Icons.CircleX, Tone.Danger, Strings.Settings_UpdateFailed),
            AppUpdateState.Unavailable => (Icons.Info, Tone.Neutral, Strings.Settings_UpdateUnavailable),
            _ => (Icons.Clock, Tone.Neutral, update.CheckedAt is { } checkedAt
                ? Text.Format(Strings.Settings_UpdateChecked, ("time", Formats.RelativeTime(checkedAt, _services.Clock)))
                : Strings.Settings_UpdateNeverChecked),
        };
        IsUpdateBusy = update.State == AppUpdateState.Checking;
        ShowsUpdateProgress = update.State == AppUpdateState.Downloading;
        UpdateProgress = Math.Clamp(update.Progress ?? 0, 0, 100);
        ShowsCheck = update.State is not (AppUpdateState.Ready or AppUpdateState.Unavailable);
        CanUpdateApp = update.State != AppUpdateState.Unavailable;
        CanCheck = update.State is not (AppUpdateState.Checking or AppUpdateState.Downloading) && !_services.Agent.State.Activity.IsChecking;
        CheckText = update.State == AppUpdateState.Available ? Strings.Shell_StatusDownload : Strings.Settings_CheckUpdates;
        WhatsNewText = Text.Format(Strings.Settings_WhatsNew, ("version", version));
        ReleaseNotes = update.State is AppUpdateState.Available or AppUpdateState.Ready && !string.IsNullOrWhiteSpace(update.ReleaseNotes)
            ? update.ReleaseNotes
            : null;
    }

    private void OnAgentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IAgent.State) && _editor.TakeState())
        {
            Sync();
        }
        else if (e.PropertyName is nameof(IAgent.State))
        {
            Backups.Refresh();
            Data.Refresh();
            ShowUpdate();
            Folders.Refresh();
        }
    }

    private void OnJobsProgressed(object? sender, IReadOnlyList<JobView> jobs) => Backups.Progress(jobs);
}
