using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.IO;
using Microsoft.Win32.SafeHandles;

namespace JustModIt.Manager.Core.Interop;

[SupportedOSPlatform("windows")]
internal static partial class WindowsFileApi
{
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileNameNormalizedVolumeNameDos = 0x0;
    private const int InitialBufferLength = 512;

    /// <summary>
    /// Gives the path with junctions, symbolic links, SUBST drives and 8.3 names resolved, in on-disk casing; null when
    /// the path does not exist or cannot be opened.
    /// </summary>
    /// <param name="path">A full path to a file or folder.</param>
    public static unsafe string? TryGetFinalPath(string path)
    {
        // Backup semantics are required to open a directory handle.
        using var handle = CreateFile(LongPath.ToExtended(path), FileReadAttributes, FileShareAll, 0, OpenExisting,
            FileFlagBackupSemantics, 0);
        if (handle.IsInvalid)
        {
            return null;
        }

        return ReadGrowing(buffer =>
        {
            fixed (char* chars = buffer)
            {
                return GetFinalPathNameByHandle(handle, chars, (uint)buffer.Length, FileNameNormalizedVolumeNameDos);
            }
        }) is { } final
            ? LongPath.StripPrefix(final)
            : null;
    }

    /// <summary>
    /// Gives the 8.3 form of an existing path, the path itself when the volume keeps no short names, or null when it does
    /// not exist.
    /// </summary>
    /// <param name="path">A full path to a file or folder.</param>
    public static unsafe string? TryGetShortPath(string path)
    {
        var shortPath = ReadGrowing(buffer =>
        {
            fixed (char* chars = buffer)
            {
                return GetShortPathName(LongPath.ToExtended(path), chars, (uint)buffer.Length);
            }
        });
        return shortPath is null ? null : LongPath.StripPrefix(shortPath);
    }

    // Both APIs return the required length including the terminator when the buffer is too small, and 0 on failure.
    private static string? ReadGrowing(Func<char[], uint> read)
    {
        var buffer = new char[InitialBufferLength];
        while (true)
        {
            var length = read(buffer);
            if (length == 0)
            {
                return null;
            }

            if (length < buffer.Length)
            {
                return new string(buffer, 0, (int)length);
            }

            buffer = new char[length];
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle file, char* path, uint pathLength, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetShortPathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint GetShortPathName(string longPath, char* shortPath, uint shortPathLength);
}
