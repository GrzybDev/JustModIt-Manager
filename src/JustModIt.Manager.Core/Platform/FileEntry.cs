namespace JustModIt.Manager.Core.Platform;

/// <summary>A file or a directory as it was when it was read.</summary>
/// <param name="FullPath">The absolute path.</param>
/// <param name="Attributes">The attributes, of a link itself rather than its target.</param>
/// <param name="Length">The size of a file; 0 for a directory.</param>
/// <param name="LastWriteTimeUtc">When the content was last written.</param>
public readonly record struct FileEntry(string FullPath, FileAttributes Attributes, long Length, DateTimeOffset LastWriteTimeUtc)
{
    public string Name => Path.GetFileName(FullPath);

    public bool IsDirectory => (Attributes & FileAttributes.Directory) != 0;

    /// <summary>A symbolic link, a junction or another reparse point.</summary>
    public bool IsLink => (Attributes & FileAttributes.ReparsePoint) != 0;
}
