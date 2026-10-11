using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Platform.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>
/// Tells which of the system's optional decoders are registered, without decoding anything: AVIF and VP9 come with
/// extensions that Windows Server and some editions of Windows lack.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class InstalledDecoders
{
    // Only software decoders count, since a source reader without a GPU device loads no other.
    private const MFT_ENUM_FLAG SoftwareDecoders =
        MFT_ENUM_FLAG.MFT_ENUM_FLAG_SYNCMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_LOCALMFT | MFT_ENUM_FLAG.MFT_ENUM_FLAG_SORTANDFILTER;

    /// <summary>Tells whether Media Foundation has a software decoder for a video format.</summary>
    /// <param name="format">The format's subtype, such as <see cref="PInvoke.MFVideoFormat_AV1"/>.</param>
    public static bool HasVideoDecoder(Guid format)
    {
        try
        {
            return MultithreadedApartment.Keep().Succeeded && CountVideoDecoders(format) > 0;
        }
        // Windows without its media features has no Media Foundation at all.
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Tells whether the Windows Imaging Component has a decoder for a type of picture.</summary>
    /// <param name="mimeType">The picture's MIME type, such as image/avif.</param>
    public static bool HasPictureDecoder(string mimeType)
    {
        ArgumentException.ThrowIfNullOrEmpty(mimeType);
        if (MultithreadedApartment.Keep().Failed
            || PInvoke.CoCreateInstance(PInvoke.CLSID_WICImagingFactory, null, CLSCTX.CLSCTX_INPROC_SERVER,
                typeof(IWICImagingFactory).GUID, out var instance).Failed)
        {
            return false;
        }

        var factory = ComObjects.Own<IWICImagingFactory>(instance);
        try
        {
            if (factory.CreateComponentEnumerator((uint)WICComponentType.WICDecoder,
                    (uint)WICComponentEnumerateOptions.WICComponentEnumerateDefault, out var decoders).Failed)
            {
                return false;
            }

            var decoder = new object[1];
            while (decoders.Next(decoder, out var fetched).Succeeded && fetched == 1)
            {
                if (decoder[0] is IWICBitmapCodecInfo codec && Takes(codec, mimeType))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            ComObjects.Release(factory);
        }
    }

    // A codec names its types in one string, separated by commas; asking it whether it matches one is not implemented.
    private static bool Takes(IWICBitmapCodecInfo codec, string mimeType)
    {
        Span<char> types = default;
        if (codec.GetMimeTypes(0, ref types, out var length).Failed || length == 0)
        {
            return false;
        }

        types = new char[length + 1];
        return codec.GetMimeTypes((uint)types.Length, ref types, out _).Succeeded
            && new string(types).Split(',', StringSplitOptions.TrimEntries).Contains(mimeType, StringComparer.OrdinalIgnoreCase);
    }

    // Unsafe because the decoders come as a native array of activation objects, which the caller releases and frees.
    private static unsafe uint CountVideoDecoders(Guid format)
    {
        var input = new MFT_REGISTER_TYPE_INFO { guidMajorType = PInvoke.MFMediaType_Video, guidSubtype = format };
        if (PInvoke.MFTEnumEx(PInvoke.MFT_CATEGORY_VIDEO_DECODER, SoftwareDecoders, input, null, out var decoders, out var count).Failed)
        {
            return 0;
        }

        for (var index = 0; index < count; index++)
        {
            Marshal.Release((nint)decoders[index]);
        }

        Marshal.FreeCoTaskMem((nint)decoders);
        return count;
    }
}
