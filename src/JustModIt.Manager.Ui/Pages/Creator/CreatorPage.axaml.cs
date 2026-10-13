using Avalonia;
using Avalonia.Controls;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The view of <see cref="CreatorPageViewModel"/>, which puts the start's explanation under it in a narrow window.</summary>
public sealed partial class CreatorPage : UserControl
{
    // Below this width the start's text beside the explanation would wrap into a column too narrow to read.
    private const double NarrowStart = 860;

    /// <summary>Creates the view.</summary>
    public CreatorPage()
    {
        InitializeComponent();
        StartGrid.SizeChanged += (_, e) => Arrange(e.NewSize.Width < NarrowStart);
    }

    public static string HowItWorksTitle => Strings.Creator_HowItWorks.ToUpper(Text.Culture);

    public static string RecentTitle => Strings.Creator_Recent.ToUpper(Text.Culture);

    public static string DetectsTitle => Strings.Creator_Detects.ToUpper(Text.Culture);

    /// <summary>The label of the buttons that stop the work under way, as the dialogs' own.</summary>
    public static string StopText => Strings.Dialog_Cancel;

    private void Arrange(bool narrow)
    {
        Grid.SetColumn(HowItWorksBox, narrow ? 0 : 1);
        Grid.SetRow(HowItWorksBox, narrow ? 1 : 0);
        HowItWorksBox.Width = narrow ? double.NaN : 404;
        HowItWorksBox.Margin = narrow ? new Thickness(0, 24, 0, 0) : default;
        StartGrid.ColumnSpacing = narrow ? 0 : 32;
    }
}
