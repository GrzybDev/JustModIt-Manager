using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Controls;

/// <summary>
/// An image of the site, loaded once it comes near the visible part of its scroll area and decoded to the size it is
/// shown at. It fills its box, cropping what does not fit unless <see cref="Stretch"/> says otherwise, and fades in on
/// arrival; the <c>:loading</c>, <c>:loaded</c> and <c>:failed</c> pseudo-classes let a page show a placeholder. A
/// moving picture, or a clip in <see cref="ClipSource"/>, plays while it can be seen and the window allows loops; with
/// reduced motion it shows its first frame, and once it leaves the area near the view its frames are let go.
/// </summary>
public sealed class RemoteImage : Control
{
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<RemoteImage, string?>(nameof(Source));

    /// <summary>An address tried when the source does not load, such as a film's smaller still.</summary>
    public static readonly StyledProperty<string?> FallbackProperty = AvaloniaProperty.Register<RemoteImage, string?>(nameof(Fallback));

    /// <summary>A silent looping clip that plays in place of the picture, such as a store page's GIF turned into a video.</summary>
    public static readonly StyledProperty<string?> ClipSourceProperty = AvaloniaProperty.Register<RemoteImage, string?>(nameof(ClipSource));

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
    private bool _isInView;
    private bool _hasFailed;
    private FramePlayer? _player;
    private bool _awaitsFirstFrame;
    private CancellationTokenSource? _opening;
    private bool _framesRefused;

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

