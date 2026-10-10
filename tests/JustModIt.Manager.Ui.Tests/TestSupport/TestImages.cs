using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using JustModIt.Manager.Ui.Images;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>Plain images made by the test, as PNG files or as bitmaps.</summary>
public static class TestImages
{
    public static byte[] Png(int width, int height)
    {
        using var bitmap = Bitmap(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    public static WriteableBitmap Bitmap(int width, int height) => Bitmap(width, height, 0xD4, 0x7C, 0x4A);

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

    public Task<Bitmap?> LoadAsync(string address, PixelSize size, CancellationToken cancellationToken)
    {
        Requests.Add((address, size));
        return Task.FromResult<Bitmap?>(TestImages.Cover(address, size.Width, size.Height));
    }

    public Task<Bitmap?> LoadBlurredAsync(string address, PixelSize size, CancellationToken cancellationToken)
    {
        Blurred.Add(address);
        return Task.FromResult<Bitmap?>(ImageBaking.Blur(TestImages.Cover(address, 8, 12), size));
    }
}
