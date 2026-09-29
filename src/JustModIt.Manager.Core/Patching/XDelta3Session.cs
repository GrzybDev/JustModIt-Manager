using System.Buffers;
using System.Runtime.InteropServices;

namespace JustModIt.Manager.Core.Patching;

/// <summary>
/// One native encoder or decoder, whose pointers stay inside these synchronous methods so the loop driving it can await
/// I/O between steps.
/// </summary>
internal sealed unsafe class XDelta3Session : IDisposable
{
    private readonly nint _handle;

    // Native memory, because xdelta3 keeps a pointer to its input until it asks for more.
    private readonly UnmanagedBytes _input;
    private CancellationTokenRegistration _cancellation;

    private XDelta3Session(nint handle, int inputSize)
    {
        _handle = handle;
        _input = UnmanagedBytes.Allocate(inputSize);
    }

    public Memory<byte> InputBuffer => _input.Memory;

    /// <summary>Starts a decoder that asks for source blocks.</summary>
    /// <param name="sourceLength">The original's size in bytes; 0 for a patch made without a source.</param>
    /// <param name="blockSize">The size of the source blocks to ask for.</param>
    /// <param name="inputSize">How many bytes of patch to feed per step.</param>
    public static XDelta3Session CreateDecoder(long sourceLength, int blockSize, int inputSize)
    {
        nint handle;
        var result = XDelta3Native.DecoderNew((ulong)sourceLength, (ulong)blockSize, &handle);
        return result == 0 ? new XDelta3Session(handle, inputSize) : throw CreateError(result, 0, "decoder init");
    }

    /// <summary>Starts an encoder over a source held in memory.</summary>
    /// <param name="source">The whole source, which must stay valid until the session is disposed.</param>
    /// <param name="sourceLength">The original's size in bytes.</param>
    /// <param name="options">The level, source window and secondary compression.</param>
    /// <param name="windowSize">The target bytes per window, which is also the input buffer's size.</param>
    public static XDelta3Session CreateEncoder(nint source, long sourceLength, XDelta3EncodeOptions options, int windowSize)
    {
        var native = new XDelta3Native.EncodeOptions
        {
            StructSize = (uint)sizeof(XDelta3Native.EncodeOptions),
            Level = options.Level,
            Secondary = (int)options.Secondary,
            WindowSize = (ulong)windowSize,
            SourceWindowSize = (ulong)Math.Max(0, options.SourceWindow),
        };
        nint handle;
        var result = XDelta3Native.EncoderNew((byte*)source, (ulong)sourceLength, &native, &handle);
        return result == 0 ? new XDelta3Session(handle, windowSize) : throw CreateError(result, 0, "encoder init");
    }

    /// <summary>Lets a cancellation interrupt a long native call, such as indexing a large source.</summary>
    /// <param name="cancellationToken">The token that aborts the session.</param>
    public void CancelWith(CancellationToken cancellationToken) =>
        _cancellation = cancellationToken.UnsafeRegister(static state => XDelta3Native.Abort((nint)state!), _handle);

    /// <summary>Runs the session until it needs input or a source block, has output or is done.</summary>
    /// <param name="output">The output's address, valid until the next step.</param>
    /// <param name="length">The output's size in bytes.</param>
    /// <returns>One of the step results of <see cref="XDelta3Native"/>, or an xdelta3 error code.</returns>
    public int Step(out nint output, out int length)
    {
        byte* produced;
        ulong producedLength;
        var result = XDelta3Native.Step(_handle, &produced, &producedLength);
        output = (nint)produced;
        // Output comes one window at a time, and windows are at most 64 MiB.
        length = checked((int)producedLength);
        return result;
    }

    /// <summary>Hands the session what was read into <see cref="InputBuffer"/>.</summary>
    /// <param name="length">How many bytes of the buffer were filled.</param>
    /// <param name="final">Whether no input follows.</param>
    public void Input(int length, bool final)
    {
        var result = XDelta3Native.Input(_handle, (byte*)_input.Address, (ulong)length, final ? 1 : 0);
        if (result != 0)
        {
            throw CreateError(result, _handle, "input");
        }
    }

    /// <summary>The source block the decoder asked for, and the block size it uses.</summary>
    public (long Block, int BlockSize) SourceRequest()
    {
        ulong blockSize;
        var block = XDelta3Native.SourceRequest(_handle, &blockSize);
        return ((long)block, checked((int)blockSize));
    }

    /// <summary>Serves the source block the decoder asked for.</summary>
    /// <param name="block">The block's number.</param>
    /// <param name="data">The block's address, which must stay valid until the decoder asks for the next one.</param>
    /// <param name="length">The block's size in bytes.</param>
    public void SourceBlock(long block, nint data, int length)
    {
        var result = XDelta3Native.SourceBlock(_handle, (ulong)block, (byte*)data, (ulong)length);
        if (result != 0)
        {
            throw CreateError(result, _handle, "source block");
        }
    }

    /// <summary>The exception for a step that failed, with xdelta3's description.</summary>
    /// <param name="code">The error code the step returned.</param>
    public PatchException Error(int code) => CreateError(code, _handle, "xdelta3");

    public void Dispose()
    {
        // Waits for a cancellation callback in flight, which must not touch a freed handle.
        _cancellation.Dispose();
        XDelta3Native.Free(_handle);
        _input.Free();
    }

    private static PatchException CreateError(int code, nint handle, string operation)
    {
        var detail = handle != 0 ? XDelta3Native.ToManaged(XDelta3Native.Message(handle)) : string.Empty;
        var error = XDelta3Native.ToManaged(XDelta3Native.StrError(code));
        return new PatchException(PatchError.Damaged,
            detail.Length > 0 ? $"{operation}: {error} ({code}): {detail}" : $"{operation}: {error} ({code})");
    }
}

/// <summary>Unmanaged memory as <see cref="Memory{T}"/>, for asynchronous I/O straight into or out of it.</summary>
/// <param name="address">The memory's start.</param>
/// <param name="length">The memory's size in bytes.</param>
internal sealed unsafe class UnmanagedBytes(nint address, int length) : MemoryManager<byte>
{
    public nint Address => address;

    /// <summary>Allocates native memory, which only <see cref="Free"/> returns.</summary>
    /// <param name="length">The size in bytes.</param>
    public static UnmanagedBytes Allocate(int length) => new((nint)NativeMemory.Alloc((nuint)length), length);

    /// <summary>Frees memory from <see cref="Allocate"/>, and only such memory.</summary>
    public void Free() => NativeMemory.Free((void*)address);

    public override Span<byte> GetSpan() => new((void*)address, length);

    public override MemoryHandle Pin(int elementIndex = 0) => new((byte*)address + elementIndex);

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
}
