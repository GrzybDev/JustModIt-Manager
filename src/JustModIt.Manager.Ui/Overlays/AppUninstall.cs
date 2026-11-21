using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>What takes the place of the window's pages once the app is uninstalled.</summary>
/// <param name="Title">That the app is uninstalled.</param>
/// <param name="Folders">The folders the app's data stayed in, each written from the home folder.</param>
/// <param name="Installs">That the projects stayed in the games; null when no game held one.</param>
public sealed record AppUninstalled(string Title, IReadOnlyList<string> Folders, string? Installs);

/// <summary>
/// Uninstalling the app itself, where the agent offers it: one question, then each step as the agent takes it, and once the
/// app is gone the uninstalled view in place of the window's pages.
/// </summary>
/// <param name="services">The window's services.</param>
public sealed partial class AppUninstall(UiServices services) : ObservableObject
{
    private AppUninstallDialogViewModel? _dialog;

    /// <summary>What takes the place of the pages once the app is uninstalled; null until then.</summary>
    [ObservableProperty]
    public partial AppUninstalled? Uninstalled { get; private set; }

    /// <summary>The player asked to see the section that uninstalls every project, which puts their originals back first.</summary>
    public event EventHandler? AppDataRequested;

    /// <summary>
    /// Whether the uninstall is offered: where the app uninstalls itself, outside Game Mode, which would be left with a shortcut
    /// to nothing and a window with no way back.
    /// </summary>
    /// <param name="state">The agent's state.</param>
    public bool IsOffered(AppStateView state) => state.AppUninstall is not null && !services.Context.InGameMode;

    /// <summary>Asks, then uninstalls the app; nothing happens where it is not offered or while the question is open.</summary>
    public async Task ConfirmAsync()
    {
        var state = services.Agent.State;
        if (!IsOffered(state) || _dialog is not null)
        {
            return;
        }

        var dialog = _dialog = new AppUninstallDialogViewModel(services, state.AppUninstall!);
        var answer = await services.Dialogs.ShowAsync(dialog).ConfigureAwait(true);
        _dialog = null;
        if (answer == AppUninstallAnswer.AppData)
        {
            AppDataRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (answer == AppUninstallAnswer.Uninstalled)
        {
            var offer = state.AppUninstall!;
            // The logs' own folder goes with the folder above it, which holds nothing else and stays whole.
            Uninstalled = new AppUninstalled(Text.Format(Strings.Uninstalled_Title, ("product", services.Context.ProductName)),
                [.. new[] { offer.DataFolder, offer.CacheFolder, Path.GetDirectoryName(offer.LogsFolder)! }.Select(GameWording.FromHome)],
                dialog.HasInstalls ? Text.Format(Strings.Uninstalled_InstallsKept, ("count", dialog.InstalledGames)) : null);
        }
    }

    /// <summary>Completes once no uninstall runs, which closing the window waits for.</summary>
    /// <returns>Whether the app is uninstalled, known as soon as the run ends.</returns>
    public Task<bool> WhenSettledAsync() => _dialog?.Settled ?? Task.FromResult(Uninstalled is not null);
}
