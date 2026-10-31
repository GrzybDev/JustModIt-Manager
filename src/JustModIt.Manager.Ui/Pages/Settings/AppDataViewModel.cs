using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>The app's own data: the space it takes, clearing what can be fetched again and uninstalling every project.</summary>
public sealed partial class AppDataViewModel : ObservableObject
{
    private readonly UiServices _services;
    private AppDataUsage? _usage;
    private bool _jobsRun;
    private int _uninstallable;
    private int _unreachable;

    /// <summary>Creates the section, which takes the agent's state from the page.</summary>
    /// <param name="services">The window's services.</param>
    internal AppDataViewModel(UiServices services) => _services = services;

    /// <summary>How much the data folder takes, in a sentence; says it is being measured until the agent answers.</summary>
    [ObservableProperty]
    public partial string Usage { get; private set; } = Strings.Settings_DiskUsageMeasuring;

    [ObservableProperty]
    public partial bool IsMeasured { get; private set; }

    [ObservableProperty]
    public partial string Downloads { get; private set; } = "";

    [ObservableProperty]
    public partial string ImagesAndCatalogue { get; private set; } = "";

    [ObservableProperty]
    public partial string Logs { get; private set; } = "";

    /// <summary>What clearing the cache does, why it waits, or what it freed.</summary>
    [ObservableProperty]
    public partial string ClearText { get; private set; } = Strings.Settings_ClearCacheDescription;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial bool CanClear { get; private set; }

    /// <summary>What uninstalling every project does, how many games are still to go, or that nothing is installed.</summary>
    [ObservableProperty]
    public partial string UninstallText { get; private set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UninstallAllCommand))]
    public partial bool CanUninstallAll { get; private set; }

    /// <summary>Measures the data folder again.</summary>
    public void Load() => _ = MeasureAsync();

    /// <summary>Shows the agent's newest state: the running jobs and the installs.</summary>
    public void Refresh()
    {
        var state = _services.Agent.State;
        _jobsRun = state.Jobs.Count > 0;
        CanClear = !_jobsRun;
        if (_jobsRun)
        {
            ClearText = Strings.Settings_ClearCacheBusy;
        }
        else if (ClearText == Strings.Settings_ClearCacheBusy)
        {
            ClearText = Strings.Settings_ClearCacheDescription;
        }

        // The agent leaves out the installs whose originals cannot be reached or were deleted, whatever their status tells first.
        var installed = state.Games.Where(game => game.Installs.Count > 0).ToList();
        _uninstallable = installed.Count(game => game.Installs.Any(install => !LacksOriginals(install)));
        _unreachable = installed.Count(game => game.Installs.Any(LacksOriginals));
        var uninstalling = state.Jobs.Where(job => job.Kind == JobKind.Uninstall).Select(job => job.Game).Distinct().Count()
            + state.PendingUninstalls;
        UninstallText = installed.Count == 0 ? Strings.Settings_UninstallAllNothing
            : uninstalling > 0 ? Text.Format(Strings.Settings_UninstallAllRunning, ("count", uninstalling))
            : _uninstallable == 0 ? Strings.Settings_UninstallAllUnreachable
            : Text.Format(Strings.Settings_UninstallAllDescription, ("count", _uninstallable));
        CanUninstallAll = _uninstallable > 0 && uninstalling == 0;
    }

    [RelayCommand]
    private async Task OpenFolderAsync() => await AgentRequests.OpenFolderAsync(_services, new OpenFolder(AppFolder.Data)).ConfigureAwait(true);

    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearAsync()
    {
        var before = _usage;
        if (await AgentRequests.SendAsync(_services, new ClearCache(), Strings.Settings_ClearCacheBusy).ConfigureAwait(true)
            && await MeasureAsync().ConfigureAwait(true) is { } after && before is not null)
        {
            var freed = before.Downloads + before.ImagesAndCatalogue - after.Downloads - after.ImagesAndCatalogue;
            ClearText = Text.Format(Strings.Settings_CacheCleared, ("size", Formats.FileSize(Math.Max(0, freed))));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUninstallAll))]
    private async Task UninstallAllAsync()
    {
        var message = Text.Format(Strings.Settings_UninstallAllMessage, ("count", _uninstallable));
        if (_unreachable > 0)
        {
            message += " " + Text.Format(Strings.Settings_UninstallAllSkipped, ("count", _unreachable));
        }

        if (await _services.Dialogs.ConfirmAsync(new ConfirmDialogViewModel(Strings.Settings_UninstallAllTitle, message,
                Strings.Settings_UninstallAllButton, Icons.Trash, DialogTone.Danger) { ConfirmIcon = Icons.Trash }).ConfigureAwait(true))
        {
            await AgentRequests.SendAsync(_services, new UninstallEverything()).ConfigureAwait(true);
        }
    }

    private async Task<AppDataUsage?> MeasureAsync()
    {
        try
        {
            if (await _services.Agent.RequestAsync(new GetAppDataUsage()).ConfigureAwait(true) is not AppDataUsage usage)
            {
                return null;
            }

            _usage = usage;
            Usage = Text.Format(Strings.Settings_DiskUsageText, ("drive", BackupsViewModel.Drive(usage.DataFolder)),
                ("size", Formats.FileSize(usage.Total)));
            Downloads = Formats.FileSize(usage.Downloads);
            ImagesAndCatalogue = Formats.FileSize(usage.ImagesAndCatalogue);
            Logs = Formats.FileSize(usage.Logs);
            IsMeasured = true;
            return usage;
        }
        // The page measures again when it is shown again.
        catch (Exception exception) when (exception is IOException or TimeoutException or RequestFailedException)
        {
            if (!IsMeasured)
            {
                Usage = Strings.Settings_DiskUsageUnknown;
            }

            return null;
        }
    }

    private static bool LacksOriginals(InstallView install) => install.BackupsUnavailable || install.MissingOriginals.Count > 0;
}
