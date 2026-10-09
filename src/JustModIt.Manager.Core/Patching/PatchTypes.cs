namespace JustModIt.Manager.Core.Patching;

/// <summary>What a patch promises to produce.</summary>
/// <param name="Size">The result's size in bytes.</param>
/// <param name="Sha256">The result's SHA-256 in lower-case hex.</param>
public sealed record PatchTarget(long Size, string Sha256);

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
