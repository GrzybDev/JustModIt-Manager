using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace JustModIt.Manager.Ui.Pages.Onboarding;

/// <summary>The first-run setup's card over the wall of covers, with the steps and the buttons that move between them.</summary>
internal sealed partial class OnboardingView : UserControl
{
    /// <summary>Creates the view, which shows the setup it is given as its data.</summary>
    public OnboardingView()
    {
        InitializeComponent();
        BackButton.PropertyChanged += OnButtonChanged;
        SkipButton.PropertyChanged += OnButtonChanged;
    }

    // The focus starts on the way forward, so Enter and Alt+Left work on the setup at once; the ring shows only once the
    // keyboard moves it.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => NextButton.Focus(), DispatcherPriority.Loaded);
    }

    // A button that goes with the step it belonged to hands the focus on to the way forward, ring and all.
    private void OnButtonChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && e.NewValue is false && sender is Button { IsFocused: true } button)
        {
            NextButton.Focus(button.Classes.Contains(":focus-visible") ? NavigationMethod.Tab : NavigationMethod.Unspecified);
        }
    }
}
