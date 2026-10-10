using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace JustModIt.Manager.Ui.Images;

/// <summary>Bakes images with SkiaSharp on the thread they are called on, which is never the UI thread.</summary>
internal static class ImageBaking
{
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
    public static WriteableBitmap Scaled(SKPixmap source, PixelSize size) => Bake(size, canvas =>
    {
        using var image = SKImage.FromPixels(source);
        canvas.DrawImage(image, CoverRect(image.Width, image.Height, new SKRect(0, 0, size.Width, size.Height)), Smooth);
    });

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

    // Fills the box, cropping what does not fit, centred.
    private static SKRect CoverRect(int width, int height, SKRect box)
    {
        var scale = Math.Max(box.Width / width, box.Height / height);
        var (shownWidth, shownHeight) = (width * scale, height * scale);
        var left = box.Left + ((box.Width - shownWidth) / 2);
        var top = box.Top + ((box.Height - shownHeight) / 2);
        return new SKRect(left, top, left + shownWidth, top + shownHeight);
    }
}
