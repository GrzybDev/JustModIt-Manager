using System.Security.Cryptography;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Installation;

/// <summary>
/// The file changes the engine makes, always writing new content to a file of its own and renaming it into place, since
/// writing in place would write through hard links and leave a half-written file behind after a crash.
/// </summary>
internal static class FileOperations
{
    public const FileAttributes PreservedAttributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;

    private const int BufferSize = 1 << 20;

    /// <summary>Writes a new file, hashing exactly the bytes written, and makes it durable before anything relies on it.</summary>
    /// <param name="content">The bytes to write, read to its end.</param>
    /// <param name="path">The new file; nothing may exist there yet.</param>
    /// <param name="progress">Receives the bytes written.</param>
    /// <param name="cancellationToken">Stops the write and removes the file.</param>
    public static async Task<FileSnapshot> WriteNewFileAsync(Stream content, string path, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0,
            FileOptions.Asynchronous);
        try
        {
            await using (target.ConfigureAwait(false))
            {
                var (size, sha256) = await FileHash.CopyAndComputeAsync(content, target, HashAlgorithmName.SHA256, progress,
                    cancellationToken).ConfigureAwait(false);
                target.Flush(flushToDisk: true);
                return new FileSnapshot(size, sha256);
            }
        }
        catch
        {
            TryDeleteFile(path);
            throw;
        }
    }

    /// <summary>Copies a file to a new one as <see cref="WriteNewFileAsync"/> writes it.</summary>
    /// <param name="source">The file to copy.</param>
    /// <param name="destination">The new file; nothing may exist there yet.</param>
    /// <param name="progress">Receives the bytes written.</param>
    /// <param name="cancellationToken">Stops the copy and removes the new file.</param>
    public static async Task<FileSnapshot> CopyNewFileAsync(string source, string destination, IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var stream = OpenRead(source);
        await using (stream.ConfigureAwait(false))
        {
            return await WriteNewFileAsync(stream, destination, progress, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The size and hash of a file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="progress">Receives the bytes hashed.</param>
    /// <param name="cancellationToken">Stops the hashing.</param>
    public static async Task<FileSnapshot> HashAsync(string path, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var stream = OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            var sha256 = await FileHash.ComputeAsync(stream, HashAlgorithmName.SHA256, progress, cancellationToken)
                .ConfigureAwait(false);
            return new FileSnapshot(stream.Length, sha256);
        }
    }

    /// <summary>Opens a file for reading with writers locked out, so the content cannot change while it is read.</summary>
    /// <param name="path">The file.</param>
    public static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0, FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <summary>Renames a file over another in one step; a read-only target would refuse it.</summary>
    /// <param name="source">The file to rename.</param>
    /// <param name="target">The file it replaces.</param>
    public static void MoveOver(string source, string target)
    {
        ClearReadOnly(target);
        File.Move(source, target, overwrite: true);
    }

    /// <summary>Deletes a file, read-only or not, when it exists.</summary>
    /// <param name="path">The file.</param>
    public static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            ClearReadOnly(path);
            File.Delete(path);
        }
    }

    /// <summary>Deletes a file when it can, ignoring any failure.</summary>
    /// <param name="path">The file.</param>
    public static void TryDeleteFile(string path)
    {
        try
        {
            DeleteFile(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Deletes a folder, but leaves it alone when anything is in it.</summary>
    /// <param name="path">The folder.</param>
    public static void DeleteDirectoryIfEmpty(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Removes <paramref name="directory"/> and then each parent up to <paramref name="top"/>, while they are empty.</summary>
    /// <param name="directory">The deepest folder.</param>
    /// <param name="top">The highest folder that may go.</param>
    public static void DeleteEmptyDirectories(string directory, string top)
    {
        for (string? current = directory;
             current is not null && PathComparison.IsWithin(top, current);
             current = Path.GetDirectoryName(current))
        {
            DeleteDirectoryIfEmpty(current);
            if (Directory.Exists(current))
            {
                return;
            }
        }
    }

    /// <summary>Removes files the app wrote into its own folder, such as staging files, and the folder itself.</summary>
    /// <param name="path">A folder only the app writes to.</param>
    public static void DeleteOwnDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            DeleteFile(file);
        }

        Directory.Delete(path, recursive: true);
    }

    /// <summary>Creates a hidden folder, so the app's files at a game root stay out of the player's way.</summary>
    /// <param name="path">The folder.</param>
    public static void CreateHiddenDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);
        if ((directory.Attributes & FileAttributes.Hidden) == 0)
        {
            directory.Attributes |= FileAttributes.Hidden;
        }
    }

    /// <summary>The <see cref="PreservedAttributes"/> a file has; null when it has none of them.</summary>
    /// <param name="path">The file, usually an original about to be backed up.</param>
    public static FileAttributes? PreservedAttributesOf(string path)
    {
        var attributes = File.GetAttributes(path) & PreservedAttributes;
        return attributes == 0 ? null : attributes;
    }

    /// <summary>Gives a file back the attributes it had.</summary>
    /// <param name="path">The file.</param>
    /// <param name="attributes">The attributes to add; null for none.</param>
    public static void RestoreAttributes(string path, FileAttributes? attributes)
    {
        if (attributes is { } restored)
        {
            File.SetAttributes(path, File.GetAttributes(path) | restored);
        }
    }

    /// <summary>Lets a read-only file be replaced or deleted.</summary>
    /// <param name="path">The file, which need not exist.</param>
    public static void ClearReadOnly(string path)
    {
        var file = new FileInfo(path);
        if (file.Exists && file.IsReadOnly)
        {
            file.IsReadOnly = false;
        }
    }
}
