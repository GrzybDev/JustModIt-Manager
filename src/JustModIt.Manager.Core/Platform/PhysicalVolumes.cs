using System.Runtime.Versioning;
using JustModIt.Manager.Core.Interop;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Platform;

public sealed class PhysicalVolumes : IVolumes
{
    public static PhysicalVolumes Instance { get; } = new();

    public string? GetVolumeId(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return MountOf(path)?.VolumeId;
        }

        if (WindowsMountPointOf(path) is not { } mountPoint)
        {
            return null;
        }

        // Network shares have no volume GUID; their root names them well enough.
        return WindowsVolumeApi.TryGetVolumeName(mountPoint) ?? mountPoint.ToUpperInvariant();
    }

    public bool IsAttached(string volumeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeId);
        if (!OperatingSystem.IsWindows())
        {
            return MountTable.Read().Mounts.Any(mount => mount.VolumeId == volumeId);
        }

        try
        {
            return Directory.Exists(volumeId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public long? GetAvailableBytes(string path)
    {
        if (ExistingAncestor(path) is not { } directory)
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            return WindowsVolumeApi.TryGetAvailableBytes(directory);
        }

        // DriveInfo asks statvfs, which answers for any folder on the mount.
        try
        {
            return new DriveInfo(directory).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool IsNetworkPath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return MountOf(path)?.IsNetwork == true;
        }

        var full = PathComparison.Normalize(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        if (WindowsMountPointOf(full) is not { } mountPoint)
        {
            return false;
        }

        // A share is mapped to a drive letter, never mounted in a folder, so its drive tells.
        try
        {
            return new DriveInfo(mountPoint).DriveType == DriveType.Network;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static Mount? MountOf(string path) =>
        ExistingAncestor(path) is { } existing ? MountTable.Read().MountOf(LinkResolver.Resolve(existing)) : null;

    [SupportedOSPlatform("windows")]
    private static string? WindowsMountPointOf(string path) =>
        ExistingAncestor(path) is { } existing && WindowsVolumeApi.TryGetMountPoint(existing) is { } mountPoint
            ? LongPath.StripPrefix(mountPoint)
            : null;

    // A missing tail says nothing about the volume, and a missing root means the drive is not there.
    private static string? ExistingAncestor(string path)
    {
        for (string? current = PathComparison.Normalize(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }
        }

        return null;
    }
}
