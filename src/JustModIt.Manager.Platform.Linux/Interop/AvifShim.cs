using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JustModIt.Manager.Platform.Linux.Interop;

/// <summary>The C ABI of <c>native/avifshim/avifshim.c</c>, whose library loads on first use, never at startup.</summary>
internal static partial class AvifShim
{
    private const string Library = "avifshim";

    /// <summary>Reads an AVIF picture's own size; returns 0, or 1 when the data is no AVIF picture or damaged.</summary>
    /// <param name="data">The picture's file.</param>
    /// <param name="size">The file's length in bytes.</param>
    /// <param name="width">Receives the picture's width.</param>
    /// <param name="height">Receives the picture's height.</param>
    [LibraryImport(Library, EntryPoint = "avs_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Size(ReadOnlySpan<byte> data, nuint size, out int width, out int height);

    /// <summary>Decodes an AVIF picture's first frame scaled to a size; returns 0, or 1 when it cannot be decoded.</summary>
    /// <param name="data">The picture's file.</param>
    /// <param name="size">The file's length in bytes.</param>
    /// <param name="width">The width to scale to.</param>
    /// <param name="height">The height to scale to.</param>
    /// <param name="pixels">Receives width × height × 4 bytes: rows of pixels, blue, green, red and premultiplied alpha.</param>
    [LibraryImport(Library, EntryPoint = "avs_decode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Decode(ReadOnlySpan<byte> data, nuint size, int width, int height, Span<byte> pixels);
}
