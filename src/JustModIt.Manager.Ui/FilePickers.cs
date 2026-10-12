using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace JustModIt.Manager.Ui;

/// <summary>The system's pickers of folders and files, shown over the window.</summary>
public interface IFilePickers
{
    /// <summary>Lets the player pick a folder.</summary>
    /// <param name="title">What the picker asks for.</param>
    /// <returns>The folder's full path; null when the player cancelled or picked a place without one.</returns>
    Task<string?> PickFolderAsync(string title);
}

/// <summary>The pickers of the window's own platform, which show them without holding up the window.</summary>
/// <param name="window">The window the pickers belong to; null while there is none.</param>
internal sealed class StorageProviderPickers(Func<TopLevel?> window) : IFilePickers
{
    public async Task<string?> PickFolderAsync(string title)
    {
        if (window() is not { StorageProvider: { CanPickFolder: true } storage })
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title }).ConfigureAwait(true);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
