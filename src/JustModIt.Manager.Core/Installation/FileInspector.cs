using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Core.Installation;

public enum FileIntegrity
{
    /// <summary>As the install left it: its content, or absent for a file it deleted.</summary>
    Intact,

    /// <summary>Neither the installed nor the original content, e.g. after a game update.</summary>
    Modified,

    Missing,

    /// <summary>The game's original content is back, e.g. after the store verified the files.</summary>
    OriginalRestored,
}

public sealed record FileVerification(string Path, FileChange Change, FileIntegrity Integrity);

public sealed record VerificationResult(ValueList<FileVerification> Files)
{
    public bool IsIntact => Files.All(file => file.Integrity == FileIntegrity.Intact);
}

/// <summary>An installed file and what is at its path now.</summary>
/// <param name="File">The file as the install's record lists it.</param>
/// <param name="Path">Null when the file's path no longer resolves safely inside the game folder.</param>
/// <param name="Integrity">What is at the path now.</param>
internal sealed record InspectedFile(InstalledFile File, string? Path, FileIntegrity Integrity);

/// <summary>What a path the package's software changes holds now.</summary>
internal enum RuntimeFound
{
    /// <summary>The path no longer resolves safely, or a link now leads elsewhere, so it is never written through.</summary>
    Unresolvable,

    Folder,
    Absent,
    File,
}

/// <summary>A file the package's software changes and what its path holds now.</summary>
/// <param name="File">The file as the install's record lists it.</param>
/// <param name="Path">Its resolved full path; null when it no longer resolves.</param>
/// <param name="Found">What the path holds.</param>
/// <param name="Size">The size of a file at the path.</param>
/// <param name="Sha256">The file's hash, taken only when its size matches the original's.</param>
internal sealed record RuntimeInspection(InstalledFile File, string? Path, RuntimeFound Found, long Size = 0, string? Sha256 = null)
{
    /// <summary>Whether the path holds exactly some content.</summary>
    /// <param name="content">The content; null for no file.</param>
    public bool Holds(FileSnapshot? content) =>
        content is null
            ? Found == RuntimeFound.Absent
            : Found == RuntimeFound.File && Size == content.Size && Sha256 is { } sha256 && FileHash.Matches(sha256, content.Sha256);
}

internal static class FileInspector
{
    /// <summary>The files a check reads, leaving out those the package's software changes while the game runs.</summary>
    /// <param name="files">The install's files.</param>
    public static IEnumerable<InstalledFile> Checked(InstalledFiles files) => files.Files.Where(file => !file.Runtime);

    /// <summary>
    /// Tells what each of an install's files is now, never opening one the package's software changes, so its work never
    /// reads as damage and a running game is never locked out.
    /// </summary>
    /// <param name="files">The install's files.</param>
    /// <param name="game">The game folder they are in.</param>
    /// <param name="progress">Counts files and hashed bytes.</param>
    /// <param name="cancellationToken">Stops the inspection.</param>
    public static async Task<List<InspectedFile>> InspectAllAsync(InstalledFiles files, GameFolder game, ProgressTracker progress,
        CancellationToken cancellationToken)
    {
        var inspected = new List<InspectedFile>(files.Files.Count);
        foreach (var file in Checked(files))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.StartFile(file.Path);
            var path = game.TryResolve(file.Path);
            inspected.Add(new InspectedFile(file, path,
                await InspectAsync(file, path, progress.Bytes, cancellationToken).ConfigureAwait(false)));
            progress.FinishFile();
        }

        return inspected;
    }

    /// <summary>At most what <see cref="InspectAllAsync"/> hashes, for progress.</summary>
    /// <param name="files">The install's files.</param>
    public static long BytesToHash(InstalledFiles files) =>
        Checked(files).Sum(file => Math.Max(file.Installed?.Size ?? 0, file.Original?.Size ?? 0));

    /// <summary>
    /// Tells what each path the package's software changes holds now, reading files without locking the software out, and
    /// hashing one only when its size matches its original's.
    /// </summary>
    /// <param name="files">The entries of the paths, as the install's record lists them.</param>
    /// <param name="game">The game folder they are in.</param>
    /// <param name="progress">Receives the bytes hashed.</param>
    /// <param name="cancellationToken">Stops the inspection.</param>
    /// <exception cref="InstallBlockedException">A file changed while it was read.</exception>
    public static async Task<List<RuntimeInspection>> InspectRuntimeAsync(IEnumerable<InstalledFile> files, GameFolder game,
        IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var inspected = new List<RuntimeInspection>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = game.TryResolve(file.Path);
            // A link that appeared since the install resolves elsewhere than the path it recorded.
            if (path is null || !PathComparison.AreSame(path, PackagePath.ToFullPath(game.Root, file.Path)))
            {
                inspected.Add(new RuntimeInspection(file, null, RuntimeFound.Unresolvable));
            }
            else if (Directory.Exists(path))
            {
                inspected.Add(new RuntimeInspection(file, path, RuntimeFound.Folder));
            }
            else if (new FileInfo(path) is not { Exists: true } info)
            {
                inspected.Add(new RuntimeInspection(file, path, RuntimeFound.Absent));
            }
            else
            {
                var sha256 = info.Length == file.Original?.Size
                    ? (await HashSharedAsync(path, file.Path, progress, cancellationToken).ConfigureAwait(false)).Sha256
                    : null;
                inspected.Add(new RuntimeInspection(file, path, RuntimeFound.File, info.Length, sha256));
            }
        }

        return inspected;
    }

    /// <summary>What a file holds now, read without locking the software out.</summary>
    /// <param name="path">The file's full path.</param>
    /// <param name="relativePath">Its game-relative path, which a refusal names.</param>
    /// <param name="progress">Receives the bytes hashed.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <exception cref="InstallBlockedException">The file changed while it was read.</exception>
    public static async Task<FileSnapshot> HashSharedAsync(string path, string relativePath, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            return await FileOperations.HashSharedAsync(path, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (FileChangedWhileReadException exception)
        {
            throw new InstallBlockedException([new FileLockedIssue(relativePath, [])], exception);
        }
    }

    /// <summary>What one installed file is now, hashed only when its size and time do not already tell.</summary>
    /// <param name="file">The file as the install's record lists it.</param>
    /// <param name="path">Its resolved full path; null when it no longer resolves safely, which counts as modified.</param>
    /// <param name="progress">Receives the hashed bytes.</param>
    /// <param name="cancellationToken">Stops the hashing.</param>
    private static async Task<FileIntegrity> InspectAsync(InstalledFile file, string? path, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        if (path is null || Directory.Exists(path))
        {
            return FileIntegrity.Modified;
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return file.Change == FileChange.Deleted ? FileIntegrity.Intact : FileIntegrity.Missing;
        }

        var installed = file.Change == FileChange.Deleted ? null : file.Installed;
        if (installed is not null && installed.Size == info.Length && installed.LastWriteTimeUtc == info.LastWriteTimeUtc)
        {
            return FileIntegrity.Intact;
        }

        var original = file.Original;
        if (installed?.Size != info.Length && original?.Size != info.Length)
        {
            return FileIntegrity.Modified;
        }

        var actual = await FileOperations.HashAsync(path, progress, cancellationToken).ConfigureAwait(false);
        return installed is not null && actual.HasSameContent(installed) ? FileIntegrity.Intact
            : original is not null && actual.HasSameContent(original) ? FileIntegrity.OriginalRestored
            : FileIntegrity.Modified;
    }
}
