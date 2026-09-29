using JustModIt.Manager.Core.Collections;

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

internal static class FileInspector
{
    /// <summary>Tells what each of an install's files is now.</summary>
    /// <param name="files">The install's files.</param>
    /// <param name="game">The game folder they are in.</param>
    /// <param name="progress">Counts files and hashed bytes.</param>
    /// <param name="cancellationToken">Stops the inspection.</param>
    public static async Task<List<InspectedFile>> InspectAllAsync(InstalledFiles files, GameFolder game, ProgressTracker progress,
        CancellationToken cancellationToken)
    {
        var inspected = new List<InspectedFile>(files.Files.Count);
        foreach (var file in files.Files)
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
        files.Files.Sum(file => Math.Max(file.Installed?.Size ?? 0, file.Original?.Size ?? 0));

    /// <summary>What one installed file is now, hashed only when its size and time do not already tell.</summary>
    /// <param name="file">The file as the install's record lists it.</param>
    /// <param name="path">Its resolved full path; null when it no longer resolves safely, which counts as modified.</param>
    /// <param name="progress">Receives the hashed bytes.</param>
    /// <param name="cancellationToken">Stops the hashing.</param>
    public static async Task<FileIntegrity> InspectAsync(InstalledFile file, string? path, IProgress<long>? progress,
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