    public string? ClipSource
    {
        get => GetValue(ClipSourceProperty);
        set => SetValue(ClipSourceProperty, value);
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

    /// <summary>The image shown: the frame playing, or the still; null until one has loaded.</summary>
    public Bitmap? Bitmap => _player is { Frames: > 0 } player ? player.Bitmap : _picture?.Bitmap;

    /// <summary>The frames playing; null while the image shows still.</summary>
    internal FramePlayer? Player => _player;

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
        if (change.Property == SourceProperty || change.Property == FallbackProperty || change.Property == ClipSourceProperty
            || change.Property == LoaderProperty || change.Property == IsBlurredProperty)
        {
            Reset();
            Load();
            UpdatePlayback();
        }
        else if (change.Property == BoundsProperty)
        {
            Load();
            UpdatePlayback();
        }
        else if (change.Property == Motion.LoopsProperty)
        {
            UpdatePlayback();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Motion.ReducedChanged += OnReducedChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Motion.ReducedChanged -= OnReducedChanged;
        Cancel();
        StopPlayback();
        _isNearView = false;
        _isInView = false;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var view = e.EffectiveViewport;
        var box = new Rect(Bounds.Size);
        var wasNear = _isNearView;
        _isNearView = view.Width > 0 && view.Height > 0 && view.Inflate(Lookahead).Intersects(box);
        _isInView = view.Width > 0 && view.Height > 0 && view.Intersects(box);
        // Frames refused for want of memory are asked for again when the image comes back near the view.
        _framesRefused &= wasNear || !_isNearView;
        Load();
        UpdatePlayback();
    }

    // With reduced motion a moving picture goes back to its first frame, so its frames start over.
    private void OnReducedChanged(object? sender, EventArgs e)
    {
        StopPlayback();
        UpdatePlayback();
    }

    private void Reset()
    {
        Cancel();
        StopPlayback();
        _framesRefused = false;
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

    private PixelSize BoxSize()
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        return new PixelSize((int)Math.Ceiling(Bounds.Width * scaling), (int)Math.Ceiling(Bounds.Height * scaling));
    }

    private void Load()
    {
        if (!_isNearView || _loading is not null || Source is not { Length: > 0 } source || GetValue(LoaderProperty) is not { } loader)
        {
            return;
        }

        var size = BoxSize();
        // A box that grew beyond the size the image was decoded for asks for it again; one that shrank keeps it.
        if (size.Width <= 0 || size.Height <= 0
            || (source == _loadedSource && size.Width <= _loadedSize.Width && size.Height <= _loadedSize.Height))
        {
            return;
        }

        _ = LoadAsync(loader, source, size);
    }

    // A moving picture plays where it can be seen; near the view it waits paused, and further away its frames go.
    private void UpdatePlayback()
    {
        var clip = ClipSource is { Length: > 0 } given ? given : null;
        var moving = clip ?? (_picture is { IsAnimated: true } ? _loadedSource : null);
        // With reduced motion a picture shows its still; a clip without one shows its first frame.
        if (moving is null || !_isNearView || _framesRefused || (Motion.IsReduced && _picture is not null)
            || GetValue(LoaderProperty) is not { } loader || TopLevel.GetTopLevel(this) is null)
        {
            StopPlayback();
            return;
        }

        if (_player is null)
        {
            if (_opening is null)
            {
                // The box the still was decoded for gives frames of exactly its size.
                _ = OpenPlayerAsync(loader, moving, _picture is null ? BoxSize() : _loadedSize);
            }

            return;
        }

        if (_isInView && GetValue(Motion.LoopsProperty) && !Motion.IsReduced)
        {
            _player.Play();
        }
        else
        {
            _player.Pause();
        }
    }

    private async Task OpenPlayerAsync(IImageLoader loader, string address, PixelSize size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        using var opening = new CancellationTokenSource();
        _opening = opening;
        if (_picture is null)
        {
            SetState(":loading");
        }

        IFrameSource? frames;
        try
        {
            frames = await loader.OpenFramesAsync(address, size, opening.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (_opening == opening)
            {
                _opening = null;
            }
        }

        if (opening.IsCancellationRequested)
        {
            frames?.Dispose();
            return;
        }

        if (frames is null)
        {
            _framesRefused = true;
            FailWhenNothingShows();
            return;
        }

        FramePlayer? player = null;
        // Both calls post to the UI thread, which handles them only after this call, so the player is known by then.
        player = new FramePlayer(frames, () => Dispatcher.UIThread.Post(() => OnFrame(player!)),
            () => Dispatcher.UIThread.Post(() => OnNoFrame(player!)));
        _player = player;
        _awaitsFirstFrame = true;
        UpdatePlayback();
    }

    private void OnFrame(FramePlayer player)
    {
        if (_player != player)
        {
            return;
        }

        // A clip without a still takes its own size and shows once its first frame is there. Frames shown while the UI
        // thread was busy arrive together, so the first one handled need not be the player's first.
        if (_awaitsFirstFrame && _picture is null)
        {
            var resized = SetAndRaise(NaturalSizeProperty, ref _naturalSize, player.NaturalSize) | _hasFailed;
            _hasFailed = false;
            if (resized && FitsPicture)
            {
                InvalidateMeasure();
            }

            SetState(":loaded");
        }

        _awaitsFirstFrame = false;
        InvalidateVisual();
    }

    // Frames that open but never give one, as a clip cut short does, count as refused.
    private void OnNoFrame(FramePlayer player)
    {
        if (_player != player)
        {
            return;
        }

        _framesRefused = true;
        StopPlayback();
        FailWhenNothingShows();
    }

    private void StopPlayback()
    {
        _opening?.Cancel();
        _opening = null;
        if (_player is { } player)
        {
            _player = null;
            player.Dispose();
            InvalidateVisual();
        }
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
            FailWhenNothingShows();
            UpdatePlayback();
            return;
        }

        var first = _picture is null;
        // Frames decoded for a smaller box start over at the size of the new still.
        if (_player is { } player && player.Bitmap.PixelSize != picture.Bitmap.PixelSize)
        {
            StopPlayback();
        }

        Show(picture, failed: false);
        SetState(":loaded");
        // An image kept in memory shows at once, as when going back to a page; only one that had to arrive fades in.
        if (first && !arrivedAtOnce)
        {
            Motion.FadeIn(this);
        }

        UpdatePlayback();
    }

    // A clip whose still does not load keeps its place while its frames may still come; once neither can show, it takes
    // none, as a missing picture does.
    private void FailWhenNothingShows()
    {
        if (_picture is null && _player is null && _loading is null && _opening is null
            && (ClipSource is not { Length: > 0 } || _framesRefused))
        {
            SetState(":failed");
            Show(null, failed: true);
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
