using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Safety;

public enum FileLockState
{
    Free,
    Missing,

    /// <summary>Another program holds the file open, so it cannot be replaced now.</summary>
    InUse,

    /// <summary>The read-only attribute is set, which an install may clear before writing.</summary>
    ReadOnly,

    /// <summary>Permissions forbid writing, e.g. a game under Program Files without elevation; closing programs does not help.</summary>
    AccessDenied,
}

/// <summary>What the lock probe found for the files an operation changes.</summary>
/// <param name="InUse">Files other programs hold open.</param>
/// <param name="AccessDenied">Files the user may not write.</param>
/// <param name="Holders">The executables holding the files in use, when the system names them.</param>
public sealed record FileLockReport(ValueList<string> InUse, ValueList<string> AccessDenied, ValueList<string> Holders);

/// <summary>Names the programs that hold files open.</summary>
public interface ILockHolderSource
{
    /// <summary>Names the executables holding any of <paramref name="paths"/>; empty when the system cannot tell.</summary>
    /// <param name="paths">Files found in use.</param>
    IReadOnlyList<string> FindHolders(IReadOnlyList<string> paths);
}

/// <summary>Finds game files that cannot be replaced right now, by opening them exclusively for writing.</summary>
/// <param name="holders">Names the programs holding files in use; null to leave them unnamed.</param>
public sealed class FileLockProbe(ILockHolderSource? holders)
{
    // Enough files to name the programs holding them; a running game may hold hundreds.
    private const int MaxFilesAskedForHolders = 100;

    /// <summary>Tells which of <paramref name="paths"/> cannot be written right now, and why.</summary>
    /// <param name="paths">The files to check; missing ones are left out of the report.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    public FileLockReport Probe(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var inUse = new List<string>();
        var accessDenied = new List<string>();
        foreach (var path in paths.Distinct(PathComparison.Comparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (ProbeFile(path))
            {
                case FileLockState.InUse:
                    inUse.Add(path);
                    break;
                case FileLockState.AccessDenied:
                    accessDenied.Add(path);
                    break;
            }
        }

        var holderNames = inUse.Count > 0 && holders is not null
            ? holders.FindHolders([.. inUse.Take(MaxFilesAskedForHolders)])
            : [];
        return new FileLockReport([.. inUse], [.. accessDenied], [.. holderNames.Distinct(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>Tells whether the file can be replaced right now, and why not.</summary>
    /// <param name="path">The file to check.</param>
    public static FileLockState ProbeFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
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
        catch (UnauthorizedAccessException)
        {
            return ProbeUnwritable(path);
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
