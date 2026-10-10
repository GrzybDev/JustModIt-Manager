using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using JustModIt.Manager.Ui.Images;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>Plain images made by the test, as PNG files or as bitmaps.</summary>
public static class TestImages
{
    public static byte[] Png(int width, int height, string? address = null)
    {
        using var bitmap = address is null ? Bitmap(width, height) : Cover(address, width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    public static WriteableBitmap Bitmap(int width, int height) => Bitmap(width, height, 0xD4, 0x7C, 0x4A);

    /// <summary>The colour a window shows at a point, named by its strongest channel, or "none" where it is dark.</summary>
    public static string ColourAt(TopLevel window, PixelPoint point)
    {
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was rendered.");
        var pixel = new byte[4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixel, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(point, new PixelSize(1, 1)), handle.AddrOfPinnedObject(), 4, 4);
        }
        finally
        {
            handle.Free();
        }

        var (red, green, blue) = frame.Format == PixelFormats.Rgba8888 ? (pixel[0], pixel[1], pixel[2]) : (pixel[2], pixel[1], pixel[0]);
        var strongest = Math.Max(red, Math.Max(green, blue));
        return strongest < 0x60 ? "none" : strongest == red ? "red" : strongest == green ? "green" : "blue";
    }

    /// <summary>A picture that stands in for an address's cover in renders: a diagonal glow in a colour the address picks.</summary>
    public static WriteableBitmap Cover(string address, int width, int height)
    {
        var hue = (uint)address.Aggregate(17, (hash, character) => (hash * 31) + character) % 360;
        var (r, g, b) = FromHue(hue);
        return Bitmap(width, height, r, g, b, shaded: true);
    }

    private static WriteableBitmap Bitmap(int width, int height, byte red, byte green, byte blue, bool shaded = false)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var pixels = bitmap.Lock();
        var row = new byte[pixels.RowBytes];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var light = shaded ? 0.25 + (0.75 * (1 - (((double)x / width) + ((double)y / height)) / 2)) : 1;
                row[x * 4] = (byte)(blue * light);
                row[(x * 4) + 1] = (byte)(green * light);
                row[(x * 4) + 2] = (byte)(red * light);
                row[(x * 4) + 3] = 0xFF;
            }

            System.Runtime.InteropServices.Marshal.Copy(row, 0, pixels.Address + (y * pixels.RowBytes), row.Length);
        }

        return bitmap;
    }

    private static (byte R, byte G, byte B) FromHue(uint hue)
    {
        var x = 1 - Math.Abs((hue / 60.0 % 2) - 1);
        (double R, double G, double B) channels = (hue / 60) switch
        {
            0 => (1, x, 0),
            1 => (x, 1, 0),
            2 => (0, 1, x),
            3 => (0, x, 1),
            4 => (x, 0, 1),
            _ => (1, 0, x),
        };
        return ((byte)(70 + (channels.R * 140)), (byte)(70 + (channels.G * 140)), (byte)(70 + (channels.B * 140)));
    }
}

/// <summary>The site's image host as the test sets it up: the images it has, and every request it got.</summary>
public sealed class FakeImageServer : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<Uri> _requests = [];

    public Dictionary<string, byte[]> Images { get; } = [];

    /// <summary>Answers wait for this task, so the test can ask twice while a download runs.</summary>
    public Task Hold { get; set; } = Task.CompletedTask;

    public IReadOnlyList<Uri> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _requests.Add(request.RequestUri!);
        }

        await Hold.WaitAsync(cancellationToken);
        return Images.TryGetValue(request.RequestUri!.AbsoluteUri, out var image)
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(image) }
            : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
    }
}

/// <summary>An image service that hands out a plain bitmap of the size asked for at once, recording what was asked.</summary>
public sealed class FakeImages : IImageLoader
{
    public List<(string Address, PixelSize Size)> Requests { get; } = [];

    public List<string> Blurred { get; } = [];

    public List<(BackdropRecipe Recipe, PixelSize Size)> Backdrops { get; } = [];

    /// <summary>The pictures' own sizes by address; any other picture is as large as the box it is asked for.</summary>
    public Dictionary<string, PixelSize> Sizes { get; } = [];

    /// <summary>Addresses that do not load, as a film without its largest still.</summary>
    public HashSet<string> Missing { get; } = [];

    /// <summary>
    /// Moving pictures and clips by address, as the colours of their frames, none for a clip that opens without a frame;
    /// anything else stands still.
    /// </summary>
    public Dictionary<string, Color[]> Animations { get; } = [];

    /// <summary>The frames handed out, in order, with the address each plays.</summary>
    public List<(string Address, FakeFrames Frames)> Opened { get; } = [];

    public Task<RemotePicture?> LoadAsync(string address, PixelSize size, CancellationToken cancellationToken)
    {
        Requests.Add((address, size));
        if (Missing.Contains(address))
        {
            return Task.FromResult<RemotePicture?>(null);
        }

        var natural = Sizes.TryGetValue(address, out var own) ? own : size;
        var decoded = ImageBaking.CoverSize(natural, size);
        return Task.FromResult<RemotePicture?>(new RemotePicture(TestImages.Cover(address, decoded.Width, decoded.Height), natural,
            Animations.ContainsKey(address)));
    }

    public Task<IFrameSource?> OpenFramesAsync(string address, PixelSize size, CancellationToken cancellationToken)
    {
        if (!Animations.TryGetValue(address, out var colours))
        {
            return Task.FromResult<IFrameSource?>(null);
        }

        var natural = Sizes.TryGetValue(address, out var own) ? own : size;
        var frames = new FakeFrames(colours, natural, ImageBaking.CoverSize(natural, size));
        Opened.Add((address, frames));
        return Task.FromResult<IFrameSource?>(frames);
    }

    public Task<Bitmap?> LoadBlurredAsync(string address, PixelSize size, CancellationToken cancellationToken)
    {
        Blurred.Add(address);
        return Task.FromResult<Bitmap?>(ImageBaking.Blur(TestImages.Cover(address, 8, 12), size));
    }

    public Task<Bitmap?> LoadBackdropAsync(BackdropRecipe recipe, PixelSize size, double scale, CancellationToken cancellationToken)
    {
        Backdrops.Add((recipe, size));
        return Task.FromResult<Bitmap?>(ImageBaking.Backdrop(null, null, null, size, scale));
    }
}

/// <summary>Frames of solid colours, one after another, each for 40 ms; without colours, no frame comes.</summary>
public sealed class FakeFrames(Color[] colours, PixelSize natural, PixelSize size) : IFrameSource
{
    private int _next;
    private volatile bool _disposed;

    public PixelSize Size => size;

    public PixelSize NaturalSize => natural;

    public bool IsDisposed => _disposed;

    public TimeSpan? DecodeNext(Span<byte> pixels)
    {
        if (colours.Length == 0)
        {
            return null;
        }

        var colour = colours[_next++ % colours.Length];
        for (var at = 0; at < pixels.Length; at += 4)
        {
            (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = (colour.B, colour.G, colour.R, 0xFF);
        }

        return TimeSpan.FromMilliseconds(40);
    }

    public void Dispose() => _disposed = true;
}
