using System.Numerics;

namespace JustModIt.Manager.Core.Patching;

/// <summary>The compressor xdelta3 applies on top of its delta encoding.</summary>
public enum XDelta3Secondary
{
    None = 0,
    Djw = 1,
    Lzma = 2,
}

public sealed record XDelta3EncodeOptions
{
    public const int MinWindowSize = 16 << 10;

    /// <summary>xdelta3 decoders reject target windows above <c>XD3_HARDMAXWINSIZE</c>.</summary>
    public const int MaxWindowSize = 64 << 20;

    private const long MinSourceWindow = 16L << 20;
    private const long MinMemoryBudget = 64L << 20;
    private const long MaxMemoryBudget = 1L << 30;

    /// <summary>0 (no compression) to 9, as in <c>xdelta3 -0</c> to <c>-9</c>.</summary>
    public int Level { get; init; } = 9;

    // Older Windows xdelta3 builds that authors still use, and tools bundling them, cannot decode LZMA, while DJW
    // compresses game data as well or better.
    public XDelta3Secondary Secondary { get; init; } = XDelta3Secondary.Djw;

    /// <summary>How much of the source is indexed at once; 0 indexes all of it.</summary>
    public long SourceWindow { get; init; }

    public int WindowSize { get; init; } = 8 << 20;

    /// <summary>
    /// A quarter of the machine's memory, within 64 MiB and 1 GiB, for the encoder's match index, so encoding while the
    /// player works in the app does not push other programs out of memory.
    /// </summary>
    public static long DefaultMemoryBudget() =>
        Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 4, MinMemoryBudget, MaxMemoryBudget);

    /// <summary>
    /// The highest level whose whole-source index fits the memory budget; a sliding source window only when even level 1
    /// does not fit, because it cannot find data that moved further than half the window.
    /// </summary>
    /// <param name="sourceLength">The original's size in bytes.</param>
    /// <param name="memoryBudget">The bytes the match index may take.</param>
    public static XDelta3EncodeOptions ForSource(long sourceLength, long memoryBudget)
    {
        foreach (var level in (ReadOnlySpan<int>)[9, 6, 3, 2, 1])
        {
            if (EstimateIndexBytes(sourceLength, level) <= memoryBudget)
            {
                return new XDelta3EncodeOptions { Level = level };
            }
        }

        var window = 1L << 62;
        while (window > MinSourceWindow && EstimateIndexBytes(sourceLength, 1, window) > memoryBudget)
        {
            window >>= 1;
        }

        return new XDelta3EncodeOptions { Level = 1, SourceWindow = window };
    }

    /// <summary>The size of xdelta3's source match table for this level, as its string matchers allocate it.</summary>
    /// <param name="sourceLength">The original's size in bytes.</param>
    /// <param name="level">The compression level, 0 to 9.</param>
    /// <param name="sourceWindow">The bytes indexed at once; 0 for the whole source.</param>
    public static long EstimateIndexBytes(long sourceLength, int level, long sourceWindow = 0)
    {
        var step = level switch
        {
            1 => 26,
            2 => 15,
            <= 5 => 8,
            6 => 3,
            _ => 2,
        };
        var indexed = sourceWindow > 0 ? Math.Min(sourceWindow, sourceLength) : sourceLength;
        var window = (long)BitOperations.RoundUpToPowerOf2((ulong)Math.Max(indexed, MinWindowSize));
        var slots = window / step;
        return slots <= 0 ? 0 : (1L << BitOperations.Log2((ulong)slots)) * 8;
    }
}
