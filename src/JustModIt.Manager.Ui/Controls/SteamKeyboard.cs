using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JustModIt.Manager.Ui.Navigation;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// Steam's keyboard for the window's text fields in Game Mode, where no other keyboard shows, opened at a field tapped or
/// clicked, reached with Ctrl+F or confirmed with Enter, and closed once the focus leaves that field.
/// </summary>
internal sealed class SteamKeyboard
{
    private readonly Window _window;
    private readonly IRunningSteam _steam;
    private TextBox? _openFor;

    private SteamKeyboard(Window window, IRunningSteam steam)
    {
        _window = window;
        _steam = steam;
        // Fields and pages handle these keys and presses themselves, so the window listens to handled ones too. Focus alone
        // opens nothing, so a controller moving through a field with Tab or the arrows does not cover half the screen.
        window.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.LostFocusEvent, (_, e) => OnLostFocus(e.Source), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Opens Steam's keyboard for a window's text fields.</summary>
    /// <param name="window">The window whose fields it serves.</param>
    /// <param name="steam">The running Steam client, which shows the keyboard.</param>
    public static void Attach(Window window, IRunningSteam steam) => _ = new SteamKeyboard(window, steam);

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A tap on a field whose keyboard the player closed in Steam opens it again.
        if (Editable((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true)) is { } field)
        {
            Open(field);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && Editable(Focused()) is { } field && field != _openFor)
        {
            Open(field);
        }
        // The page moves the focus to its search on the same key, after this handler.
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Editable(Focused()) is { } search)
                {
                    Open(search);
                }
            });
        }
    }

    private void OnLostFocus(object? source)
    {
        if (source is not null && source == _openFor)
        {
            _openFor = null;
            _steam.Send("steam://close/keyboard");
        }
    }

    // Steam places its keyboard by the field, in the window's device pixels.
    private void Open(TextBox field)
    {
        if (field.TranslatePoint(default, _window) is not { } at)
        {
            return;
        }

        var scale = _window.RenderScaling;
        string Pixels(double value) => ((int)Math.Round(value * scale)).ToString(CultureInfo.InvariantCulture);
        _openFor = field;
        _steam.Send($"steam://open/keyboard?XPosition={Pixels(at.X)}&YPosition={Pixels(at.Y)}&Width={Pixels(field.Bounds.Width)}"
            + $"&Height={Pixels(field.Bounds.Height)}&Mode=0");
    }

    private object? Focused() => _window.FocusManager?.GetFocusedElement();

    private static TextBox? Editable(object? element) =>
        element is TextBox { IsReadOnly: false, IsEffectivelyEnabled: true } field ? field : null;
}
