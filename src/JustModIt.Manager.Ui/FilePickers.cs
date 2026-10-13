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
    /// <param name="startIn">The folder the picker opens in; null for the system's choice.</param>
    /// <returns>The folder's full path; null when the player cancelled or picked a place without one.</returns>
    Task<string?> PickFolderAsync(string title, string? startIn = null);

    /// <summary>Lets the player pick a file to open.</summary>
    /// <param name="title">What the picker asks for.</param>
    /// <param name="kind">The type of file offered; null for any.</param>
    /// <param name="startIn">The folder the picker opens in; null for the system's choice.</param>
    /// <returns>The file's full path; null when the player cancelled or picked a place without one.</returns>
    Task<string?> PickFileAsync(string title, FileKind? kind = null, string? startIn = null);

    /// <summary>Lets the player choose where to save a file of one type.</summary>
    /// <param name="title">What the picker asks for.</param>
    /// <param name="suggestedName">The file's name to start with, with its extension.</param>
    /// <param name="kind">The file's type.</param>
    /// <param name="startIn">The folder the picker opens in; null for the system's choice.</param>
    /// <returns>The file's full path; null when the player cancelled or picked a place without one.</returns>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, FileKind kind, string? startIn = null);
}

/// <summary>The pickers of the window's own platform, which show them without holding up the window.</summary>
/// <param name="window">The window the pickers belong to; null while there is none.</param>
internal sealed class StorageProviderPickers(Func<TopLevel?> window) : IFilePickers
{
    public async Task<string?> PickFolderAsync(string title, string? startIn = null)
    {
        if (window() is not { StorageProvider: { CanPickFolder: true } storage })
        {
            return null;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = await StartAsync(storage, startIn).ConfigureAwait(true),
        }).ConfigureAwait(true);
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string title, FileKind? kind = null, string? startIn = null)
    {
        if (window() is not { StorageProvider: { CanOpen: true } storage })
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            SuggestedStartLocation = await StartAsync(storage, startIn).ConfigureAwait(true),
            FileTypeFilter = kind is null ? null : [TypeOf(kind)],
        }).ConfigureAwait(true);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, FileKind kind, string? startIn = null)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (window() is not { StorageProvider: { CanSave: true } storage })
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedStartLocation = await StartAsync(storage, startIn).ConfigureAwait(true),
            SuggestedFileName = suggestedName,
            DefaultExtension = kind.Extension.TrimStart('.'),
            FileTypeChoices = [TypeOf(kind)],
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);
        return file?.TryGetLocalPath();
    }

    private static FilePickerFileType TypeOf(FileKind kind) => new(kind.Name) { Patterns = ["*" + kind.Extension] };

    // The provider looks the folder up on the disk, so it runs on the thread pool.
    private static async Task<IStorageFolder?> StartAsync(IStorageProvider storage, string? folder) =>
        folder is { Length: > 0 } && Path.IsPathFullyQualified(folder)
            ? await Task.Run(() => storage.TryGetFolderFromPathAsync(folder)).ConfigureAwait(true)
            : null;
}
