using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JustModIt.Manager.Core.Patching;

/// <summary>
/// One native encoder or decoder, whose addresses stay inside these synchronous methods so the loop driving it can await
/// I/O between steps.
/// </summary>
internal sealed class XDelta3Session : IDisposable
{
    // Older Windows xdelta3 builds that authors still use, and tools bundling them, cannot decode LZMA, while DJW
    // compresses game data as well or better.
    private const int DjwSecondary = 1;

    private readonly XDelta3Native.Session _session;

    // Native memory, because xdelta3 keeps a pointer to its input until it asks for more.
    private readonly UnmanagedBytes _input;
    private CancellationTokenRegistration _cancellation;

    private XDelta3Session(XDelta3Native.Session session, int inputSize)
    {
        _session = session;
        _input = UnmanagedBytes.Allocate(inputSize);
    }

    public Memory<byte> InputBuffer => _input.Memory;

    /// <summary>Starts a decoder that asks for source blocks.</summary>
    /// <param name="sourceLength">The original's size in bytes; 0 for a patch made without a source.</param>
    /// <param name="blockSize">The size of the source blocks to ask for.</param>
    /// <param name="inputSize">How many bytes of patch to feed per step.</param>
    public static XDelta3Session CreateDecoder(long sourceLength, int blockSize, int inputSize)
    {
        var result = XDelta3Native.DecoderNew((ulong)sourceLength, (ulong)blockSize, out var session);
        return result == 0 ? new XDelta3Session(session, inputSize) : throw CreateError(result, session, "decoder init");
    }

    /// <summary>Starts an encoder over a source held in memory.</summary>
    /// <param name="source">The whole source, which must stay valid until the session is disposed.</param>
    /// <param name="sourceLength">The original's size in bytes.</param>
    /// <param name="options">The level and the source window.</param>
    /// <param name="windowSize">The target bytes per window, which is also the input buffer's size.</param>
    public static XDelta3Session CreateEncoder(nint source, long sourceLength, XDelta3EncodeOptions options, int windowSize)
    {
        var native = new XDelta3Native.EncodeOptions
        {
            StructSize = (uint)Unsafe.SizeOf<XDelta3Native.EncodeOptions>(),
            Level = options.Level,
            Secondary = DjwSecondary,
            WindowSize = (ulong)windowSize,
            SourceWindowSize = (ulong)Math.Max(0, options.SourceWindow),
        };
        var result = XDelta3Native.EncoderNew(source, (ulong)sourceLength, native, out var session);
        return result == 0 ? new XDelta3Session(session, windowSize) : throw CreateError(result, session, "encoder init");
    }

    /// <summary>Lets a cancellation interrupt a long native call, such as indexing a large source.</summary>
    /// <param name="cancellationToken">The token that aborts the session.</param>
    public void CancelWith(CancellationToken cancellationToken) =>
        _cancellation = cancellationToken.UnsafeRegister(static state => XDelta3Native.Abort((XDelta3Native.Session)state!), _session);

    /// <summary>Runs the session until it needs input or a source block, has output or is done.</summary>
    /// <param name="output">The output's address, valid until the next step.</param>
    /// <param name="length">The output's size in bytes.</param>
    /// <returns>One of the step results of <see cref="XDelta3Native"/>, or an xdelta3 error code.</returns>
    public int Step(out nint output, out int length)
    {
        var result = XDelta3Native.Step(_session, out output, out var producedLength);
        // Output comes one window at a time, and windows are at most 64 MiB.
        length = checked((int)producedLength);
        return result;
    }

    /// <summary>Hands the session what was read into <see cref="InputBuffer"/>.</summary>
    /// <param name="length">How many bytes of the buffer were filled.</param>
    /// <param name="final">Whether no input follows.</param>
    public void Input(int length, bool final)
    {
        var result = XDelta3Native.Input(_session, _input.Address, (ulong)length, final ? 1 : 0);
        if (result != 0)
        {
            throw CreateError(result, _session, "input");
        }
    }

    /// <summary>The source block the decoder asked for, and the block size it uses.</summary>
    public (long Block, int BlockSize) SourceRequest()
    {
        var block = XDelta3Native.SourceRequest(_session, out var blockSize);
        return ((long)block, checked((int)blockSize));
    }

    /// <summary>Serves the source block the decoder asked for.</summary>
    /// <param name="block">The block's number.</param>
    /// <param name="data">The block's address, which must stay valid until the decoder asks for the next one.</param>
    /// <param name="length">The block's size in bytes.</param>
    public void SourceBlock(long block, nint data, int length)
    {
        var result = XDelta3Native.SourceBlock(_session, (ulong)block, data, (ulong)length);
        if (result != 0)
        {
            throw CreateError(result, _session, "source block");
        }
    }

    /// <summary>The exception for a step that failed, with xdelta3's description.</summary>
    /// <param name="code">The error code the step returned.</param>
    public PatchException Error(int code) => CreateError(code, _session, "xdelta3");

    public void Dispose()
    {
        // Waits for a cancellation callback in flight, which must not touch a freed session.
        _cancellation.Dispose();
        _session.Dispose();
        _input.Free();
    }

    private static PatchException CreateError(int code, XDelta3Native.Session session, string operation)
    {
        var detail = session.IsInvalid ? string.Empty : XDelta3Native.ToManaged(XDelta3Native.Message(session));
        var error = XDelta3Native.ToManaged(XDelta3Native.StrError(code));
        return new PatchException(PatchError.Damaged,
            detail.Length > 0 ? $"{operation}: {error} ({code}): {detail}" : $"{operation}: {error} ({code})");
    }
}

/// <summary>Unmanaged memory as <see cref="Memory{T}"/>, for asynchronous I/O straight into or out of it.</summary>
/// <param name="address">The memory's start.</param>
/// <param name="length">The memory's size in bytes.</param>
internal sealed class UnmanagedBytes(nint address, int length) : MemoryManager<byte>
{
    public nint Address => address;

    /// <summary>Allocates native memory, which only <see cref="Free"/> returns.</summary>
    /// <param name="length">The size in bytes.</param>
    public static UnmanagedBytes Allocate(int length) => new(Marshal.AllocHGlobal(length), length);

    /// <summary>Frees memory from <see cref="Allocate"/>, and only such memory.</summary>
    public void Free() => Marshal.FreeHGlobal(address);

    // Only a pointer turns native memory into a span.
    public override unsafe Span<byte> GetSpan() => new((void*)address, length);

    // Native memory never moves, so a pin is its address, which only a pointer holds.
    public override unsafe MemoryHandle Pin(int elementIndex = 0) => new((byte*)address + elementIndex);

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
}
