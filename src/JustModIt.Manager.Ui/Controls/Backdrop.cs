using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// The backdrop behind a page's head, baked by the image service into one bitmap per recipe and width step, so nothing
/// blends or blurs live. It draws at its natural size from the top left and fades in when a bake arrives.
/// </summary>
public sealed class Backdrop : Control
{
    public static readonly StyledProperty<BackdropRecipe?> RecipeProperty =
        AvaloniaProperty.Register<Backdrop, BackdropRecipe?>(nameof(Recipe));

    // Widths round up to this step, so resizing the window bakes again only now and then.
    private const int WidthStep = 128;

    private Bitmap? _bitmap;
    private double _scale = 1;
    private (BackdropRecipe Recipe, PixelSize Size)? _wanted;
    private CancellationTokenSource? _loading;

    /// <summary>What the backdrop is made of; null draws nothing.</summary>
    public BackdropRecipe? Recipe
    {
        get => GetValue(RecipeProperty);
        set => SetValue(RecipeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_bitmap is { } bitmap)
        {
            var size = new Size(bitmap.PixelSize.Width / _scale, bitmap.PixelSize.Height / _scale);
            context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(size));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == RecipeProperty || change.Property == BoundsProperty || change.Property == RemoteImage.LoaderProperty)
        {
            Load();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Load();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _loading?.Cancel();
        _loading = null;
        _wanted = null;
    }

    private void Load()
    {
        if (Recipe is not { } recipe || GetValue(RemoteImage.LoaderProperty) is not { } loader || Bounds.Width <= 0
            || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        var scale = top.RenderScaling;
        var width = (int)Math.Ceiling(Bounds.Width * scale / WidthStep) * WidthStep;
        var size = new PixelSize(width, (int)Math.Ceiling(BackdropRecipe.Height * scale));
        if (_wanted == (recipe, size))
        {
            return;
        }

        _wanted = (recipe, size);
        _loading?.Cancel();
        _ = LoadAsync(loader, recipe, size, scale);
    }

    private async Task LoadAsync(IImageLoader loader, BackdropRecipe recipe, PixelSize size, double scale)
    {
        using var loading = new CancellationTokenSource();
        _loading = loading;
        var cancellationToken = loading.Token;
        Bitmap? bitmap;
        var task = loader.LoadBackdropAsync(recipe, size, scale, cancellationToken);
        var arrivedAtOnce = task.IsCompleted;
        try
        {
            bitmap = await task;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (_loading == loading)
            {
                _loading = null;
            }
        }

        if (cancellationToken.IsCancellationRequested || bitmap is null)
        {
            return;
        }

        _bitmap = bitmap;
        _scale = scale;
        InvalidateVisual();
        if (!arrivedAtOnce)
        {
            Motion.FadeIn(this);
        }
    }
}
