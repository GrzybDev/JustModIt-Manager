using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>Databases in memory, each answering its one query with fixed rows; a database without rows is missing.</summary>
internal sealed class InMemorySqlite : ISqliteReader
{
    private readonly Dictionary<string, IReadOnlyList<string?[]>> _rows = new(PathComparison.Comparer);
    private readonly HashSet<string> _busy = new(PathComparison.Comparer);

    public InMemorySqlite Add(string path, params string?[][] rows)
    {
        _rows[path] = rows;
        return this;
    }

    public InMemorySqlite Lock(string path)
    {
        _busy.Add(path);
        return this;
    }

    public IReadOnlyList<string?[]>? Query(string path, string sql) =>
        _busy.Contains(path) ? throw new IOException("The database is locked.") : _rows.GetValueOrDefault(path);
}
