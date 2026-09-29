namespace JustModIt.Manager.Core.Patching;

/// <summary>What a patch promises to produce.</summary>
/// <param name="Size">The result's size in bytes.</param>
/// <param name="Sha256">The result's SHA-256 in lower-case hex.</param>
public sealed record PatchTarget(long Size, string Sha256);

public interface IPatchDecoder
{
    /// <summary>Applies a patch and writes the checked result, flushed to disk, to a new file; a failure leaves no output.</summary>
    /// <param name="sourcePath">The original, taken as given: callers check it, and a wrong one cannot produce the promised hash.</param>
    /// <param name="patch">The VCDIFF patch, read to its end.</param>
    /// <param name="outputPath">Where the result goes; nothing may exist there yet.</param>
    /// <param name="target">What the result must match.</param>
    /// <param name="progress">Bytes written since the previous report.</param>
    /// <param name="cancellationToken">Stops decoding and removes the output.</param>
    /// <exception cref="PatchException">The patch is damaged, or its result differs from the target.</exception>
    Task DecodeAsync(string sourcePath, Stream patch, string outputPath, PatchTarget target, IProgress<long>? progress,
        CancellationToken cancellationToken);
}

public enum PatchStage
{
    HashingSource,
    Encoding,
}

/// <summary>How far an encode has got.</summary>
/// <param name="Stage">What is being read.</param>
/// <param name="Done">Bytes of the stage's file read so far.</param>
/// <param name="Total">The size of the stage's file.</param>
public readonly record struct PatchProgress(PatchStage Stage, long Done, long Total);

/// <summary>What an encode read and wrote, for the package's manifest.</summary>
/// <param name="SourceSize">The original's size in bytes.</param>
/// <param name="SourceSha256">The original's SHA-256 in lower-case hex.</param>
/// <param name="TargetSize">The changed file's size in bytes.</param>
/// <param name="TargetSha256">The changed file's SHA-256 in lower-case hex.</param>
/// <param name="PatchSize">Bytes written to the patch stream.</param>
public sealed record PatchInfo(long SourceSize, string SourceSha256, long TargetSize, string TargetSha256, long PatchSize);

public interface IPatchEncoder
{
    /// <summary>Writes a patch that turns one file into another; for a large source it may use a sizeable share of memory.</summary>
    /// <param name="sourcePath">The original file.</param>
    /// <param name="targetPath">The changed file.</param>
    /// <param name="patch">The stream the patch is written to.</param>
    /// <param name="progress">Reports hashing the original, then encoding the changed file.</param>
    /// <param name="cancellationToken">Stops encoding.</param>
    Task<PatchInfo> EncodeAsync(string sourcePath, string targetPath, Stream patch, IProgress<PatchProgress>? progress,
        CancellationToken cancellationToken);
}
