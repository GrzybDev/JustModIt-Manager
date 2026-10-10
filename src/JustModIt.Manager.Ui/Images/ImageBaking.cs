using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace JustModIt.Manager.Ui.Images;

/// <summary>
/// The layers of a page's backdrop, which the window bakes once per project and width into one bitmap rather than
/// blending, blurring and masking them live: the project's artwork, its banner and a glow in the game's colour.
/// </summary>
/// <param name="Key">What the backdrop belongs to, e.g. the project's id; one backdrop is baked per key and size.</param>
/// <param name="Artwork">The large image behind the page's head; empty for none.</param>
/// <param name="Banner">The banner across the top; empty for none.</param>
/// <param name="Tint">The game's colour as <c>#rrggbb</c>; empty to take it from <paramref name="Cover"/>.</param>
/// <param name="Cover">The game's cover, whose average colour stands in for a missing tint; empty for none.</param>
public sealed record BackdropRecipe(string Key, string Artwork, string Banner, string Tint, string Cover)
{
    /// <summary>The backdrop's height in pixels at scale 1: the head of the page and the glow under it.</summary>
    public const double Height = 900;
}

/// <summary>Bakes images with SkiaSharp on the thread they are called on, which is never the UI thread.</summary>
internal static class ImageBaking
{
    private static readonly SKColor Page = new(0x03, 0x07, 0x12);
    private static readonly SKColor DefaultTint = new(58, 88, 120);

    // Mipmaps keep a picture decoded far larger than its box from shimmering once it is scaled down.
    private static readonly SKSamplingOptions Smooth = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>The size a picture is decoded to so that it covers a box, never beyond its own size.</summary>
    /// <param name="natural">The picture's own size.</param>
    /// <param name="box">The box in physical pixels.</param>
    public static PixelSize CoverSize(PixelSize natural, PixelSize box)
    {
        var scale = Math.Min(1, Math.Max((double)box.Width / natural.Width, (double)box.Height / natural.Height));
        return new PixelSize(Math.Max(1, (int)Math.Round(natural.Width * scale)), Math.Max(1, (int)Math.Round(natural.Height * scale)));
    }

    /// <summary>Scales decoded pixels into a bitmap of the given size, cropping what does not fit.</summary>
    /// <param name="source">The decoded pixels.</param>
    /// <param name="size">The bitmap's size.</param>
    public static WriteableBitmap Scaled(SKPixmap source, PixelSize size) => Bake(size, canvas => DrawCover(canvas, source, size));

