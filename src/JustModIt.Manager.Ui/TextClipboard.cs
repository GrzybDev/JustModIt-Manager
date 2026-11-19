using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace JustModIt.Manager.Ui;

/// <summary>The system's clipboard, which the window puts text on for the player to paste elsewhere.</summary>
public interface ITextClipboard
{
    /// <summary>Puts text on the clipboard in place of what it held.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether the clipboard took it.</returns>
    Task<bool> SetTextAsync(string text);
}

/// <summary>The clipboard of the window's own platform.</summary>
/// <param name="window">The window the clipboard belongs to; null while there is none.</param>
internal sealed class WindowClipboard(Func<TopLevel?> window) : ITextClipboard
{
    public async Task<bool> SetTextAsync(string text)
    {
        if (window()?.Clipboard is not { } clipboard)
        {
            return false;
        }

        await clipboard.SetTextAsync(text).ConfigureAwait(true);
        return true;
    }
}
