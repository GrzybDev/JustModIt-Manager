using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace JustModIt.Manager.Ui;

/// <summary>A type of file the pickers offer.</summary>
/// <param name="Name">How the picker names the type.</param>
/// <param name="Extension">The type's extension, with its dot.</param>
public sealed record FileKind(string Name, string Extension);

/// <summary>The system's pickers of folders and files, shown over the window.</summary>
public interface IFilePickers
{
    /// <summary>Lets the player pick a folder.</summary>
    /// <param name="title">What the picker asks for.</param>
    /// <returns>The folder's full path; null when the player cancelled or picked a place without one.</returns>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Lets the player choose where to save a file of one type.</summary>
    /// <param name="title">What the picker asks for.</param>
    /// <param name="suggestedName">The file's name to start with, with its extension.</param>
    /// <param name="kind">The file's type.</param>
    /// <returns>The file's full path; null when the player cancelled or picked a place without one.</returns>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, FileKind kind);
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

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, FileKind kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (window() is not { StorageProvider: { CanSave: true } storage })
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = kind.Extension.TrimStart('.'),
            FileTypeChoices = [new FilePickerFileType(kind.Name) { Patterns = ["*" + kind.Extension] }],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);
        return file?.TryGetLocalPath();
    }
}
