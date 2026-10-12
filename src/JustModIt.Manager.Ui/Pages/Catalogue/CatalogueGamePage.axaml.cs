using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.VisualTree;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Catalogue;

/// <summary>The view of <see cref="CatalogueGamePageViewModel"/>: its layout in a narrow window, and the status explanation's light dismiss.</summary>
public sealed partial class CatalogueGamePage : UserControl
{
    /// <summary>The status box's fill: the site's colour of the status, faint.</summary>
    public static readonly IValueConverter BoxTint =
        new FuncValueConverter<string, IBrush>(hex => new ImmutableSolidColorBrush(StatusColors.Parse(hex), 0.12));

    /// <summary>The status box's edge: the site's colour of the status, half strength.</summary>
    public static readonly IValueConverter BoxEdge =
        new FuncValueConverter<string, IBrush>(hex => new ImmutableSolidColorBrush(StatusColors.Parse(hex), 0.45));

    // Narrower than this, the cover shrinks and the site's button goes under the facts, as the smallest window's board shows.
    private const double CompactWidth = 760;

    /// <summary>Creates the view.</summary>
    public CatalogueGamePage()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        PageContent.SizeChanged += (_, e) => Fit(e.NewSize.Width);
    }

    public static string AbsentText => Text.Format(Strings.Catalogue_AbsentText, ("library", Strings.Shell_Library));

    private CatalogueGamePageViewModel? Page => DataContext as CatalogueGamePageViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Fit(PageContent.Bounds.Width);
    }

    private void Fit(double width)
    {
        if (Page is { } page && width > 0)
        {
            page.IsCompact = width < CompactWidth;
        }
    }

    // A click anywhere outside the status's explanation closes it, as a popup would close.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Page is { IsStatusOpen: true } page && e.Source is Visual source
            && !StatusPopover.IsVisualAncestorOf(source) && !StatusChip.IsVisualAncestorOf(source) && source != StatusChip)
        {
            page.IsStatusOpen = false;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Page is { IsStatusOpen: true } page)
        {
            page.IsStatusOpen = false;
            e.Handled = true;
        }
    }
}
