using Microsoft.Win32.SafeHandles;

namespace JustModIt.Manager.Core.Patching;

/// <summary>
/// A small LRU of source blocks in native memory, freed as soon as the patch is done, where every slot but the block
/// xdelta3 asked for last may be reused, since that is the only one it keeps a pointer to.
/// </summary>
/// <param name="source">The original, open for random access.</param>
/// <param name="sourceLength">The original's size in bytes.</param>
/// <param name="slots">How many blocks to keep.</param>
internal sealed class SourceBlockCache(SafeFileHandle source, long sourceLength, int slots) : IDisposable
{
    private readonly long[] _blockNumbers = [.. Enumerable.Repeat(-1L, slots)];
    private readonly UnmanagedBytes?[] _buffers = new UnmanagedBytes?[slots];
    private readonly int[] _lengths = new int[slots];
    private readonly long[] _lastUse = new long[slots];
    private int _blockSize;
    private long _clock;

    /// <summary>Hands out a source block, reading it into the least recently used slot unless a slot holds it.</summary>
    /// <param name="blockNumber">The block xdelta3 asked for.</param>
    /// <param name="blockSize">The size xdelta3 reports: it clamps the requested size for small sources.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The address of the block, which stays valid until another block takes its slot.</returns>
    public async ValueTask<(nint Address, int Length)> GetAsync(long blockNumber, int blockSize,
        CancellationToken cancellationToken)
    {
        EnsureBuffers(blockSize);
        var victim = 0;
        for (var i = 0; i < _blockNumbers.Length; i++)
        {
            if (_blockNumbers[i] == blockNumber)
            {
                _lastUse[i] = ++_clock;
                return (_buffers[i]!.Address, _lengths[i]);
            }

            if (_lastUse[i] < _lastUse[victim])
            {
                victim = i;
            }
        }

        var offset = blockNumber * _blockSize;
        var wanted = (int)Math.Clamp(sourceLength - offset, 0, _blockSize);
        var buffer = _buffers[victim]!;
        var read = 0;
        while (read < wanted)
        {
            var count = await RandomAccess.ReadAsync(source, buffer.Memory.Slice(read, wanted - read), offset + read,
                cancellationToken).ConfigureAwait(false);
            if (count <= 0)
            {
                throw new EndOfStreamException("The source file shrank while it was being patched.");
            }

            read += count;
        }

        _blockNumbers[victim] = blockNumber;
        _lengths[victim] = read;
        _lastUse[victim] = ++_clock;
        return (buffer.Address, read);
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers)
        {
            buffer?.Free();
        }
    }

    private void EnsureBuffers(int blockSize)
    {
        if (_blockSize == blockSize)
        {
            return;
        }

        if (_blockSize != 0)
        {
            throw new PatchException(PatchError.Damaged, "the source block size changed mid-stream");
        }

        _blockSize = blockSize;
        for (var i = 0; i < _buffers.Length; i++)
        {
            _buffers[i] = UnmanagedBytes.Allocate(blockSize);
        }
    }
}
