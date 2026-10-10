using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>
/// Decodes pictures with the Windows Imaging Component, which reads AVIF through the system's HEIF and AV1 extensions
/// besides the formats Windows ships with.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMediaCodecs : IMediaCodecs
{
    // A decoded picture larger than this is not a site's still, and would take hundreds of megabytes.
    private const long MaxPixels = 64L << 20;

    public MediaPicture? DecodePicture(string path, Func<int, int, (int Width, int Height)> sizeFor) => Decode(path, sizeFor, out _);

    /// <summary>Decodes the first frame of a picture, telling a missing decoder from a damaged file.</summary>
    /// <param name="path">The picture's file.</param>
    /// <param name="sizeFor">Gives the size to decode to from the picture's own width and height.</param>
    /// <param name="status">
    /// The result of the call that failed, <see cref="HRESULT.WINCODEC_ERR_COMPONENTNOTFOUND"/> when no decoder reads the file.
    /// </param>
    /// <returns>The picture; null when it could not be decoded.</returns>
    internal static MediaPicture? Decode(string path, Func<int, int, (int Width, int Height)> sizeFor, out HRESULT status)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(sizeFor);
        // The decoder reads a copy, since the collector releases it at a time of its own and a file it read would stay open
        // until then, which keeps the cache from removing or replacing it.
        var content = File.ReadAllBytes(path);
        if ((status = MultithreadedApartment.Keep()).Failed
            || (status = PInvoke.CoCreateInstance(PInvoke.CLSID_WICImagingFactory, null, CLSCTX.CLSCTX_INPROC_SERVER,
                typeof(IWICImagingFactory).GUID, out var factory)).Failed)
        {
            return null;
        }

        var owned = ComObjects.Own<IWICImagingFactory>(factory);
        try
        {
            return Decode(owned, content, sizeFor, out status);
        }
        finally
        {
            ComObjects.Release(owned);
        }
    }

    // The objects the factory makes are let go by the collector, as .NET's COM wrappers let go of every object they make
    // themselves; none of them holds more than the copy of the file and the frame, and the pixels are copied out.
    private static MediaPicture? Decode(IWICImagingFactory factory, ReadOnlySpan<byte> content,
        Func<int, int, (int Width, int Height)> sizeFor, out HRESULT status)
    {
        if (PInvoke.SHCreateMemStream(content) is not { } stream)
        {
            status = HRESULT.E_OUTOFMEMORY;
            return null;
        }

        // No decoder's vendor is GUID_NULL, so naming it as the preferred vendor prefers none.
        if ((status = factory.CreateDecoderFromStream(stream, Guid.Empty, WICDecodeOptions.WICDecodeMetadataCacheOnDemand,
                out var decoder)).Failed
            || (status = decoder.GetFrame(0, out var frame)).Failed
            || (status = frame.GetSize(out var naturalWidth, out var naturalHeight)).Failed
            || (status = factory.CreateFormatConverter(out var converter)).Failed
            || (status = converter.Initialize(frame, PInvoke.GUID_WICPixelFormat32bppPBGRA, WICBitmapDitherType.WICBitmapDitherTypeNone,
                null, 0, WICBitmapPaletteType.WICBitmapPaletteTypeCustom)).Failed)
        {
            return null;
        }

        var (width, height) = sizeFor((int)naturalWidth, (int)naturalHeight);
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
        {
            status = HRESULT.E_INVALIDARG;
            return null;
        }

        IWICBitmapSource source = converter;
        if (width != naturalWidth || height != naturalHeight)
        {
            if ((status = factory.CreateBitmapScaler(out var scaler)).Failed
                || (status = scaler.Initialize(converter, (uint)width, (uint)height,
                    WICBitmapInterpolationMode.WICBitmapInterpolationModeHighQualityCubic)).Failed)
            {
                return null;
            }

            source = scaler;
        }

        var pixels = new byte[width * height * 4];
        status = source.CopyPixels(null, (uint)width * 4, pixels);
        return status.Failed ? null : new MediaPicture(width, height, (int)naturalWidth, (int)naturalHeight, pixels);
    }
}
