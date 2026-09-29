using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JustModIt.Manager.Core.Patching;

/// <summary>The C ABI of <c>native/xd3shim/xd3shim.c</c>, whose library loads on first use, never at startup.</summary>
internal static unsafe partial class XDelta3Native
{
    public const int NeedInput = 1;
    public const int HaveOutput = 2;
    public const int Done = 3;
    public const int NeedSource = 4;

    private const string Library = "xd3shim";

    // The ABI this code was written for; a shim built from older sources has another prefix.
    private const string CompatibleVersion = "xd3shim 2 ";

    private static readonly Lazy<string?> LoadError = new(CheckVersion);

    [StructLayout(LayoutKind.Sequential)]
    public struct EncodeOptions
    {
        public uint StructSize;
        public int Level;
        public ulong WindowSize;
        public ulong SourceWindowSize;
        public int Secondary;
    }

    /// <summary>Checks, once per process, that the shim loads and matches this code.</summary>
    /// <exception cref="PatchException">The shim is missing or was built from other sources.</exception>
    public static void EnsureAvailable()
    {
        if (LoadError.Value is { } error)
        {
            throw new PatchException(PatchError.EngineUnavailable, error);
        }
    }

    /// <summary>A string from the shim; empty for a null pointer.</summary>
    /// <param name="text">A NUL-terminated UTF-8 string that the shim owns.</param>
    public static string ToManaged(byte* text) => text == null ? string.Empty : Marshal.PtrToStringUTF8((nint)text) ?? string.Empty;

    /// <summary>The xdelta3 version the shim was built with, as a string the shim owns.</summary>
    [LibraryImport(Library, EntryPoint = "xds_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte* Version();

    /// <summary>Starts an encoder; returns 0 or an xdelta3 error code.</summary>
    /// <param name="source">The whole source, which must stay valid until <see cref="Free"/>.</param>
    /// <param name="sourceLength">The source's size in bytes.</param>
    /// <param name="options">The encoder's settings.</param>
    /// <param name="handle">Receives the session.</param>
    [LibraryImport(Library, EntryPoint = "xds_encoder_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int EncoderNew(byte* source, ulong sourceLength, EncodeOptions* options, nint* handle);

    /// <summary>Starts a decoder that asks for source blocks; returns 0 or an xdelta3 error code.</summary>
    /// <param name="sourceLength">The source's size in bytes; 0 for a patch without a source.</param>
    /// <param name="blockSize">The size of the source blocks to ask for.</param>
    /// <param name="handle">Receives the session.</param>
    [LibraryImport(Library, EntryPoint = "xds_decoder_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int DecoderNew(ulong sourceLength, ulong blockSize, nint* handle);

    /// <summary>Hands the session more input; returns 0 or an xdelta3 error code.</summary>
    /// <param name="handle">The session.</param>
    /// <param name="buffer">The input, which must stay valid until <see cref="Step"/> asks for more.</param>
    /// <param name="length">The input's size in bytes; an encoder needs whole windows except for the last.</param>
    /// <param name="final">1 when no input follows, otherwise 0.</param>
    [LibraryImport(Library, EntryPoint = "xds_input")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Input(nint handle, byte* buffer, ulong length, int final);

    /// <summary>Runs the session until it needs input or a source block, has output or is done.</summary>
    /// <param name="handle">The session.</param>
    /// <param name="output">Receives the output, valid until the next step.</param>
    /// <param name="outputLength">Receives the output's size in bytes.</param>
    /// <returns><see cref="NeedInput"/>, <see cref="HaveOutput"/>, <see cref="Done"/>, <see cref="NeedSource"/> or an xdelta3 error code.</returns>
    [LibraryImport(Library, EntryPoint = "xds_step")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Step(nint handle, byte** output, ulong* outputLength);

    /// <summary>The number of the source block a decoder asked for.</summary>
    /// <param name="handle">The session.</param>
    /// <param name="blockSize">Receives the block size the session uses.</param>
    [LibraryImport(Library, EntryPoint = "xds_source_request")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial ulong SourceRequest(nint handle, ulong* blockSize);

    /// <summary>Serves the source block a decoder asked for; returns 0 or an xdelta3 error code.</summary>
    /// <param name="handle">The session.</param>
    /// <param name="blockNumber">The block's number.</param>
    /// <param name="data">The block, which must stay valid until the session asks for the next one.</param>
    /// <param name="length">The block's size in bytes.</param>
    [LibraryImport(Library, EntryPoint = "xds_source_block")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SourceBlock(nint handle, ulong blockNumber, byte* data, ulong length);

    /// <summary>Makes a running step fail soon; safe to call from another thread.</summary>
    /// <param name="handle">The session.</param>
    [LibraryImport(Library, EntryPoint = "xds_abort")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Abort(nint handle);

    /// <summary>xdelta3's description of the session's last error; empty when there is none.</summary>
    /// <param name="handle">The session.</param>
    [LibraryImport(Library, EntryPoint = "xds_message")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte* Message(nint handle);

    /// <summary>xdelta3's name for an error code.</summary>
    /// <param name="code">An error code that a call returned.</param>
    [LibraryImport(Library, EntryPoint = "xds_strerror")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte* StrError(int code);

    /// <summary>Ends the session and frees its memory.</summary>
    /// <param name="handle">The session.</param>
    [LibraryImport(Library, EntryPoint = "xds_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Free(nint handle);

    // Calling into a shim with another struct layout would corrupt memory instead of failing.
    private static string? CheckVersion()
    {
        try
        {
            var version = ToManaged(Version());
            return version.StartsWith(CompatibleVersion, StringComparison.Ordinal)
                ? null
                : $"The xdelta3 shim reports '{version}'; rebuild it with native/build.ps1.";
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return exception.Message;
        }
    }
}
