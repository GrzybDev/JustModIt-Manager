using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Library;

public enum WatchSignal
{
    Changed,

    /// <summary>Changes may have been missed: the watcher overflowed, failed or lost what it watched.</summary>
    Lost,
}

/// <summary>The folders and registry keys whose changes mean a store's records changed.</summary>
/// <param name="Folders">Folders whose direct entries matter.</param>
/// <param name="RegistryKeys">Registry keys whose values matter.</param>
public sealed record WatchPlan(ValueList<WatchedFolder> Folders, ValueList<WatchedRegistryKey> RegistryKeys)
{
    public static WatchPlan Empty { get; } = new([], []);

    /// <summary>A plan of folders only, each once.</summary>
    /// <param name="folders">The folders; null ones are left out.</param>
    public static WatchPlan ForFolders(params IEnumerable<WatchedFolder?> folders) =>
        new([.. folders.OfType<WatchedFolder>().Distinct()], []);

    /// <summary>Combines this plan with <paramref name="other"/>, keeping each folder and key once.</summary>
    /// <param name="other">The plan of another source of the store.</param>
    public WatchPlan Merge(WatchPlan other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new WatchPlan([.. Folders.Concat(other.Folders).Distinct()], [.. RegistryKeys.Concat(other.RegistryKeys).Distinct()]);
    }
}

/// <summary>
/// The direct entries of a folder that matter; watches never recurse, since store folders hold whole game installs,
/// whose file churn says nothing about the library.
/// </summary>
public sealed class WatchedFolder : IEquatable<WatchedFolder>
{
    /// <summary>Creates a watch of the entries of <paramref name="directory"/> that match <paramref name="filters"/>.</summary>
    /// <param name="directory">The folder; it is stored normalized.</param>
    /// <param name="filters">Names or wildcard patterns of the entries that matter; none means every entry.</param>
    public WatchedFolder(string directory, IEnumerable<string> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        Directory = PathComparison.Normalize(directory);
        Filters = [.. filters.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
    }

    public string Directory { get; }

    public ValueList<string> Filters { get; }

    /// <summary>
    /// Watches the folder holding <paramref name="path"/> for its name or, while that folder is missing, its nearest
    /// existing ancestor for the next folder on the way, so the next scan picks up each step down.
    /// </summary>
    /// <param name="fileSystem">Tells which folders exist.</param>
    /// <param name="path">The file or folder to wait for.</param>
    /// <returns>Null when not even the drive exists.</returns>
    public static WatchedFolder? Toward(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        var full = PathComparison.Normalize(path);
        var name = Path.GetFileName(full);
        var directory = Path.GetDirectoryName(full);
        while (directory is not null && !fileSystem.DirectoryExists(directory))
        {
            name = Path.GetFileName(directory);
            directory = Path.GetDirectoryName(directory);
        }

        return directory is null || name.Length == 0 ? null : new WatchedFolder(directory, [name]);
    }

    public bool Equals(WatchedFolder? other) =>
        other is not null
        && string.Equals(Directory, other.Directory, PathComparison.Comparison)
        && Filters.Count == other.Filters.Count
        && Filters.Zip(other.Filters).All(pair => pair.First.Equals(pair.Second, StringComparison.OrdinalIgnoreCase));

    public override bool Equals(object? obj) => obj is WatchedFolder other && Equals(other);

    public override int GetHashCode() => PathComparison.Comparer.GetHashCode(Directory);

    public override string ToString() => $"{Directory} [{string.Join(", ", Filters)}]";
}

/// <summary>
/// A registry key to watch, waited for on its nearest existing ancestor while it does not exist, so a store installed
/// later is noticed.
/// </summary>
/// <param name="Hive">The hive the key is in.</param>
/// <param name="View">The 32-bit or 64-bit view of the registry to watch.</param>
/// <param name="Path">The key's path within the hive.</param>
/// <param name="IncludeSubkeys">Changes anywhere below the key count too, not only its own values.</param>
public sealed record WatchedRegistryKey(RegistryHive Hive, RegistryView View, string Path, bool IncludeSubkeys);
