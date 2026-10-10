using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// An image of the site, loaded once it comes near the visible part of its scroll area and decoded to the size it is
/// shown at. It fills its box, cropping what does not fit unless <see cref="Stretch"/> says otherwise, and fades in on
/// arrival; the <c>:loading</c>, <c>:loaded</c> and <c>:failed</c> pseudo-classes let a page show a placeholder.
/// </summary>
public sealed class RemoteImage : Control
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<RemoteImage, string?>(nameof(Source));

    /// <summary>An address tried when the source does not load, such as a film's smaller still.</summary>
    public static readonly StyledProperty<string?> FallbackProperty = AvaloniaProperty.Register<RemoteImage, string?>(nameof(Fallback));

    public static readonly StyledProperty<Stretch> StretchProperty =
        AvaloniaProperty.Register<RemoteImage, Stretch>(nameof(Stretch), Stretch.UniformToFill);

    /// <summary>The image shows as a soft blur of its colours, baked when it is decoded, as an adult game's cover is veiled.</summary>
    public static readonly StyledProperty<bool> IsBlurredProperty = AvaloniaProperty.Register<RemoteImage, bool>(nameof(IsBlurred));

    /// <summary>
    /// The control takes the picture's own proportions and at most its width, as a picture in a text does; until the
    /// picture arrives it holds a 16:9 place, and one that cannot be shown takes none.
    /// </summary>
    public static readonly StyledProperty<bool> FitsPictureProperty = AvaloniaProperty.Register<RemoteImage, bool>(nameof(FitsPicture));

    public static readonly DirectProperty<RemoteImage, PixelSize> NaturalSizeProperty =
        AvaloniaProperty.RegisterDirect<RemoteImage, PixelSize>(nameof(NaturalSize), image => image.NaturalSize);

    /// <summary>The service images load through, set once on the window and inherited by every image in it.</summary>
    public static readonly AttachedProperty<IImageLoader?> LoaderProperty =
        AvaloniaProperty.RegisterAttached<RemoteImage, Control, IImageLoader?>("Loader", inherits: true);

    // Images this far beyond the visible part start loading before they scroll in.
    private const double Lookahead = 600;

    private const double PlaceholderRatio = 0.5625;

    private RemotePicture? _picture;
    private PixelSize _naturalSize;
    private CancellationTokenSource? _loading;
    private string? _loadedSource;
    private PixelSize _loadedSize;
    private bool _isNearView;
    private bool _hasFailed;

    static RemoteImage()
    {
        AffectsRender<RemoteImage>(StretchProperty);
        AffectsMeasure<RemoteImage>(FitsPictureProperty);
    }

    /// <summary>Creates an image that shows nothing until it has a source and comes near the view.</summary>
    public RemoteImage() => EffectiveViewportChanged += OnEffectiveViewportChanged;

    /// <summary>The image's absolute address; null or empty shows nothing.</summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string? Fallback
    {
        get => GetValue(FallbackProperty);
        set => SetValue(FallbackProperty, value);
    }

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public bool IsBlurred
    {
        get => GetValue(IsBlurredProperty);
        set => SetValue(IsBlurredProperty, value);
    }

    public bool FitsPicture
    {
        get => GetValue(FitsPictureProperty);
        set => SetValue(FitsPictureProperty, value);
    }

    /// <summary>The picture's own size in pixels; empty until it has loaded.</summary>
    public PixelSize NaturalSize => _naturalSize;

    /// <summary>The image shown; null until one has loaded.</summary>
    public Bitmap? Bitmap => _picture?.Bitmap;

    /// <summary>Gets the service images in a part of the window load through.</summary>
    /// <param name="control">The control where it was set, or one inside it.</param>
    public static IImageLoader? GetLoader(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(LoaderProperty);
    }

    /// <summary>Sets the service images in a part of the window load through.</summary>
    /// <param name="control">The window, or the part of it whose images load through the service.</param>
    /// <param name="value">The service.</param>
    public static void SetLoader(Control control, IImageLoader? value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(LoaderProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Bitmap is not { } bitmap || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var box = new Rect(Bounds.Size);
        var scale = Stretch.CalculateScaling(Bounds.Size, bitmap.Size);
        var shown = box.CenterRect(new Rect(bitmap.Size * scale)).Intersect(box);
        var source = new Rect(bitmap.Size).CenterRect(new Rect(shown.Size / scale));
        context.DrawImage(bitmap, source, shown);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!FitsPicture)
        {
            return base.MeasureOverride(availableSize);
        }

        if (_hasFailed)
        {
            return default;
        }

        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        if (NaturalSize is { Width: > 0, Height: > 0 } natural)
        {
            width = width > 0 ? Math.Min(width, natural.Width) : natural.Width;
            return new Size(width, width * natural.Height / natural.Width);
        }

        return new Size(width, width * PlaceholderRatio);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty || change.Property == FallbackProperty || change.Property == LoaderProperty
            || change.Property == IsBlurredProperty)
        {
            Reset();
            Load();
        }
        else if (change.Property == BoundsProperty)
        {
            Load();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Cancel();
        _isNearView = false;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var view = e.EffectiveViewport;
        _isNearView = view.Width > 0 && view.Height > 0 && view.Inflate(Lookahead).Intersects(new Rect(Bounds.Size));
        Load();
    }

    private void Reset()
    {
        Cancel();
        Show(null, failed: false);
        _loadedSource = null;
        _loadedSize = default;
        SetState(null);
    }

    private void Cancel()
    {
        _loading?.Cancel();
        _loading = null;
    }

    private void Load()
    {
        if (!_isNearView || _loading is not null || Source is not { Length: > 0 } source || GetValue(LoaderProperty) is not { } loader)
        {
            return;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var size = new PixelSize((int)Math.Ceiling(Bounds.Width * scaling), (int)Math.Ceiling(Bounds.Height * scaling));
        // A box that grew beyond the size the image was decoded for asks for it again; one that shrank keeps it.
        if (size.Width <= 0 || size.Height <= 0
            || (source == _loadedSource && size.Width <= _loadedSize.Width && size.Height <= _loadedSize.Height))
        {
            return;
        }

        _ = LoadAsync(loader, source, size);
    }

    private async Task LoadAsync(IImageLoader loader, string source, PixelSize size)
    {
        using var loading = new CancellationTokenSource();
        _loading = loading;
        if (_picture is null)
        {
            SetState(":loading");
        }

        RemotePicture? picture;
        var task = LoadOneAsync(loader, source, size, loading.Token);
        var arrivedAtOnce = task.IsCompleted;
        try
        {
            picture = await task;
            if (picture is null && Fallback is { Length: > 0 } fallback && fallback != source)
            {
                picture = await LoadOneAsync(loader, fallback, size, loading.Token);
            }
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

        if (loading.IsCancellationRequested)
        {
            return;
        }

        _loadedSource = source;
        _loadedSize = size;
        if (picture is null)
        {
            if (_picture is null)
            {
                SetState(":failed");
                Show(null, failed: true);
            }

            return;
        }

        var first = _picture is null;
        Show(picture, failed: false);
        SetState(":loaded");
        // An image kept in memory shows at once, as when going back to a page; only one that had to arrive fades in.
        if (first && !arrivedAtOnce)
        {
            Motion.FadeIn(this);
        }
    }

    private async Task<RemotePicture?> LoadOneAsync(IImageLoader loader, string address, PixelSize size, CancellationToken cancellationToken)
    {
        if (!IsBlurred)
        {
            return await loader.LoadAsync(address, size, cancellationToken);
        }

        return await loader.LoadBlurredAsync(address, size, cancellationToken) is { } blur ? new RemotePicture(blur, blur.PixelSize) : null;
    }

    private void Show(RemotePicture? picture, bool failed)
    {
        _picture = picture;
        var resized = SetAndRaise(NaturalSizeProperty, ref _naturalSize, picture?.NaturalSize ?? default) | _hasFailed != failed;
        _hasFailed = failed;
        if (resized && FitsPicture)
        {
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    private void SetState(string? state)
    {
        PseudoClasses.Set(":loading", state == ":loading");
        PseudoClasses.Set(":loaded", state == ":loaded");
        PseudoClasses.Set(":failed", state == ":failed");
    }
}
