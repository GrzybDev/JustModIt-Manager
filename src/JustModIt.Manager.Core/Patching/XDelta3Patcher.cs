using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Patching;

/// <summary>VCDIFF patches as xdelta3 3.2.0 makes and reads them, through the native shim.</summary>
/// <param name="memoryBudget">For the encoder's match index; <see cref="XDelta3EncodeOptions.DefaultMemoryBudget"/> when null.</param>
public sealed class XDelta3Patcher(long? memoryBudget = null) : IPatchDecoder, IPatchEncoder
{
    private const int OutputBufferSize = 1 << 20;

    public async Task DecodeAsync(string sourcePath, Stream patch, string outputPath, PatchTarget target,
        IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        ArgumentNullException.ThrowIfNull(target);
        XDelta3Native.EnsureAvailable();
        // Writers are locked out, so the source cannot change while it is read in blocks.
        using var source = File.OpenHandle(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        var sourceLength = RandomAccess.GetLength(source);
        var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, OutputBufferSize,
            FileOptions.Asynchronous);
        try
        {
            await using (output.ConfigureAwait(false))
            {
                // Fails fast on a full disk and keeps large game files in one piece.
                output.SetLength(target.Size);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long written = 0;
                await XDelta3Codec.DecodeAsync(source, sourceLength, patch, output, target.Size, span =>
                {
                    hash.AppendData(span);
                    written += span.Length;
                    progress?.Report(span.Length);
                }, cancellationToken).ConfigureAwait(false);

                // A patch truncated on a window boundary decodes without an error, so length and hash are the real check.
                if (written != target.Size)
                {
                    throw new PatchException(PatchError.OutputMismatch, $"{written} bytes instead of {target.Size}");
                }

                var produced = Convert.ToHexStringLower(hash.GetHashAndReset());
                if (!FileHash.Matches(produced, target.Sha256))
                {
                    throw new PatchException(PatchError.OutputMismatch, $"sha256 {produced} instead of {target.Sha256}");
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
        }
        catch
        {
            TryDelete(outputPath);
            throw;
        }
    }

    public async Task<PatchInfo> EncodeAsync(string sourcePath, string targetPath, Stream patch,
        IProgress<PatchProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        XDelta3Native.EnsureAvailable();
        var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (sourceStream.ConfigureAwait(false))
        {
            var sourceLength = sourceStream.Length;
            long hashed = 0;
            var hashing = new InlineProgress<long>(bytes =>
                progress?.Report(new PatchProgress(PatchStage.HashingSource, hashed += bytes, sourceLength)));
            var sourceSha256 = await FileHash.ComputeAsync(sourceStream, HashAlgorithmName.SHA256, hashing, cancellationToken)
                .ConfigureAwait(false);

            var target = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (target.ConfigureAwait(false))
            {
                var targetLength = target.Length;
                var options = XDelta3EncodeOptions.ForSource(sourceLength, memoryBudget ?? XDelta3EncodeOptions.DefaultMemoryBudget());
                using var targetHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long encoded = 0;
                long patchSize;
                using (var mapping = MappedSource.Open(sourceStream))
                {
                    patchSize = await XDelta3Codec.EncodeAsync(mapping.Address, sourceLength, target, targetLength, patch, options, span =>
                    {
                        targetHash.AppendData(span);
                        encoded += span.Length;
                        progress?.Report(new PatchProgress(PatchStage.Encoding, encoded, targetLength));
                    }, cancellationToken).ConfigureAwait(false);
                }

                return new PatchInfo(sourceLength, sourceSha256, encoded, Convert.ToHexStringLower(targetHash.GetHashAndReset()),
                    patchSize);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The source as a read-only mapping, so the page cache holds what the encoder random-accesses instead of a private
    /// copy; decoding reads blocks instead, because an I/O error on a mapped page ends the process.
    /// </summary>
    private sealed unsafe class MappedSource : IDisposable
    {
        private readonly MemoryMappedFile? _mapping;
        private readonly MemoryMappedViewAccessor? _view;

        private MappedSource(MemoryMappedFile? mapping, MemoryMappedViewAccessor? view, nint address)
        {
            _mapping = mapping;
            _view = view;
            Address = address;
        }

        public nint Address { get; }

        public static MappedSource Open(FileStream source)
        {
            if (source.Length == 0)
            {
                return new MappedSource(null, null, 0);
            }

            var mapping = MemoryMappedFile.CreateFromFile(source, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None,
                leaveOpen: true);
            var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            byte* pointer = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            return new MappedSource(mapping, view, (nint)(pointer + view.PointerOffset));
        }

        public void Dispose()
        {
            if (_view is not null)
            {
                _view.SafeMemoryMappedViewHandle.ReleasePointer();
                _view.Dispose();
            }

            _mapping?.Dispose();
        }
    }
}
