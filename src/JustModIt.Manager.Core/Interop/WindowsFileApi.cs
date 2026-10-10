using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.IO;
using Microsoft.Win32.SafeHandles;

namespace JustModIt.Manager.Core.Interop;

[SupportedOSPlatform("windows")]
internal static partial class WindowsFileApi
{
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint FileShareDelete = 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileNameNormalized = 0x0;
    private const uint VolumeNameDos = 0x0;
    private const int InitialBufferLength = 512;

    /// <summary>
    /// Gives the path with junctions, symbolic links, SUBST drives and 8.3 names resolved, in on-disk casing; null when
    /// the path does not exist or cannot be opened.
    /// </summary>
    /// <param name="path">A full path to a file or folder.</param>
    public static string? TryGetFinalPath(string path)
    {
        // Backup semantics are required to open a directory handle.
        using var handle = CreateFile(LongPath.ToExtended(path), FileReadAttributes, FileShareRead | FileShareWrite | FileShareDelete, 0,
            OpenExisting, FileFlagBackupSemantics, 0);
        if (handle.IsInvalid)
        {
            return null;
        }

        return ReadGrowing(buffer => GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, FileNameNormalized | VolumeNameDos))
            is { } final
            ? LongPath.StripPrefix(final)
            : null;
    }

    /// <summary>
    /// Gives the 8.3 form of an existing path, the path itself when the volume keeps no short names, or null when it does
    /// not exist.
    /// </summary>
    /// <param name="path">A full path to a file or folder.</param>
    public static string? TryGetShortPath(string path)
    {
        var shortPath = ReadGrowing(buffer => GetShortPathName(LongPath.ToExtended(path), buffer, (uint)buffer.Length));
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

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle file, Span<char> path, uint pathLength, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetShortPathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetShortPathName(string longPath, Span<char> shortPath, uint shortPathLength);
}
