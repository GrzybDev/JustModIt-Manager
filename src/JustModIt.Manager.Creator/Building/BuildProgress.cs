namespace JustModIt.Manager.Creator.Building;

public enum BuildStage
{
    /// <summary>Making sure the files are as the plan saw them.</summary>
    Checking,

    Copying,

    /// <summary>Hashing the original a patch is made against.</summary>
    HashingOriginal,

    CreatingPatch,

    /// <summary>Hashing the game's own files that the package's software changes while the game runs, for the manifest.</summary>
    HashingRuntimeFiles,

    WritingManifest,
}

/// <summary>
/// Progress of a package build, weighted by bytes: a copy counts its file, a patch its original (hashed) and the
/// author's file (encoded).
/// </summary>
/// <param name="Stage">What the build is doing.</param>
/// <param name="CurrentFile">The game path of the copy or patch being written; null while checking or writing the manifest.</param>
/// <param name="FileNumber">The file being worked on, counting copies and patches from 1.</param>
/// <param name="FileCount">The copies and patches in all.</param>
/// <param name="BytesDone">Weighted bytes done so far.</param>
/// <param name="BytesTotal">Weighted bytes of the whole build.</param>
public readonly record struct BuildProgress(BuildStage Stage, string? CurrentFile, int FileNumber, int FileCount, long BytesDone,
    long BytesTotal);
