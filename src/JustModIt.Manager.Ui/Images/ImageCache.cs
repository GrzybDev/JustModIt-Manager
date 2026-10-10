using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Platform;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace JustModIt.Manager.Ui.Images;

/// <summary>Loads the site's images for the window, decoded to the size they are shown at.</summary>
public interface IImageLoader
{
    /// <summary>Loads an image decoded to cover a box of the given size.</summary>
    /// <param name="address">The image's absolute address.</param>
    /// <param name="size">The box it fills, in physical pixels.</param>
    /// <param name="cancellationToken">Stops waiting once the image is no longer needed.</param>
    /// <returns>The image; null when it is not allowed, cannot be downloaded or is not an image.</returns>
    Task<Bitmap?> LoadAsync(string address, PixelSize size, CancellationToken cancellationToken);
}

/// <summary>
/// The window's one image service: it downloads only from the hosts the site loads images from, over https, keeps every
/// image on disk in the app's cache folder (the site never changes an image under its address), decodes on the thread
/// pool straight to the size shown, with the system's decoders for what Skia does not read, and keeps the decoded images
/// most recently used within a memory budget.
/// </summary>
public sealed partial class ImageCache : IImageLoader, IDisposable
{
    /// <summary>The decoded images kept in memory, in bytes of pixels.</summary>
    public const long MemoryBudget = 64L << 20;

    /// <summary>How much the cache folder may hold before the images used longest ago are removed.</summary>
    public const long DiskBudget = 256L << 20;

    /// <summary>The largest image downloaded; anything larger is not a cover or a screenshot.</summary>
    public const long MaxImageBytes = 16L << 20;

    // Decoding to sizes rounded up to this step lets a box that changes by a few pixels reuse the image decoded before.
    private const int SizeStep = 64;

    // A format that cannot decode to a smaller size, such as PNG, decodes whole first; a small file can hold a huge picture.
    private const long MaxDecodedPixels = 64L << 20;
    private const string Extension = ".img";
    private const string PartialExtension = ".part";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly string _directory;
    private readonly (string Host, string Path)[] _sources;
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly IMediaCodecs? _codecs;
    private readonly long _memoryBudget;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Task<string?>> _downloads = [];
    private readonly Dictionary<string, LinkedListNode<Decoded>> _decoded = [];
    private readonly LinkedList<Decoded> _recent = [];
    private long _memoryBytes;

    /// <summary>Creates the service over the app's image cache folder and removes what is over its budget there.</summary>
    /// <param name="directory">The cache folder; created when the first image is downloaded.</param>
    /// <param name="sources">The only hosts images are loaded from, each alone or with the path its images are under, e.g. <c>images.example.com/vi/</c>.</param>
    /// <param name="http">The client the images are downloaded with; disposed with the service.</param>
    /// <param name="logger">Where failed downloads are logged.</param>
    /// <param name="codecs">The system's decoders for the pictures Skia does not read; null for none.</param>
    /// <param name="memoryBudget">The decoded images kept in memory, in bytes; null for <see cref="MemoryBudget"/>.</param>
    public ImageCache(string directory, IEnumerable<string> sources, HttpClient http, ILogger logger, IMediaCodecs? codecs = null,
        long? memoryBudget = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        _directory = directory;
        _sources = [.. sources.Select(source => source.IndexOf('/', StringComparison.Ordinal) is var slash and >= 0
            ? (source[..slash], source[slash..]) : (source, "/"))];
        _http = http;
        _logger = logger;
        _codecs = codecs;
        _memoryBudget = memoryBudget ?? MemoryBudget;
        _ = Task.Run(Prune);
    }

    /// <summary>Creates the service for the window, downloading with the client the agent's storage downloads use.</summary>
    /// <param name="context">The site, the image hosts, the system's decoders and the cache folder.</param>
    public static ImageCache For(UiContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ImageCache(context.ImageCacheDirectory, context.ImageHosts, CatalogueHttp.CreateStorageClient(context.Site),
            context.Loggers.CreateLogger<ImageCache>(), context.Codecs);
    }

    /// <summary>Tells whether an address is one the window loads images from.</summary>
    /// <param name="address">The image's address.</param>
    public bool IsAllowed(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return address.IsAbsoluteUri && address.Scheme == Uri.UriSchemeHttps && address.IsDefaultPort && address.UserInfo.Length == 0
            && _sources.Any(source => string.Equals(source.Host, address.IdnHost, StringComparison.OrdinalIgnoreCase)
                && address.AbsolutePath.StartsWith(source.Path, StringComparison.Ordinal));
    }

