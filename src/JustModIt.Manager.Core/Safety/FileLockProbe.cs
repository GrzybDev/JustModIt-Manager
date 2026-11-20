using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Safety;

public enum FileLockState
{
    Free,
    Missing,

    /// <summary>Another program holds the file open, so it cannot be replaced now.</summary>
    InUse,

    /// <summary>The read-only attribute is set, which an install may clear before writing; only Windows tells it.</summary>
    ReadOnly,

    /// <summary>
    /// Permissions forbid changing the file on Windows, e.g. a game under Program Files without elevation, or reading it
    /// elsewhere; closing programs does not help.
    /// </summary>
    AccessDenied,
}

/// <summary>What the lock probe found for the files an operation changes.</summary>
/// <param name="InUse">Files other programs hold open.</param>
/// <param name="AccessDenied">Files the user may not change on Windows, or read elsewhere.</param>
/// <param name="Holders">The executables holding the files in use, when the system names them.</param>
public sealed record FileLockReport(ValueList<string> InUse, ValueList<string> AccessDenied, ValueList<string> Holders);

/// <summary>A program holding a file open.</summary>
/// <param name="Program">The file name of its executable, which players recognise.</param>
/// <param name="Path">The file it holds, of those asked about; null where the system names only the holders of them all.</param>
public sealed record FileHolder(string Program, string? Path);

/// <summary>Names the programs that hold files open.</summary>
public interface ILockHolderSource
{
    /// <summary>Names the programs holding any of <paramref name="paths"/>; empty when the system cannot tell.</summary>
    /// <param name="paths">The files to ask about.</param>
    IReadOnlyList<FileHolder> FindHolders(IReadOnlyList<string> paths);
}

/// <summary>
/// Finds game files that cannot be replaced right now: on Windows by opening them exclusively for writing, elsewhere by the
/// programs the system names as holding them, since no other system locks the files programs hold.
/// </summary>
/// <param name="holders">Names the programs holding files, which outside Windows is the only way to tell a file in use.</param>
public sealed class FileLockProbe(ILockHolderSource holders)
{
    // Enough files to name the programs holding them; a running game may hold hundreds.
    private const int MaxFilesAskedForHolders = 100;

    /// <summary>Tells which of <paramref name="paths"/> cannot be written right now, and why.</summary>
    /// <param name="paths">The files to check; missing ones are left out of the report.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    public FileLockReport Probe(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var files = paths.Distinct(PathComparison.Comparer).ToList();
        IReadOnlyList<FileHolder> held = OperatingSystem.IsWindows() || files.Count == 0 ? [] : holders.FindHolders(files);
        var heldFiles = held.Select(holder => holder.Path).OfType<string>().ToHashSet(PathComparison.Comparer);
        var inUse = new List<string>();
        var accessDenied = new List<string>();
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (heldFiles.Contains(path) ? FileLockState.InUse : ProbeFile(path))
            {
                case FileLockState.InUse:
                    inUse.Add(path);
                    break;
                case FileLockState.AccessDenied:
                    accessDenied.Add(path);
                    break;
            }
        }

        if (OperatingSystem.IsWindows() && inUse.Count > 0)
        {
            held = holders.FindHolders([.. inUse.Take(MaxFilesAskedForHolders)]);
        }

        return new FileLockReport([.. inUse], [.. accessDenied],
            [.. held.Select(holder => holder.Program).Distinct(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// Tells whether the file can be replaced right now, and why not, as far as opening it tells; outside Windows that is
    /// only whether the user may read it and whether another .NET program holds it exclusively.
    /// </summary>
    /// <param name="path">The file to check.</param>
    public static FileLockState ProbeFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return OperatingSystem.IsWindows() ? ProbeForWriting(path) : ProbeForReading(path);
    }

    private static FileLockState ProbeForWriting(string path)
    {
        try
        {
            // A running executable or library, or a file the game keeps open, refuses an exclusive write open.
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return FileLockState.Free;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return FileLockState.Missing;
        }
        catch (IOException exception) when (IOErrors.IsSharingViolation(exception))
        {
            return FileLockState.InUse;
        }
        catch (IOException exception) when (IOErrors.IsAccessDenied(exception))
        {
            return FileLockState.AccessDenied;
        }
        catch (UnauthorizedAccessException)
        {
            return ProbeUnwritable(path);
        }
    }

    // Replacing or deleting a file needs only its folder's permission, which is probed apart; the file itself is read, to be
    // hashed or copied.
    private static FileLockState ProbeForReading(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, SharedFileRead.Sharing);
            return FileLockState.Free;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return FileLockState.Missing;
        }
        catch (IOException exception) when (IOErrors.IsSharingViolation(exception))
        {
            return FileLockState.InUse;
        }
        catch (UnauthorizedAccessException)
        {
            return File.Exists(path) ? FileLockState.AccessDenied : FileLockState.Missing;
        }
    }

    private static FileLockState ProbeUnwritable(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            return FileLockState.Missing;
        }

        if (!file.IsReadOnly)
        {
            return FileLockState.AccessDenied;
        }

        try
        {
            // Without write access, only an exclusive read open can still reveal another program holding the file.
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return FileLockState.ReadOnly;
        }
        catch (IOException exception) when (IOErrors.IsSharingViolation(exception))
        {
            return FileLockState.InUse;
        }
        catch (UnauthorizedAccessException)
        {
            return FileLockState.AccessDenied;
        }
    }
}
