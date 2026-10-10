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

    public static WriteableBitmap Bitmap(int width, int height)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var pixels = bitmap.Lock();
        var row = new byte[pixels.RowBytes];
        for (var x = 0; x < width; x++)
        {
            row[x * 4] = 0x4A;
            row[(x * 4) + 1] = 0x7C;
            row[(x * 4) + 2] = 0xD4;
            row[(x * 4) + 3] = 0xFF;
        }

        for (var y = 0; y < height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(row, 0, pixels.Address + (y * pixels.RowBytes), row.Length);
        }

        return bitmap;
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

    public Task<Bitmap?> LoadAsync(string address, PixelSize size, CancellationToken cancellationToken)
    {
        Requests.Add((address, size));
        return Task.FromResult<Bitmap?>(TestImages.Bitmap(size.Width, size.Height));
    }
}
