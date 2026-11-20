using System.Text.RegularExpressions;

namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// The file systems Linux has mounted and the mount points <c>/etc/fstab</c> names, read again for every question since
/// drives come and go; it never touches the paths it is asked about, so a mount that does not answer cannot block it.
/// </summary>
internal sealed partial class MountTable
{
    private const string MountInfoFile = "/proc/self/mountinfo";
    private const string FstabFile = "/etc/fstab";
    private const string UuidLinks = "/dev/disk/by-uuid";

    // udisks mounts removable drives in these folders and removes each drive's folder when it goes.
    private static readonly string[] RemovableMedia = ["/run/media/", "/media/"];

    private readonly IReadOnlyList<string> _configured;

    private MountTable(IReadOnlyList<Mount> mounts, IReadOnlyList<string> configured)
    {
        Mounts = mounts;
        _configured = configured;
    }

    /// <summary>Every mount, in the order the system made them.</summary>
    public IReadOnlyList<Mount> Mounts { get; }

    /// <summary>Reads the system's mounts, its fstab and the UUIDs of its block devices.</summary>
    public static MountTable Read() => Parse(File.ReadAllText(MountInfoFile),
        File.Exists(FstabFile) ? File.ReadAllText(FstabFile) : string.Empty, DeviceUuids());

    /// <summary>Reads the mounts from the texts of the system's files.</summary>
    /// <param name="mountInfo">The text of <c>/proc/self/mountinfo</c>.</param>
    /// <param name="fstab">The text of <c>/etc/fstab</c>.</param>
    /// <param name="uuids">The UUID of each block device's file system, by the device's path.</param>
    public static MountTable Parse(string mountInfo, string fstab, IReadOnlyDictionary<string, string> uuids)
    {
        var mounts = new List<Mount>();
        foreach (var line in mountInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ');
            // Optional fields of any number come before the separator.
            var separator = Array.IndexOf(fields, "-", 6);
            if (separator < 0 || separator + 2 >= fields.Length)
            {
                continue;
            }

            var point = Unescape(fields[4]);
            var source = Unescape(fields[separator + 2]);
            mounts.Add(new Mount(point, fields[separator + 1], $"{uuids.GetValueOrDefault(source) ?? source}@{point}"));
        }

        var configured = new List<string>();
        foreach (var line in fstab.Split('\n'))
        {
            // Swap and other entries without a mount point write none or swap in its place.
            var fields = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (fields is [var device, ['/', ..] point, ..] && !device.StartsWith('#'))
            {
                configured.Add(TrimEnd(Unescape(point)));
            }
        }

        return new MountTable(mounts, configured);
    }

    /// <summary>The mount a path lies on: the deepest mount point holding it, the last one made on that point.</summary>
    /// <param name="path">A full, canonical path.</param>
    public Mount? MountOf(string path)
    {
        Mount? found = null;
        foreach (var mount in Mounts)
        {
            if (IsWithin(mount.Point, path) && mount.Point.Length >= (found?.Point.Length ?? 0))
            {
                found = mount;
            }
        }

        return found;
    }

    /// <summary>
    /// Whether the drive a path lies on is not mounted: a removable drive's folder or a mount point fstab names, while a
    /// plain folder is no drive.
    /// </summary>
    /// <param name="path">A full, canonical path.</param>
    /// <param name="user">The user's name, which names the folder of the user's removable drives.</param>
    public bool IsDriveMissing(string path, string user)
    {
        var root = RemovableRootOf(path, user);
        foreach (var point in _configured)
        {
            if (IsWithin(point, path) && point.Length > (root?.Length ?? 0))
            {
                root = point;
            }
        }

        return root is not null && !Mounts.Any(mount => mount.Point == root);
    }

    private static string? RemovableRootOf(string path, string user)
    {
        foreach (var media in RemovableMedia)
        {
            if (path.StartsWith(media, StringComparison.Ordinal))
            {
                var segments = path[media.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
                var depth = segments is [var first, ..] && first == user ? 2 : 1;
                return segments.Length >= depth ? media + string.Join('/', segments[..depth]) : null;
            }
        }

        return null;
    }

    private static bool IsWithin(string point, string path) =>
        point == "/" || path == point || (path.StartsWith(point, StringComparison.Ordinal) && path[point.Length] == '/');

    private static string TrimEnd(string point) => point.Length > 1 ? point.TrimEnd('/') : point;

    // Spaces, tabs, line breaks and backslashes in paths are written as octal escapes such as \040.
    private static string Unescape(string field) =>
        OctalEscape().Replace(field, match => ((char)Convert.ToInt32(match.Groups[1].Value, 8)).ToString());

    private static Dictionary<string, string> DeviceUuids()
    {
        var uuids = new Dictionary<string, string>(StringComparer.Ordinal);
        var links = new DirectoryInfo(UuidLinks);
        if (links.Exists)
        {
            foreach (var link in links.EnumerateFileSystemInfos())
            {
                if (link.LinkTarget is { } target)
                {
                    uuids[Path.GetFullPath(target, links.FullName)] = link.Name;
                }
            }
        }

        return uuids;
    }

    [GeneratedRegex(@"\\([0-7]{3})", RegexOptions.CultureInvariant)]
    private static partial Regex OctalEscape();
}

/// <summary>A mounted file system.</summary>
/// <param name="Point">The folder it is mounted on.</param>
/// <param name="Type">Its file system's type, such as <c>ext4</c> or <c>nfs4</c>.</param>
/// <param name="VolumeId">
/// Its file system's UUID (else its source) with the mount point, since a rename fails between two mounts of one file
/// system and another card at the same place is another drive.
/// </param>
internal sealed record Mount(string Point, string Type, string VolumeId)
{
    private static readonly HashSet<string> NetworkTypes =
        ["nfs", "nfs4", "cifs", "smb3", "smbfs", "fuse.sshfs", "fuse.rclone", "fuse.gvfsd-fuse"];

    /// <summary>Whether the file system is a network share rather than a local disk.</summary>
    public bool IsNetwork => NetworkTypes.Contains(Type);
}
