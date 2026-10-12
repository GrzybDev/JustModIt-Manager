using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Pages.Library;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>Backups that installs forgotten with their game left outside it, which only the player removes.</summary>
/// <param name="Backup">The leftover as the agent lists it.</param>
/// <param name="Detail">Its size and folder.</param>
/// <param name="RemoveName">What its remove button does, for screen readers.</param>
public sealed record LeftoverRow(LeftoverBackup Backup, string Detail, string RemoveName);

/// <summary>
/// Where the games' original files are kept: the choice of place, a folder the player picked, moving the backups made in
/// the earlier place, a place that is not there, and the backups of games that are gone.
/// </summary>
public sealed partial class BackupsViewModel : ObservableObject
{
    private readonly UiServices _services;
    private readonly SettingsEditor _editor;
    private readonly Action _showSettings;
    private int _moveTotal;
    private int _installCount = -1;
    private bool _syncing;

    /// <summary>Creates the section, which takes the settings and installs from the page.</summary>
    /// <param name="services">The window's services.</param>
    /// <param name="editor">Changes the agent's settings.</param>
    /// <param name="showSettings">Shows the settings again, after a choice that did not go through.</param>
    internal BackupsViewModel(UiServices services, SettingsEditor editor, Action showSettings)
    {
        _services = services;
        _editor = editor;
        _showSettings = showSettings;
    }

    [ObservableProperty]
    public partial BackupLocation Location { get; set; }

    /// <summary>The folder the player picked; null before one was.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomFolder))]
    public partial string? CustomFolder { get; private set; }

    public bool HasCustomFolder => CustomFolder is not null;

    [ObservableProperty]
    public partial bool ShowsFolder { get; private set; }

    /// <summary>The free space on the picked folder's drive; null while unknown.</summary>
    [ObservableProperty]
    public partial string? FreeSpace { get; private set; }

    /// <summary>Why the folder the player picked cannot hold backups; null when it can.</summary>
    [ObservableProperty]
    public partial string? FolderProblem { get; private set; }

    [ObservableProperty]
    public partial bool IsPicking { get; private set; }

    /// <summary>How many games' originals are kept, in a sentence; null while none are.</summary>
    [ObservableProperty]
    public partial string? Usage { get; private set; }

    /// <summary>The backups that are still in the earlier place and can be moved, in a sentence; null when there are none.</summary>
    [ObservableProperty]
    public partial string? Elsewhere { get; private set; }

    /// <summary>The backups left in an earlier place that cannot be reached now, in a sentence; null when there are none.</summary>
    [ObservableProperty]
    public partial string? StuckElsewhere { get; private set; }

    [ObservableProperty]
    public partial bool IsMoving { get; private set; }

    [ObservableProperty]
    public partial string MovingDetail { get; private set; } = "";

    [ObservableProperty]
    public partial double? MovingFraction { get; private set; }

    [ObservableProperty]
    public partial string MovingPercent { get; private set; } = "";

    /// <summary>Why some backups in the place the setting names cannot be reached, in a sentence; null while all can.</summary>
    [ObservableProperty]
    public partial string? Unavailable { get; private set; }

    /// <summary>Something more is said about the backups, under the choice of their place.</summary>
    [ObservableProperty]
    public partial bool HasDetails { get; private set; }

    public ObservableCollection<LeftoverRow> Leftovers { get; } = [];

    [ObservableProperty]
    public partial bool HasLeftovers { get; private set; }

    /// <summary>Reads the leftover backups again, and checks the picked folder, whose drive may have changed meanwhile.</summary>
    public void Load()
    {
        _ = LoadLeftoversAsync();
        if (_editor.Current is { BackupLocation: BackupLocation.CustomFolder, CustomBackupFolder: { } folder })
        {
            _ = CheckAsync(folder);
        }
    }

