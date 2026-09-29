using System.Buffers;
using System.IO.Compression;

namespace JustModIt.Manager.Creator.Building;

/// <summary>
/// Picks a payload's compression from a deflated sample, which tells media and archives that are compressed already
/// from text and binaries without a list of extensions; storing what does not compress keeps large builds fast.
/// </summary>
internal static class PayloadCompression
{
    private const int SampleSize = 256 << 10;
    private const double WorthwhileRatio = 0.9;

    /// <summary>The compression for a file: optimal when a sample from its start deflates to under 90%, none otherwise.</summary>
    /// <param name="file">Positioned at its start, and rewound to it afterwards.</param>
    /// <param name="cancellationToken">Cancels reading the sample.</param>
    public static async Task<CompressionLevel> ForAsync(Stream file, CancellationToken cancellationToken)
    {
        var sample = ArrayPool<byte>.Shared.Rent(SampleSize);
        try
        {
            var read = await file.ReadAtLeastAsync(sample.AsMemory(0, SampleSize), SampleSize, throwOnEndOfStream: false,
                cancellationToken).ConfigureAwait(false);
            file.Position = 0;
            if (read == 0)
            {
                return CompressionLevel.NoCompression;
            }

            using var compressed = new MemoryStream();
            using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(sample, 0, read);
            }

            return compressed.Length < read * WorthwhileRatio ? CompressionLevel.Optimal : CompressionLevel.NoCompression;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sample);
        }
    }
}
