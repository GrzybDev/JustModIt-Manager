using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Settings;

/// <summary>A folder the player added because detection did not find a game, with what was found in it.</summary>
/// <param name="Folder">The folder as the settings keep it.</param>
/// <param name="Detail">What kind of folder it is and what was found there.</param>
/// <param name="RemoveName">What its remove button does, for screen readers.</param>
public sealed record AddedFolderRow(LibraryFolderView Folder, string Detail, string RemoveName);

/// <summary>The folders the player added in the help for a missing game, which can be taken off the list again.</summary>
/// <param name="services">The window's services.</param>
public sealed partial class AddedFoldersViewModel(UiServices services) : ObservableObject
{
    private DetectionReportView? _report;
    private DateTimeOffset? _reportFor;

    public string Description { get; } = Text.Format(Strings.Settings_AddedFoldersDescription, ("help", Strings.Shell_PageUndetected));

    public ObservableCollection<AddedFolderRow> Rows { get; } = [];

    [ObservableProperty]
    public partial bool HasFolders { get; private set; }

    /// <summary>Reads what detection found again after a new scan, for the games in the folders.</summary>
    public void Load()
    {
        var library = services.Agent.State.Library;
        if (library.AddedFolders.Count > 0 && library.ScannedAt is { } scannedAt && scannedAt != _reportFor)
        {
            _reportFor = scannedAt;
            _ = LoadReportAsync();
        }
    }

    /// <summary>Shows the folders the agent's newest state lists.</summary>
    public void Refresh()
    {
        var state = services.Agent.State;
        var rows = state.Library.AddedFolders.Select(folder => new AddedFolderRow(folder, Detail(folder, state),
            Text.Format(Strings.Settings_RemoveFolderName, ("folder", folder.Path)))).ToList();
        if (!rows.SequenceEqual(Rows))
        {
            Rows.Clear();
            rows.ForEach(Rows.Add);
        }

        HasFolders = Rows.Count > 0;
        Load();
    }

    [RelayCommand]
    private async Task RemoveAsync(AddedFolderRow row) =>
        await AgentRequests.SendAsync(services, new RemoveLibraryFolder(row.Folder.Store, row.Folder.Path)).ConfigureAwait(true);

    private string Detail(LibraryFolderView folder, AppStateView state)
    {
        var kind = Text.Format(Strings.Settings_FolderKind, ("store", folder.Store), ("name", folder.Store));
        var library = _report?.Stores.Where(store => store.Store == folder.Store).SelectMany(store => store.Libraries)
            .FirstOrDefault(library => library.IsAdded && string.Equals(library.Path, folder.Path, StringComparison.OrdinalIgnoreCase));
        if (library is null)
        {
            return kind;
        }

        var offered = state.Games.Count(game => game.Detected?.LibraryId == library.Id);
        return GameWording.Join([kind, Text.Format(Strings.Settings_FolderGames, ("games", library.GameCount), ("projects", offered))]);
    }

    private async Task LoadReportAsync()
    {
        try
        {
            if (await services.Agent.RequestAsync(new GetDetectionReport()).ConfigureAwait(true) is DetectionReportResult result)
            {
                _report = result.Report;
                Refresh();
            }
        }
        // The folders show without their games until the next scan.
        catch (Exception exception) when (exception is IOException or TimeoutException or RequestFailedException)
        {
            _reportFor = null;
        }
    }
}