    /// <summary>Shows the agent's newest state and the settings the page shows.</summary>
    public void Refresh()
    {
        var settings = _editor.Current;
        var state = _services.Agent.State;
        _syncing = true;
        Location = settings.BackupLocation;
        _syncing = false;
        CustomFolder = settings.CustomBackupFolder;
        ShowsFolder = settings.BackupLocation == BackupLocation.CustomFolder;

        var installs = state.Games.SelectMany(game => game.Installs.Select(install => new GameInstall(game, install))).ToList();
        var unreachable = installs.Where(entry => entry.Install.BackupsUnavailable).ToList();
        var elsewhere = installs.Where(entry => !IsAt(entry.Install, settings)).ToList();
        var stuck = elsewhere.Intersect(unreachable).ToList();
        var held = unreachable.Except(stuck).ToList();
        Unavailable = held.Count > 0
            ? Text.Format(Strings.Settings_BackupsUnavailable, ("count", GameCount(held)),
                ("games", string.Join(", ", held.Select(entry => entry.Game.Name).Distinct())),
                ("folder", string.Join(", ", held.Select(entry => Folder(entry.Install)).Distinct(StringComparer.OrdinalIgnoreCase))))
            : null;

        var moving = state.Jobs.Where(job => job.Kind == JobKind.MoveBackups).ToList();
        IsMoving = moving.Count > 0 || state.PendingBackupMoves > 0;
        _moveTotal = IsMoving ? Math.Max(_moveTotal, moving.Count + state.PendingBackupMoves) : 0;
        var movable = elsewhere.Except(stuck).ToList();
        Elsewhere = IsMoving || movable.Count == 0 ? null
            : Text.Format(Strings.Settings_BackupsElsewhere, ("count", GameCount(movable)), ("place", Place(movable)));
        StuckElsewhere = stuck.Count == 0 ? null
            : Text.Format(Strings.Settings_BackupsElsewhereUnavailable, ("count", GameCount(stuck)), ("place", Place(stuck)));
        Usage = installs.Count > 0 && elsewhere.Count == 0 && unreachable.Count == 0 && !IsMoving
            ? Text.Format(Strings.Settings_BackupsKept, ("count", GameCount(installs)))
            : null;
        ShowMoving(moving.FirstOrDefault());
        HasDetails = Usage is not null || Elsewhere is not null || StuckElsewhere is not null || IsMoving || Unavailable is not null;

        if (installs.Count != _installCount)
        {
            // An install that goes may leave its backups behind, among the leftovers.
            if (_installCount >= 0)
            {
                _ = LoadLeftoversAsync();
            }

            _installCount = installs.Count;
        }
    }

    /// <summary>Moves the progress of a running move on.</summary>
    /// <param name="jobs">The jobs that moved on.</param>
    public void Progress(IReadOnlyList<JobView> jobs)
    {
        if (jobs.FirstOrDefault(job => job.Kind == JobKind.MoveBackups) is { } job)
        {
            ShowMoving(job);
        }
    }

    partial void OnLocationChanged(BackupLocation value)
    {
        if (!_syncing)
        {
            _ = ChooseAsync(value);
        }
    }

