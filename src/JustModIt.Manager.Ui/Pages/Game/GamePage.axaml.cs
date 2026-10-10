using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>The view of <see cref="GamePageViewModel"/>: its layout in a short window, and the status explanation's light dismiss.</summary>
public sealed partial class GamePage : UserControl
{
    /// <summary>A band with a close button keeps less room on its right, as the button sits in the corner.</summary>
    public static readonly IValueConverter BandPadding =
        new FuncValueConverter<bool, Thickness>(dismissable => dismissable ? new Thickness(24, 22, 12, 22) : new Thickness(24, 22));

    /// <summary>The description's toggle: more while it is folded, less once it is open.</summary>
    public static readonly IValueConverter MoreOrLess =
        new FuncValueConverter<bool, string>(open => open ? Strings.Library_ShowLess : Strings.GamePage_ShowMore);

    /// <summary>A support link's label from its platform's name.</summary>
    public static readonly IValueConverter SupportText =
        new FuncValueConverter<string, string>(platform => Text.Format(Strings.GamePage_Support, ("platform", platform ?? "")));

    // A window this short cannot show the panel's foot without scrolling, so its next step moves to its head.
    private const double CompactHeight = 720;

    /// <summary>Creates the view.</summary>
    public GamePage()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        SizeChanged += (_, e) =>
        {
            if (DataContext is GamePageViewModel page)
            {
                page.IsCompact = e.NewSize.Height < CompactHeight;
            }
        };
    }

    public static string NotFoundText => Text.Format(Strings.GamePage_NotFoundText, ("library", Strings.Shell_Library));

    public static string BackText => Text.Format(Strings.GamePage_BackToLibrary, ("library", Strings.Shell_Library));

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is GamePageViewModel page && Bounds.Height > 0)
        {
            page.IsCompact = Bounds.Height < CompactHeight;
        }
    }

    // A click anywhere outside the status's explanation closes it, as a popup would close.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is GamePageViewModel { IsStatusOpen: true } page && e.Source is Visual source
            && !StatusPopover.IsVisualAncestorOf(source) && !StatusChip.IsVisualAncestorOf(source) && source != StatusChip)
        {
            page.IsStatusOpen = false;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is GamePageViewModel { IsStatusOpen: true } page)
        {
            page.IsStatusOpen = false;
            e.Handled = true;
        }
    }
}
