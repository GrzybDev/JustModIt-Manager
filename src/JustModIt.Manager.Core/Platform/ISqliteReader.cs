namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// Queries another program's SQLite database without ever writing to it, so the code that reads a launcher's database
/// can be tested without SQLite; a query may wait for the database's owner, so it stays off the UI thread.
/// </summary>
public interface ISqliteReader
{
    /// <summary>Runs <paramref name="sql"/> on the database at <paramref name="path"/>, opened read-only.</summary>
    /// <param name="path">The database file.</param>
    /// <param name="sql">A query that only reads.</param>
    /// <returns>The rows, each value as text and SQL NULL as null; null when the database or SQLite is missing.</returns>
    /// <exception cref="IOException">The database stayed busy, is damaged or does not have what the query reads.</exception>
    IReadOnlyList<string?[]>? Query(string path, string sql);
}
