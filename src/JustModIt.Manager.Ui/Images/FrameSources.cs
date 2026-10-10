using Avalonia;
using JustModIt.Manager.Core.Platform;
using SkiaSharp;

namespace JustModIt.Manager.Ui.Images;

/// <summary>The frames of a moving picture or a silent clip, decoded one after another at the size they are shown at.</summary>
public interface IFrameSource : IDisposable
{
    /// <summary>The frames' size in pixels.</summary>
    PixelSize Size { get; }

    /// <summary>The picture's or the clip's own size.</summary>
    PixelSize NaturalSize { get; }

    /// <summary>Decodes the next frame: the first again after the last, unless the picture plays a set number of times.</summary>
    /// <param name="pixels">Rows of premultiplied BGRA pixels of <see cref="Size"/>, one after another.</param>
    /// <returns>How long the frame shows; null when no further frame comes.</returns>
    TimeSpan? DecodeNext(Span<byte> pixels);
}

/// <summary>The frames of an animated GIF or WebP, composed by Skia's codec at the picture's own size and scaled to the shown one.</summary>
internal sealed class SkiaFrames : IFrameSource
{
    private readonly Stream _file;
    private readonly SKCodec _codec;
    private readonly SKCodecFrameInfo[] _frames;
    private readonly SKBitmap _canvas;
    private readonly SKBitmap _scaled;
    private readonly int _plays;
    private int _next;
    private int _composed = -1;
    private int _played;

    private SkiaFrames(Stream file, SKCodec codec, PixelSize box)
    {
        _file = file;
        _codec = codec;
        _frames = codec.FrameInfo;
        _canvas = new SKBitmap(codec.Info.WithColorType(SKColorType.Bgra8888).WithAlphaType(SKAlphaType.Premul)
            .WithColorSpace(SKColorSpace.CreateSrgb()));
        // GIF and WebP count the repetitions after the first play; a negative count plays forever.
        _plays = codec.RepetitionCount < 0 ? 0 : codec.RepetitionCount + 1;
        NaturalSize = new PixelSize(codec.Info.Width, codec.Info.Height);
        Size = ImageBaking.CoverSize(NaturalSize, box);
        _scaled = ImageBaking.Frame(Size, SKAlphaType.Premul);
    }

    public PixelSize Size { get; }

    public PixelSize NaturalSize { get; }

    /// <summary>Opens a picture's frames to cover a box.</summary>
    /// <param name="path">The picture's file.</param>
    /// <param name="box">The box in physical pixels.</param>
    /// <returns>The frames; null when the picture does not move or Skia does not read it.</returns>
    public static SkiaFrames? Open(string path, PixelSize box)
    {
        var file = File.OpenRead(path);
        var codec = SKCodec.Create(file);
        if (codec is { FrameCount: > 1 })
        {
            return new SkiaFrames(file, codec, box);
        }

        codec?.Dispose();
        file.Dispose();
        return null;
    }

    public TimeSpan? DecodeNext(Span<byte> pixels)
    {
        if (_plays > 0 && _played >= _plays)
        {
            return null;
        }

        var index = _next;
        var frame = _frames[index];
        // A frame drawn over the one before it decodes onto that one; otherwise Skia composes the frames it needs itself.
        var prior = index > 0 && _composed == index - 1 && frame.RequiredFrame >= 0
            && _frames[index - 1].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious ? index - 1 : -1;
        if (prior < 0)
        {
            _canvas.Erase(SKColors.Transparent);
        }

        if (_codec.GetPixels(_canvas.Info, _canvas.GetPixels(), new SKCodecOptions(index, prior))
            is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return null;
        }

        _composed = index;
        _next = (index + 1) % _frames.Length;
        _played += _next == 0 ? 1 : 0;
        using var canvas = _canvas.PeekPixels();
        ImageBaking.Cover(canvas, _scaled, pixels);
        return TimeSpan.FromMilliseconds(frame.Duration);
    }

    public void Dispose()
    {
        _scaled.Dispose();
        _canvas.Dispose();
        _codec.Dispose();
        _file.Dispose();
    }
}

/// <summary>The frames of a silent clip the system decodes, scaled to the shown size.</summary>
internal sealed class ClipFrames : IFrameSource
{
    private readonly IMediaClip _clip;
    private readonly SKBitmap _frame;
    private readonly SKBitmap _scaled;

    /// <summary>Reads a clip's frames to cover a box.</summary>
    /// <param name="clip">The clip, disposed with the frames.</param>
    /// <param name="box">The box in physical pixels.</param>
    public ClipFrames(IMediaClip clip, PixelSize box)
    {
        _clip = clip;
        NaturalSize = new PixelSize(clip.Width, clip.Height);
        Size = ImageBaking.CoverSize(NaturalSize, box);
        _frame = ImageBaking.Frame(NaturalSize, SKAlphaType.Opaque);
        // A later frame smaller than the clip's first covers only part of it.
        _frame.Erase(SKColors.Black);
        _scaled = ImageBaking.Frame(Size, SKAlphaType.Premul);
    }

    public PixelSize Size { get; }

    public PixelSize NaturalSize { get; }

    public TimeSpan? DecodeNext(Span<byte> pixels)
    {
        using var frame = _frame.PeekPixels();
        if (_clip.ReadFrame(frame.GetPixelSpan()) is not { } duration)
        {
            return null;
        }

        ImageBaking.Cover(frame, _scaled, pixels);
        return duration;
    }

    public void Dispose()
    {
        _clip.Dispose();
        _scaled.Dispose();
        _frame.Dispose();
    }
}