    private async Task ChooseAsync(BackupLocation location)
    {
        var chosen = location != BackupLocation.CustomFolder
            ? await _editor.ChangeAsync(settings => settings with { BackupLocation = location }).ConfigureAwait(true)
            : await UseFolderAsync(await PickAsync().ConfigureAwait(true)).ConfigureAwait(true);
        if (chosen)
        {
            if (location != BackupLocation.CustomFolder)
            {
                (FolderProblem, FreeSpace) = (null, null);
            }

            Refresh();
        }
        else
        {
            Dispatcher.UIThread.Post(_showSettings);
        }
    }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        if (await PickAsync().ConfigureAwait(true) is { } folder && await UseFolderAsync(folder).ConfigureAwait(true))
        {
            Refresh();
        }
    }

    [RelayCommand]
    private async Task MoveAsync()
    {
        _moveTotal = 0;
        await AgentRequests.SendAsync(_services, new MoveBackups()).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task StopMovingAsync() => await AgentRequests.SendAsync(_services, new StopMovingBackups()).ConfigureAwait(true);

    [RelayCommand]
    private async Task RemoveLeftoverAsync(LeftoverRow row)
    {
        var backup = row.Backup;
        if (!await _services.Dialogs.ConfirmAsync(new ConfirmDialogViewModel(
                Text.Format(Strings.Settings_RemoveLeftoverTitle, ("game", backup.GameName)),
                Text.Format(Strings.Settings_RemoveLeftoverMessage, ("folder", backup.Directory), ("size", Formats.FileSize(backup.Bytes))),
                Strings.Settings_RemoveLeftover, Icons.Trash, DialogTone.Danger)
            {
                ConfirmIcon = Icons.Trash,
            }).ConfigureAwait(true))
        {
            return;
        }

        if (await AgentRequests.SendAsync(_services, new RemoveLeftoverBackups([backup.RecordId])).ConfigureAwait(true))
        {
            await LoadLeftoversAsync().ConfigureAwait(true);
        }
    }

    private async Task<string?> PickAsync()
    {
        IsPicking = true;
        try
        {
            return await _services.Pickers.PickFolderAsync(Strings.Settings_PickBackupFolderTitle).ConfigureAwait(true);
        }
        finally
        {
            IsPicking = false;
        }
    }

    private async Task<bool> UseFolderAsync(string? folder)
    {
        if (folder is null || !await CheckAsync(folder).ConfigureAwait(true))
        {
            return false;
        }

        return await _editor.ChangeAsync(settings => settings with
        {
            BackupLocation = BackupLocation.CustomFolder,
            CustomBackupFolder = folder,
        }).ConfigureAwait(true);
    }

    private async Task<bool> CheckAsync(string folder)
    {
        var (sent, answer) = await AgentRequests.AskAsync(_services, new ValidateBackupFolder(folder)).ConfigureAwait(true);
        if (!sent || answer is not BackupFolderChecked check)
        {
            return false;
        }

        FolderProblem = check.Problem is { } problem ? Text.Format(Strings.Settings_BackupFolderProblem, ("problem", problem.ToString())) : null;
        FreeSpace = check.AvailableBytes is { } free
            ? Text.Format(Strings.Settings_FreeSpace, ("drive", Drive(folder)), ("space", Formats.FileSize(free)))
            : null;
        return check.Problem is null;
    }

    private async Task LoadLeftoversAsync()
    {
        try
        {
            if (await _services.Agent.RequestAsync(new GetLeftoverBackups()).ConfigureAwait(true) is LeftoverBackups found)
            {
                Leftovers.Clear();
                foreach (var backup in found.Backups.OrderBy(backup => backup.GameName, Text.Comparer))
                {
                    Leftovers.Add(new LeftoverRow(backup, GameWording.Join([Formats.FileSize(backup.Bytes), backup.Directory]),
                        Text.Format(Strings.Settings_RemoveLeftoverName, ("game", backup.GameName))));
                }

                HasLeftovers = Leftovers.Count > 0;
            }
        }
        // The list stays as it was; the page reads it again when it is shown again.
        catch (Exception exception) when (exception is IOException or TimeoutException or Protocol.Transport.RequestFailedException)
        {
        }
    }

    // The agent counts the games whose move waits apart from those whose move runs, one job per game.
    private void ShowMoving(JobView? job)
    {
        if (!IsMoving)
        {
            return;
        }

        var state = _services.Agent.State;
        var pending = state.PendingBackupMoves;
        var total = Math.Max(_moveTotal, pending + state.Jobs.Count(running => running.Kind == JobKind.MoveBackups));
        MovingDetail = GameWording.Join([job?.GameName ?? "",
            Text.Format(Strings.Settings_MovingCount, ("position", total - pending), ("total", total))]);
        MovingFraction = job?.Fraction;
        MovingPercent = job?.Fraction is { } fraction ? Formats.Percent((int)Math.Floor(fraction * 100)) : "";
    }

    private static int GameCount(IEnumerable<GameInstall> installs) => installs.Select(entry => entry.Game.Key).Distinct().Count();

    private static bool IsAt(InstallView install, SettingsView settings) => settings.BackupLocation switch
    {
        BackupLocation.CustomFolder => install.BackupLocation == BackupLocation.CustomFolder && settings.CustomBackupFolder is { } folder
            && PathComparison.IsWithin(folder, install.BackupDirectory),
        var location => install.BackupLocation == location,
    };

    private static string Place(IReadOnlyList<GameInstall> installs)
    {
        var places = installs.Select(entry => entry.Install.BackupLocation switch
        {
            BackupLocation.CustomFolder => Text.Format(Strings.Settings_PlaceFolder, ("folder", Folder(entry.Install))),
            BackupLocation.AppData => Strings.Settings_PlaceAppData,
            _ => Strings.Settings_PlaceGameFolders,
        }).Distinct().ToList();
        return places.Count == 1 ? places[0] : Strings.Settings_PlaceSeveral;
    }

    // A custom folder keeps each install's backups three levels down, in folders named after the app, the game and the install.
    private static string Folder(InstallView install) => install.BackupLocation == BackupLocation.CustomFolder
        ? Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(install.BackupDirectory))))
            ?? install.BackupDirectory
        : install.BackupDirectory;

    private static string Drive(string folder) => Path.GetPathRoot(folder)?.TrimEnd('\\', '/') ?? folder;

    private sealed record GameInstall(GameView Game, InstallView Install);
}
