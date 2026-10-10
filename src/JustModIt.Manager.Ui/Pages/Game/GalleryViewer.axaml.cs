using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>The view of <see cref="GalleryViewerViewModel"/>: nearly as large as the window, arrows moving through the pictures.</summary>
public sealed partial class GalleryViewer : UserControl
{
    private TopLevel? _top;

    /// <summary>Creates the view.</summary>
    public GalleryViewer()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        if (_top is not null)
        {
            _top.SizeChanged += OnWindowSizeChanged;
            Fit(_top.ClientSize);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_top is not null)
        {
            _top.SizeChanged -= OnWindowSizeChanged;
            _top = null;
        }
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) => Fit(e.NewSize);

    // The viewer leaves the title bar and a margin of the window around it, so it reads as part of the window.
    private void Fit(Size window)
    {
        Frame.Width = Math.Max(480, window.Width - 96);
        Frame.Height = Math.Max(360, window.Height - 112);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not GalleryViewerViewModel viewer)
        {
            return;
        }

        if (e.Key == Key.Left)
        {
            viewer.PreviousCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            viewer.NextCommand.Execute(null);
            e.Handled = true;
        }
    }
}
