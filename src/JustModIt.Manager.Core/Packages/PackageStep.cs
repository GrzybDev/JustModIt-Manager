namespace JustModIt.Manager.Core.Packages;

public enum PackageKind
{
    /// <summary>A ZIP with a manifest of operations; only the app installs it.</summary>
    Manifest,

    /// <summary>A ZIP of files laid out relative to the game folder, which players can also unpack by hand.</summary>
    Zip,
}

/// <summary>
/// One validated change a package makes, whichever kind it came from, with game-relative paths, <c>Entry</c> naming the
/// archive entry holding the payload, and lower-case hex hashes.
/// </summary>
public abstract record PackageStep;

/// <summary>Copies a payload file into the game.</summary>
/// <param name="Path">The game-relative destination.</param>
/// <param name="Entry">The archive entry holding the file.</param>
/// <param name="Size">The file's size in bytes.</param>
/// <param name="Sha256">Null for a plain .zip, which carries no hashes.</param>
public sealed record CopyStep(string Path, string Entry, long Size, string? Sha256) : PackageStep;

/// <summary>Patches a game file with a payload.</summary>
/// <param name="Path">The game-relative file to patch.</param>
/// <param name="Entry">The archive entry holding the xdelta3 patch.</param>
/// <param name="SourceSize">The original's size in bytes.</param>
/// <param name="SourceSha256">The original's SHA-256.</param>
/// <param name="TargetSize">The patched file's size in bytes.</param>
/// <param name="TargetSha256">The patched file's SHA-256.</param>
public sealed record PatchStep(string Path, string Entry, long SourceSize, string SourceSha256, long TargetSize,
    string TargetSha256) : PackageStep;

/// <summary>Renames a game file.</summary>
/// <param name="From">The game-relative file to rename.</param>
/// <param name="To">Its new game-relative path.</param>
public sealed record RenameStep(string From, string To) : PackageStep;

/// <summary>Deletes a game file.</summary>
/// <param name="Path">The game-relative file to delete.</param>
public sealed record DeleteStep(string Path) : PackageStep;