    public async Task<Bitmap?> LoadAsync(string address, PixelSize size, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !IsAllowed(uri) || size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        var decodeSize = new PixelSize(RoundUp(size.Width), RoundUp(size.Height));
        var key = FormattableString.Invariant($"{decodeSize.Width}x{decodeSize.Height} {uri.AbsoluteUri}");
        if (Recall(key) is { } kept)
        {
            return kept;
        }

        var file = await FileAsync(uri).WaitAsync(cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        var bitmap = await Task.Run(() => Decode(uri, file, decodeSize), cancellationToken).ConfigureAwait(false);
        if (bitmap is not null)
        {
            Remember(key, bitmap);
        }

        return bitmap;
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _http.Dispose();
        _stopping.Dispose();
    }

    private static int RoundUp(int pixels) => (pixels + SizeStep - 1) / SizeStep * SizeStep;

    private string PathOf(Uri address) =>
        Path.Combine(_directory, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address.AbsoluteUri))) + Extension);

    // One download per image however many boxes ask for it; a caller that gives up only stops waiting for it.
    private Task<string?> FileAsync(Uri address)
    {
        var path = PathOf(address);
        lock (_gate)
        {
            if (_downloads.TryGetValue(path, out var running))
            {
                return running;
            }

            var download = Task.Run(() => FetchAsync(address, path));
            _downloads[path] = download;
            _ = download.ContinueWith(_ =>
            {
                lock (_gate)
                {
                    _downloads.Remove(path);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return download;
        }
    }

    private async Task<string?> FetchAsync(Uri address, string path)
    {
        if (File.Exists(path))
        {
            Touch(path);
            return path;
        }

        var partial = path + "." + Guid.NewGuid().ToString("N") + PartialExtension;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LogRefused(_logger, address, (int)response.StatusCode);
                return null;
            }

            if (response.Content.Headers.ContentLength > MaxImageBytes)
            {
                LogTooLarge(_logger, address);
                return null;
            }

            Directory.CreateDirectory(_directory);
            var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var target = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await using (target.ConfigureAwait(false))
                {
                    if (!await CopyAsync(source, target, timeout.Token).ConfigureAwait(false))
                    {
                        LogTooLarge(_logger, address);
                        return null;
                    }
                }
            }

            File.Move(partial, path, overwrite: true);
            return path;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException
            or OperationCanceledException or ObjectDisposedException)
        {
            LogFailed(_logger, address, exception);
            return null;
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private static async Task<bool> CopyAsync(Stream source, Stream target, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            copied += read;
            if (copied > MaxImageBytes)
            {
                return false;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    // Decoded to cover the box, never beyond the picture's own size; Skia reads what it can, the system's decoders the rest.
    private WriteableBitmap? Decode(Uri address, string path, PixelSize box)
    {
        Exception? failure = null;
        try
        {
            if ((DecodeWithSkia(path, box) ?? DecodeWithSystem(path, box)) is { } bitmap)
            {
                return bitmap;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailed(_logger, address, exception);
            return null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            failure = exception;
        }

        // Not an image the window can show; it is asked for again next time, in case the site replaced it.
        LogUndecodable(_logger, address, failure);
        TryDelete(path);
        return null;
    }

    private static WriteableBitmap? DecodeWithSkia(string path, PixelSize box)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
        {
            return null;
        }

        var natural = new PixelSize(codec.Info.Width, codec.Info.Height);
        var size = ImageBaking.CoverSize(natural, box);
        // JPEG and WebP decode straight to a smaller size, which leaves little to scale.
        var info = codec.Info.WithSize(codec.GetScaledDimensions((float)size.Width / natural.Width))
            .WithColorType(SKColorType.Bgra8888).WithAlphaType(SKAlphaType.Premul).WithColorSpace(SKColorSpace.CreateSrgb());
        if ((long)info.Width * info.Height > MaxDecodedPixels)
        {
            return null;
        }

        using var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return null;
        }

        using var pixels = decoded.PeekPixels();
        return ImageBaking.Scaled(pixels, size);
    }

    private WriteableBitmap? DecodeWithSystem(string path, PixelSize box)
    {
        (int Width, int Height) SizeFor(int width, int height)
        {
            var size = ImageBaking.CoverSize(new PixelSize(width, height), box);
            return (size.Width, size.Height);
        }

        return _codecs?.DecodePicture(path, SizeFor) is { } picture
            ? ImageBaking.FromPixels(picture.Pixels, new PixelSize(picture.Width, picture.Height))
            : null;
    }

    private Bitmap? Recall(string key)
    {
        lock (_gate)
        {
            if (!_decoded.TryGetValue(key, out var node))
            {
                return null;
            }

            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Bitmap;
        }
    }

    // Images pushed out are only forgotten, never disposed, since a box may still show them.
    private void Remember(string key, Bitmap bitmap)
    {
        var bytes = 4L * bitmap.PixelSize.Width * bitmap.PixelSize.Height;
        lock (_gate)
        {
            if (_decoded.ContainsKey(key))
            {
                return;
            }

            _decoded[key] = _recent.AddFirst(new Decoded(key, bitmap, bytes));
            _memoryBytes += bytes;
            while (_memoryBytes > _memoryBudget && _recent.Last is { } oldest && oldest != _recent.First)
            {
                _recent.RemoveLast();
                _decoded.Remove(oldest.Value.Key);
                _memoryBytes -= oldest.Value.Bytes;
            }
        }
    }

    private void Prune()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            var folder = new DirectoryInfo(_directory);
            foreach (var partial in folder.EnumerateFiles("*" + PartialExtension).Where(file => file.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1)))
            {
                TryDelete(partial.FullName);
            }

            var images = folder.EnumerateFiles("*" + Extension).OrderByDescending(file => file.LastWriteTimeUtc).ToList();
            long kept = 0;
            foreach (var image in images)
            {
                kept += image.Length;
                if (kept > DiskBudget)
                {
                    TryDelete(image.FullName);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogPruneFailed(_logger, exception);
        }
    }

    // The folder's file times say which images were used last, since the system rarely keeps last access times.
    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The image {Address} was refused with {StatusCode}")]
    private static partial void LogRefused(ILogger logger, Uri address, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The image {Address} is larger than the window takes")]
    private static partial void LogTooLarge(ILogger logger, Uri address);

    [LoggerMessage(Level = LogLevel.Information, Message = "The image {Address} could not be loaded")]
    private static partial void LogFailed(ILogger logger, Uri address, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The image {Address} could not be decoded")]
    private static partial void LogUndecodable(ILogger logger, Uri address, Exception? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The image cache could not be pruned")]
    private static partial void LogPruneFailed(ILogger logger, Exception exception);

    private sealed record Decoded(string Key, Bitmap Bitmap, long Bytes);
}