    /// <summary>A bitmap for one frame: rows of BGRA pixels, one right after another.</summary>
    /// <param name="size">The frame's size.</param>
    /// <param name="alpha">Whether the frame is opaque or premultiplied.</param>
    public static SKBitmap Frame(PixelSize size, SKAlphaType alpha) =>
        new(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, alpha, SKColorSpace.CreateSrgb()));

    /// <summary>Scales decoded pixels over rows of premultiplied BGRA pixels, cropping what does not fit.</summary>
    /// <param name="source">The decoded pixels.</param>
    /// <param name="scaled">A <see cref="Frame"/> of the target's size, which the pixels are scaled into on their way.</param>
    /// <param name="target">The rows, one after another, of a picture of <paramref name="scaled"/>'s size.</param>
    public static void Cover(SKPixmap source, SKBitmap scaled, Span<byte> target)
    {
        ArgumentNullException.ThrowIfNull(scaled);
        using (var canvas = new SKCanvas(scaled))
        {
            canvas.Clear(SKColors.Transparent);
            DrawCover(canvas, source, new PixelSize(scaled.Width, scaled.Height));
        }

        scaled.GetPixelSpan().CopyTo(target);
    }

    private static void DrawCover(SKCanvas canvas, SKPixmap source, PixelSize size)
    {
        using var image = SKImage.FromPixels(source);
        canvas.DrawImage(image, CoverRect(image.Width, image.Height, new SKRect(0, 0, size.Width, size.Height)), Smooth);
    }

    /// <summary>Copies rows of premultiplied BGRA pixels into a bitmap.</summary>
    /// <param name="pixels">The rows, one after another.</param>
    /// <param name="size">Their size.</param>
    public static WriteableBitmap FromPixels(byte[] pixels, PixelSize size)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        var row = size.Width * 4;
        for (var y = 0; y < size.Height; y++)
        {
            Marshal.Copy(pixels, y * row, buffer.Address + (y * buffer.RowBytes), row);
        }

        return bitmap;
    }

    /// <summary>Spreads a tiny image over a box and softens it, so an adult cover shows as a blur of its colours.</summary>
    /// <param name="tiny">The image decoded a few pixels wide.</param>
    /// <param name="size">The box in physical pixels.</param>
    public static Bitmap Blur(Bitmap tiny, PixelSize size) => Bake(size, canvas =>
    {
        using var image = ToSkia(tiny);
        var box = new SKRect(0, 0, size.Width, size.Height);
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(size.Width / 18f, size.Width / 18f, SKShaderTileMode.Clamp) };
        canvas.DrawImage(image, CoverRect(image.Width, image.Height, box), new SKSamplingOptions(SKFilterMode.Linear), paint);
    });

    /// <summary>Bakes a backdrop: the page's colour lit by the game's, under its artwork and banner fading into the page.</summary>
    /// <param name="artwork">The artwork decoded to the backdrop's width; null for none.</param>
    /// <param name="banner">The banner decoded to the backdrop's width; null for none.</param>
    /// <param name="tint">The game's colour; null for the theme's own.</param>
    /// <param name="size">The backdrop in physical pixels.</param>
    /// <param name="scale">Physical pixels per layout pixel.</param>
    public static Bitmap Backdrop(Bitmap? artwork, Bitmap? banner, Color? tint, PixelSize size, double scale) => Bake(size, canvas =>
    {
        var width = (float)size.Width;
        var unit = (float)scale;
        var color = tint is { } given ? new SKColor(given.R, given.G, given.B) : DefaultTint;
        // Everything is drawn into one layer that fades out at its foot, where the page's own colour takes over.
        canvas.SaveLayer();
        canvas.Clear(Mix(Page, color, 0.2f));

        if (artwork is not null)
        {
            using var image = ToSkia(artwork);
            var area = new SKRect(0, 0, width, width * 0.5625f);
            canvas.SaveLayer();
            // A soft blur lets the artwork recede behind the page's head, which sits on top of it.
            using (var soft = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(6 * unit, 6 * unit, SKShaderTileMode.Clamp) })
            {
                canvas.DrawImage(image, CoverRect(image.Width, image.Height, area, alignTop: true), new SKSamplingOptions(SKFilterMode.Linear), soft);
            }

            using (var hue = new SKPaint { Color = color.WithAlpha(140), BlendMode = SKBlendMode.Color })
            {
                canvas.DrawRect(area, hue);
            }

            Fill(canvas, area, Vertical(area, (Page.WithAlpha(158), 0), (Page.WithAlpha(115), 0.55f), (Page.WithAlpha(140), 1)));
            Mask(canvas, area, (1, 0), (1, 0.7f), (0, 1));
            canvas.Restore();
        }

        var glow = new SKRect(0, 220 * unit, width, 900 * unit);
        Glow(canvas, glow, 0.70f, 0.10f, 0.55f, 0.60f, color.WithAlpha(128), 0.72f);
        Glow(canvas, glow, 0.22f, 0.06f, 0.45f, 0.50f, color.WithAlpha(82), 0.70f);

        if (banner is not null)
        {
            using var image = ToSkia(banner);
            var height = width * image.Height / image.Width;
            // A short banner fades out before its own edge, so it never ends in a line.
            var band = new SKRect(0, 0, width, Math.Min(380 * unit, height - (24 * unit)));
            canvas.SaveLayer();
            canvas.DrawImage(image, new SKRect(0, -24 * unit, width, height - (24 * unit)), new SKSamplingOptions(SKFilterMode.Linear));
            Mask(canvas, band, (1, 0), (1, 0.42f), (0.55f, 0.68f), (0.18f, 0.86f), (0, 1));
            canvas.Restore();
        }

        var shade = new SKRect(0, 0, width, 420 * unit);
        canvas.SaveLayer();
        using (var paint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, 0),
                [Page.WithAlpha(209), Page.WithAlpha(168), Page.WithAlpha(71), Page.WithAlpha(0)], [0, 0.30f, 0.56f, 0.74f], SKShaderTileMode.Clamp),
        })
        {
            canvas.DrawRect(shade, paint);
        }

        Mask(canvas, shade, (0, 0), (1, 70 / 420f), (1, 260 / 420f), (0, 1));
        canvas.Restore();

        Mask(canvas, new SKRect(0, 0, width, size.Height), (1, 0), (1, 0.55f), (0, 1));
        canvas.Restore();
    });

    /// <summary>The average colour of an image, deepened to a tint that glows on the dark page.</summary>
    /// <param name="image">The image, decoded small.</param>
    public static Color AverageTint(Bitmap image)
    {
        var (pixels, stride) = Pixels(image);
        long red = 0, green = 0, blue = 0, count = 0;
        for (var y = 0; y < image.PixelSize.Height; y++)
        {
            for (var x = 0; x < image.PixelSize.Width; x++)
            {
                var at = (y * stride) + (x * 4);
                var (r, b) = image.Format == PixelFormats.Rgba8888 ? (pixels[at], pixels[at + 2]) : (pixels[at + 2], pixels[at]);
                red += r;
                green += pixels[at + 1];
                blue += b;
                count++;
            }
        }

        if (count == 0)
        {
            return Color.FromRgb(DefaultTint.Red, DefaultTint.Green, DefaultTint.Blue);
        }

        // An average drifts towards grey and dark; scaling its brightest channel to the theme's tint keeps the hue lit.
        var (ar, ag, ab) = (red / (double)count, green / (double)count, blue / (double)count);
        var factor = 120 / Math.Max(1, Math.Max(ar, Math.Max(ag, ab)));
        return Color.FromRgb((byte)Math.Min(255, ar * factor), (byte)Math.Min(255, ag * factor), (byte)Math.Min(255, ab * factor));
    }

    private static WriteableBitmap Bake(PixelSize size, Action<SKCanvas> draw)
    {
        var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, buffer.Address, buffer.RowBytes)
                ?? throw new InvalidOperationException("A surface could not be made over the bitmap.");
            draw(surface.Canvas);
            surface.Canvas.Flush();
        }

        return bitmap;
    }

    private static SKImage ToSkia(Bitmap bitmap)
    {
        var (pixels, stride) = Pixels(bitmap);
        var type = bitmap.Format == PixelFormats.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
        var alpha = bitmap.AlphaFormat == AlphaFormat.Unpremul ? SKAlphaType.Unpremul : SKAlphaType.Premul;
        var info = new SKImageInfo(bitmap.PixelSize.Width, bitmap.PixelSize.Height, type, alpha);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            return SKImage.FromPixelCopy(info, handle.AddrOfPinnedObject(), stride)
                ?? throw new InvalidOperationException("The image could not be read.");
        }
        finally
        {
            handle.Free();
        }
    }

    private static (byte[] Pixels, int Stride) Pixels(Bitmap bitmap)
    {
        var stride = bitmap.PixelSize.Width * 4;
        var pixels = new byte[stride * bitmap.PixelSize.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        return (pixels, stride);
    }

    // Fills the box, cropping what does not fit: centred, or along the top edge as a page's artwork sits.
    private static SKRect CoverRect(int width, int height, SKRect box, bool alignTop = false)
    {
        var scale = Math.Max(box.Width / width, box.Height / height);
        var (shownWidth, shownHeight) = (width * scale, height * scale);
        var left = box.Left + ((box.Width - shownWidth) / 2);
        var top = alignTop ? box.Top : box.Top + ((box.Height - shownHeight) / 2);
        return new SKRect(left, top, left + shownWidth, top + shownHeight);
    }

    private static SKShader Vertical(SKRect area, params (SKColor Color, float At)[] stops) =>
        SKShader.CreateLinearGradient(new SKPoint(0, area.Top), new SKPoint(0, area.Bottom), [.. stops.Select(stop => stop.Color)],
            [.. stops.Select(stop => stop.At)], SKShaderTileMode.Clamp);

    private static void Fill(SKCanvas canvas, SKRect area, SKShader shader)
    {
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(area, paint);
    }

    // Keeps what the current layer drew in the area by the given opacities down it, and clears everything below it.
    private static void Mask(SKCanvas canvas, SKRect area, params (float Opacity, float At)[] stops)
    {
        using var paint = new SKPaint
        {
            BlendMode = SKBlendMode.DstIn,
            Shader = Vertical(area, [.. stops.Select(stop => (SKColors.Black.WithAlpha((byte)(stop.Opacity * 255)), stop.At))]),
        };
        canvas.DrawRect(new SKRect(area.Left, -area.Height, area.Right, area.Bottom + 100000), paint);
    }

    // An elliptical glow centred at a share of the area, its radii as shares of the area's width and height.
    private static void Glow(SKCanvas canvas, SKRect area, float x, float y, float radiusX, float radiusY, SKColor color, float fade)
    {
        var center = new SKPoint(area.Left + (area.Width * x), area.Top + (area.Height * y));
        var (rx, ry) = (area.Width * radiusX, area.Height * radiusY);
        var stretch = SKMatrix.CreateScale(1, ry / rx, center.X, center.Y);
        using var paint = new SKPaint
        {
            Shader = SKShader.CreateRadialGradient(center, rx, [color, color.WithAlpha(0)], [0, fade], SKShaderTileMode.Clamp, stretch),
        };
        // Painted over the whole canvas: a glow centred near the area's top edge would otherwise end there in a line.
        canvas.DrawPaint(paint);
    }

    private static SKColor Mix(SKColor from, SKColor to, float share) => new(
        (byte)(from.Red + ((to.Red - from.Red) * share)),
        (byte)(from.Green + ((to.Green - from.Green) * share)),
        (byte)(from.Blue + ((to.Blue - from.Blue) * share)));
}
