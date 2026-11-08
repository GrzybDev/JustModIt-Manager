using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>The view of <see cref="ProjectPageViewModel"/>.</summary>
public sealed partial class ProjectPage : UserControl
{
    // Below this width the table and the bar keep too little room beside the panel, so the panel stands over them.
    private const double OverlayBelow = 1000;

    private ProjectPageViewModel? _page;
    private CreatorSession? _session;
    private (string Opposite, object? Parameter, NavigationMethod How)? _pressed;

    /// <summary>Creates the view.</summary>
    public ProjectPage()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Lay(e.NewSize.Width < OverlayBelow);
        // A click on another cell picks it rather than closing the panel.
        LightDismiss.Connect(this, Aside, TabContent, () => _page is { IsPanelOpen: true, IsOverlay: true }, () => _page!.IsPanelOpen = false);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(Button.ClickEvent, OnClicked);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _page = DataContext as ProjectPageViewModel;
        if (_page is not null)
        {
            _page.PropertyChanged += OnPageChanged;
            _page.Build.PropertyChanged += OnBuildChanged;
            _session = _page.Session;
            _session?.PropertyChanged += OnSessionChanged;
            Lay(Bounds.Width is > 0 and < OverlayBelow);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_page is not null)
        {
            _page.PropertyChanged -= OnPageChanged;
            _page.Build.PropertyChanged -= OnBuildChanged;
            _session?.PropertyChanged -= OnSessionChanged;
            (_page, _session) = (null, null);
        }
    }

    // The panel takes the focus when it opens and gives it back to its cell when it closes.
    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectPageViewModel.IsPanelOpen) && _page is { } page)
        {
            (page.IsPanelOpen ? ClosePanel : CellButton(page.Cell))?.Focus(NavigationMethod.Tab);
        }
    }

    // A long run scrolls its list, which keeps the package being built in sight.
    private void OnBuildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BuildViewModel.CurrentRow) && _page?.Build.CurrentRow is { } row)
        {
            Dispatcher.UIThread.Post(() => RunRows.ContainerFromItem(row)?.BringIntoView(), DispatcherPriority.Loaded);
        }
    }

    // The pressed button's row goes with the next plan, so what it was is taken down now.
    private void OnClicked(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button button && (button.Classes.Contains("accept") || button.Classes.Contains("undoAccept")))
        {
            _pressed = (button.Classes.Contains("accept") ? "undoAccept" : "accept", button.CommandParameter,
                button.Classes.Contains(":focus-visible") ? NavigationMethod.Tab : NavigationMethod.Unspecified);
        }
    }

    // A pressed acceptance hides with the plan that holds it, and the keyboard's focus would go with it, so the focus goes to the row's
    // way back, which the same plan shows.
    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CreatorSession.Review) || _pressed is not var (opposite, parameter, how))
        {
            return;
        }

        _pressed = null;
        // The row's buttons change, and a row may move to another list, with the layout that follows the plan.
        void MoveFocus(object? laidOut, EventArgs args)
        {
            LayoutUpdated -= MoveFocus;
            this.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.Classes.Contains(opposite) && button.IsEffectivelyVisible && Equals(button.CommandParameter, parameter))?.Focus(how);
        }

        LayoutUpdated += MoveFocus;
    }

    private void Lay(bool overlay)
    {
        Classes.Set("overlay", overlay);
        Grid.SetColumn(Aside, overlay ? 0 : 1);
        Grid.SetColumnSpan(Aside, overlay ? 2 : 1);
        if (_page is not null)
        {
            _page.IsOverlay = overlay;
        }
    }

    // The arrows move between the table's cells, row by row over the versions.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_page is not { IsTable: true } page || e.Source is not Button { DataContext: CellViewModel cell } || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var cells = page.Cells.ToList();
        var index = cells.IndexOf(cell) + e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -page.Heads.Count,
            Key.Down => page.Heads.Count,
            _ => 0,
        };
        if (index != cells.IndexOf(cell) && index >= 0 && index < cells.Count && CellButton(cells[index]) is { } next)
        {
            next.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }
    }

    private Button? CellButton(CellViewModel? cell) =>
        this.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Classes.Contains("cell") && button.DataContext == cell);
}
