using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;

namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// The registry and the launchers' SQLite databases on a system that has neither, where the stores keep their records in
/// files: every key and database reads as missing, and no key can be watched.
/// </summary>
public sealed class NoWindowsRecords : IRegistry, IRegistryWatcher, ISqliteReader
{
    private NoWindowsRecords()
    {
    }

    public static NoWindowsRecords Instance { get; } = new();

    public RegistryKeySnapshot? ReadKey(RegistryHive hive, RegistryView view, string path) => null;

    public IReadOnlyList<string?[]>? Query(string path, string sql) => null;

    public IDisposable Watch(WatchedRegistryKey key, Action<WatchSignal> onSignal) =>
        throw new PlatformNotSupportedException("This system has no registry to watch.");
}
