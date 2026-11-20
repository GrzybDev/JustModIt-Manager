using JustModIt.Manager.Core.Interop;

namespace JustModIt.Manager.Core.IO;

/// <summary>Makes renames durable on Linux, where a file renamed into a folder survives a power cut only once the folder is flushed.</summary>
internal static class FileDurability
{
    /// <summary>Flushes the entries of folders to the disk on Linux; nothing on other systems.</summary>
    /// <param name="folders">The folders, each flushed once; one that cannot be opened, such as one that is gone, is skipped.</param>
    /// <exception cref="IOException">The disk reported an error.</exception>
    public static void FlushFolders(IEnumerable<string> folders)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        foreach (var folder in folders.Distinct(PathComparison.Comparer))
        {
            UnixFileApi.FlushFolder(folder);
        }
    }
}
