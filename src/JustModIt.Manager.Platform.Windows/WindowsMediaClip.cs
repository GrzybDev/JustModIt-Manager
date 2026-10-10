using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Windows.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.MediaFoundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>
/// Reads a silent clip's frames as 32-bit RGB through Media Foundation's source reader, which decodes H.264 with what
/// Windows ships and VP9 with the system's extension.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMediaClip : IMediaClip
{
    private const uint FirstVideoStream = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_FIRST_VIDEO_STREAM);
    private const uint AllStreams = unchecked((uint)MF_SOURCE_READER_CONSTANTS.MF_SOURCE_READER_ALL_STREAMS);

    // The reader may hand out stream ticks and format changes before a frame; a clip that never gives one is broken.
    private const int MaxReads = 64;

    private static readonly TimeSpan DefaultFrame = TimeSpan.FromMilliseconds(40);

    // Never shut down, since the collector may release objects of the platform at any time later.
    private static readonly Lazy<HRESULT> Startup = new(StartPlatform);

    private readonly Lock _gate = new();
    private readonly IMFByteStream _file;
    private readonly IMFSourceReader _reader;
    private IMFMediaBuffer? _frame;
    private IMF2DBuffer? _frameRows;
    private byte[] _contiguous = [];
    private int _left;
    private int _top;
    private int _shownWidth;
    private int _shownHeight;
    private int _stride;
    private int _frameHeight;
    private long _previousTime = -1;
    private bool _disposed;

    private WindowsMediaClip(IMFByteStream file, IMFSourceReader reader)
    {
        _file = file;
        _reader = reader;
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Opens a clip, telling a system without the decoders it needs from a damaged file.</summary>
    /// <param name="path">The clip's file, MP4 or WebM.</param>
    /// <param name="status">The result of the call that failed; see <see cref="LacksSupport"/>.</param>
    /// <returns>The clip; null when it cannot be read.</returns>
    internal static WindowsMediaClip? Open(string path, out HRESULT status)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (Sniff(path) is not { } contentType)
        {
            status = HRESULT.MF_E_UNSUPPORTED_BYTESTREAM_TYPE;
            return null;
        }

        if ((status = MultithreadedApartment.Keep()).Failed
            || (status = Startup.Value).Failed
            || (status = PInvoke.MFCreateFile(MF_FILE_ACCESSMODE.MF_ACCESSMODE_READ, MF_FILE_OPENMODE.MF_OPENMODE_FAIL_IF_NOT_EXIST,
                MF_FILE_FLAGS.MF_FILEFLAGS_NONE, path, out var file)).Failed)
        {
            return null;
        }

        if ((status = CreateReader(file, contentType, out var reader)).Failed)
        {
            _ = file.Close();
            return null;
        }

        var clip = new WindowsMediaClip(file, reader!);
        if ((status = clip.Start()).Failed)
        {
            clip.Dispose();
            return null;
        }

        return clip;
    }

    /// <summary>Tells whether an open failed for want of what the system lacks rather than a fault of the file.</summary>
    /// <param name="status">The status <see cref="Open"/> gave.</param>
    internal static bool LacksSupport(HRESULT status) =>
        status == HRESULT.MF_E_PLATFORM_NOT_INITIALIZED || status == HRESULT.MF_E_TOPO_CODEC_NOT_FOUND
        || status == HRESULT.MF_E_INVALIDMEDIATYPE || status == HRESULT.MF_E_UNSUPPORTED_BYTESTREAM_TYPE;

    public TimeSpan? ReadFrame(Span<byte> pixels)
    {
        lock (_gate)
        {
            if (_disposed || pixels.Length < Width * Height * 4)
            {
                return null;
            }

            var rewound = false;
            for (var attempt = 0; attempt < MaxReads; attempt++)
            {
                if (ReadSample(out var flags, out var time, out var sample).Failed || (flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ERROR) != 0)
                {
                    ComObjects.Release(sample);
                    return null;
                }

                try
                {
                    if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0 && ReadFormat().Failed)
                    {
                        return null;
                    }

                    if (sample is not null)
                    {
                        return Copy(sample, time, pixels);
                    }

                    if ((flags & MF_SOURCE_READER_FLAG.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                    {
                        // A clip that ends without a frame after starting over has none to show.
                        if (rewound || Rewind().Failed)
                        {
                            return null;
                        }

                        rewound = true;
                    }
                }
                finally
                {
                    ComObjects.Release(sample);
                }
            }

            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // The decoders go with the reader and the file closes at once; the stream's and the frame buffer's wrappers wait
            // for the collector.
            _disposed = true;
            ComObjects.Release(_reader);
            _ = _file.Close();
        }
    }

    private static HRESULT StartPlatform()
    {
        try
        {
            return PInvoke.MFStartup(PInvoke.MF_VERSION, PInvoke.MFSTARTUP_LITE);
        }
        // Windows without its media features has no Media Foundation at all.
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return HRESULT.MF_E_PLATFORM_NOT_INITIALIZED;
        }
    }

    // Media Foundation picks a container's reader by its type, and the window's cache names files after no format.
    private static string? Sniff(string path)
    {
        Span<byte> head = stackalloc byte[12];
        using (var file = File.OpenRead(path))
        {
            if (file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length)
            {
                return null;
            }
        }

        return head[4..8].SequenceEqual("ftyp"u8) ? "video/mp4"
            : head[..4].SequenceEqual((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]) ? "video/webm"
            : null;
    }

    // Made by the class factory, which hands the reader over by address, so it can be released with its decoders at once.
    private static HRESULT CreateReader(IMFByteStream file, string contentType, out IMFSourceReader? reader)
    {
        reader = null;
        HRESULT status;
        if (file is not IMFAttributes fileAttributes)
        {
            return HRESULT.MF_E_UNSUPPORTED_BYTESTREAM_TYPE;
        }

        if ((status = fileAttributes.SetString(PInvoke.MF_BYTESTREAM_CONTENT_TYPE, contentType)).Failed
            || (status = PInvoke.MFCreateAttributes(out var attributes, 1)).Failed
            || (status = attributes.SetUINT32(PInvoke.MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING, 1)).Failed
            || (status = PInvoke.CoCreateInstance(PInvoke.CLSID_MFReadWriteClassFactory, null, CLSCTX.CLSCTX_INPROC_SERVER,
                typeof(IMFReadWriteClassFactory).GUID, out var factoryInstance)).Failed)
        {
            return status;
        }

        var factory = ComObjects.Own<IMFReadWriteClassFactory>(factoryInstance);
        try
        {
            if ((status = factory.CreateInstanceFromObject(PInvoke.CLSID_MFSourceReader, file, attributes, typeof(IMFSourceReader).GUID,
                out var readerInstance)).Succeeded)
            {
                reader = ComObjects.Own<IMFSourceReader>(readerInstance);
            }

            return status;
        }
        finally
        {
            ComObjects.Release(factory);
        }
    }

    private HRESULT Start()
    {
        HRESULT status;
        if ((status = _reader.SetStreamSelection(AllStreams, false)).Failed
            || (status = _reader.SetStreamSelection(FirstVideoStream, true)).Failed
            || (status = PInvoke.MFCreateMediaType(out var type)).Failed
            || (status = type.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video)).Failed
            || (status = type.SetGUID(PInvoke.MF_MT_SUBTYPE, PInvoke.MFVideoFormat_RGB32)).Failed
            || (status = _reader.SetCurrentMediaType(FirstVideoStream, type)).Failed)
        {
            return status;
        }

        return ReadFormat();
    }

    // Decoders work on frames padded to whole blocks; the aperture is the part of the frame the clip shows. The frames are
    // copied into a buffer of this format, which hands its rows over without an address.
    private HRESULT ReadFormat()
    {
        HRESULT status;
        if ((status = _reader.GetCurrentMediaType(FirstVideoStream, out var type)).Failed
            || (status = type.GetUINT64(PInvoke.MF_MT_FRAME_SIZE, out var size)).Failed
            || (status = PInvoke.MFCreateMediaBufferFromMediaType(type, 0, 0, 0, out var frame)).Failed)
        {
            return status;
        }

        if (frame is not IMF2DBuffer rows)
        {
            return HRESULT.MF_E_INVALIDMEDIATYPE;
        }

        if ((status = rows.GetContiguousLength(out var length)).Failed)
        {
            return status;
        }

        var (frameWidth, frameHeight) = ((int)(size >> 32), (int)(size & uint.MaxValue));
        _stride = type.GetUINT32(PInvoke.MF_MT_DEFAULT_STRIDE, out var stride).Succeeded ? (int)stride : frameWidth * 4;
        _frameHeight = frameHeight;
        var (left, top, width, height) = Aperture(type, PInvoke.MF_MT_MINIMUM_DISPLAY_APERTURE)
            ?? Aperture(type, PInvoke.MF_MT_GEOMETRIC_APERTURE) ?? (0, 0, frameWidth, frameHeight);
        if (width <= 0 || height <= 0 || left + width > frameWidth || top + height > frameHeight)
        {
            (left, top, width, height) = (0, 0, frameWidth, frameHeight);
        }

        // A clip keeps the size it opened with; a later frame of another size shows as much of it as fits.
        if (Width == 0)
        {
            (Width, Height) = (width, height);
        }

        (_left, _top, _shownWidth, _shownHeight) = (left, top, width, height);
        (_frame, _frameRows, _contiguous) = (frame, rows, new byte[length]);
        return HRESULT.S_OK;
    }

    private static (int Left, int Top, int Width, int Height)? Aperture(IMFMediaType type, in Guid key)
    {
        var area = new byte[Unsafe.SizeOf<MFVideoArea>()];
        uint length = 0;
        if (type.GetBlob(key, area, ref length).Failed || length != area.Length)
        {
            return null;
        }

        var aperture = MemoryMarshal.Read<MFVideoArea>(area);
        return (aperture.OffsetX.value, aperture.OffsetY.value, aperture.Area.cx, aperture.Area.cy);
    }

    // The friendly overload leaves each sample to the collector, which the decoder may be waiting on; owning it needs its address.
    private unsafe HRESULT ReadSample(out MF_SOURCE_READER_FLAG flags, out long time, out IMFSample? sample)
    {
        uint streamFlags;
        long timestamp;
        IMFSample_unmanaged* read = null;
        var status = _reader.ReadSample(FirstVideoStream, 0, null, &streamFlags, &timestamp, &read);
        (flags, time) = ((MF_SOURCE_READER_FLAG)streamFlags, timestamp);
        sample = read is null ? null : ComObjects.Own<IMFSample>((nint)read);
        return status;
    }

    private TimeSpan Copy(IMFSample sample, long time, Span<byte> pixels)
    {
        if (sample.GetSampleDuration(out var duration).Failed || duration <= 0)
        {
            duration = _previousTime >= 0 && time > _previousTime ? time - _previousTime : DefaultFrame.Ticks;
        }

        _previousTime = time;
        if (sample.CopyToBuffer(_frame).Failed || _frameRows!.ContiguousCopyTo(out _contiguous[0], (uint)_contiguous.Length).Failed)
        {
            return TimeSpan.FromTicks(duration);
        }

        var pitch = _contiguous.Length / _frameHeight;
        var (row, copied) = (Width * 4, Math.Min(Width, _shownWidth) * 4);
        for (var y = 0; y < Math.Min(Height, _shownHeight); y++)
        {
            // A negative stride stores the rows bottom-up, so the top row is the buffer's last.
            var line = _stride < 0 ? _frameHeight - 1 - (_top + y) : _top + y;
            var target = pixels.Slice(y * row, copied);
            _contiguous.AsSpan((line * pitch) + (_left * 4), copied).CopyTo(target);
            // RGB32 leaves the fourth byte undefined; the frame is opaque.
            for (var alpha = 3; alpha < copied; alpha += 4)
            {
                target[alpha] = 0xFF;
            }
        }

        return TimeSpan.FromTicks(duration);
    }

    private HRESULT Rewind()
    {
        _previousTime = -1;
        var start = default(PROPVARIANT);
        start.vt = VARENUM.VT_I8;
        return _reader.SetCurrentPosition(Guid.Empty, start);
    }
}
