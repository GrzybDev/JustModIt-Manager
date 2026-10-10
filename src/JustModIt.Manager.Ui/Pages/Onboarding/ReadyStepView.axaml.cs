using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Pages.Onboarding;

internal sealed partial class ReadyStepView : UserControl
{
    private ReadyStep? _step;

    /// <summary>Creates the view of the last step, whose result fades in once the search ends.</summary>
    public ReadyStepView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Watch(TopLevel.GetTopLevel(this) is null ? null : DataContext as ReadyStep);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Watch(DataContext as ReadyStep);
    }

    // The step outlives its view, which the next step's view replaces.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Watch(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Watch(ReadyStep? step)
    {
        _step?.PropertyChanged -= OnStepChanged;
        _step = step;
        _step?.PropertyChanged += OnStepChanged;
    }

    private void OnStepChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReadyStep.IsSearching) && _step is { IsSearching: false })
        {
            Motion.FadeIn(Found);
        }
    }
}
