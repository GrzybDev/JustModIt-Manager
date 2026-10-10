using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Interop;

[SupportedOSPlatform("windows")]
internal static partial class WindowsVolumeApi
{
    private const int MaxPath = 32 * 1024;
    private const int VolumeNameLength = 64;

    /// <summary>
    /// Gives the folder the volume holding <paramref name="path"/> is mounted at, e.g. <c>D:\</c> or a mount point
    /// folder; null when the system cannot tell.
    /// </summary>
    /// <param name="path">An existing path on the volume.</param>
    public static string? TryGetMountPoint(string path)
    {
        var buffer = new char[MaxPath];
        return GetVolumePathName(LongPath.ToExtended(path), buffer, (uint)buffer.Length) ? UpToTerminator(buffer) : null;
    }

    /// <summary>Gives the <c>\\?\Volume{GUID}\</c> name of a local volume; null for network shares.</summary>
    /// <param name="mountPoint">The folder the volume is mounted at, with a trailing separator.</param>
    public static string? TryGetVolumeName(string mountPoint)
    {
        var buffer = new char[VolumeNameLength];
        return GetVolumeNameForVolumeMountPoint(mountPoint, buffer, (uint)buffer.Length) ? UpToTerminator(buffer) : null;
    }

    /// <summary>Gives the bytes the user may still write on the volume holding the folder; null when unknown.</summary>
    /// <param name="directory">An existing folder on the volume.</param>
    public static long? TryGetAvailableBytes(string directory) =>
        GetDiskFreeSpaceEx(LongPath.ToExtended(directory), out var available, out _, out _) ? (long)available : null;

    private static string UpToTerminator(ReadOnlySpan<char> buffer) =>
        new(buffer.IndexOf('\0') is var end and >= 0 ? buffer[..end] : buffer);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumePathName(string fileName, Span<char> volumePathName, uint bufferLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPoint(string mountPoint, Span<char> volumeName, uint bufferLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceEx(string directoryName, out ulong freeBytesAvailable, out ulong totalBytes,
        out ulong totalFreeBytes);
}
