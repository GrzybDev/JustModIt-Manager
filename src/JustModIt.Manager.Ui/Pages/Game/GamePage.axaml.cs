using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>The view of <see cref="GamePageViewModel"/>: its layout in a short window, and the status explanation's light dismiss.</summary>
public sealed partial class GamePage : UserControl
{
    /// <summary>A band with a close button keeps less room on its right, as the button sits in the corner.</summary>
    public static readonly IValueConverter BandPadding =
        new FuncValueConverter<bool, Thickness>(dismissable => dismissable ? new Thickness(24, 22, 12, 22) : new Thickness(24, 22));

    // A window this short cannot show the panel's foot without scrolling, so its next step moves to its head.
    private const double CompactHeight = 720;

    /// <summary>Creates the view.</summary>
    public GamePage()
    {
        InitializeComponent();
        LightDismiss.Connect(this, StatusPopover, StatusChip, () => DataContext is GamePageViewModel { IsStatusOpen: true },
            () => ((GamePageViewModel)DataContext!).IsStatusOpen = false);
        SizeChanged += (_, e) =>
        {
            if (DataContext is GamePageViewModel page)
            {
                page.IsCompact = e.NewSize.Height < CompactHeight;
            }
        };
    }

    public static string BackText => Text.Format(Strings.GamePage_BackToLibrary, ("library", Strings.Shell_Library));

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is GamePageViewModel page && Bounds.Height > 0)
        {
            page.IsCompact = Bounds.Height < CompactHeight;
        }
    }
}
