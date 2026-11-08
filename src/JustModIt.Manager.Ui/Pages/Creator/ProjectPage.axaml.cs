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

    /// <summary>Creates the view.</summary>
    public ProjectPage()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Lay(e.NewSize.Width < OverlayBelow);
        // A click on another cell picks it rather than closing the panel.
        LightDismiss.Connect(this, Aside, TabContent, () => _page is { IsPanelOpen: true, IsOverlay: true }, () => _page!.IsPanelOpen = false);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _page = DataContext as ProjectPageViewModel;
        if (_page is not null)
        {
            _page.PropertyChanged += OnPageChanged;
            _page.Build.PropertyChanged += OnBuildChanged;
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
            _page = null;
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
