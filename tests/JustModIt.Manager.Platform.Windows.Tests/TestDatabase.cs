using System.Runtime.InteropServices;

namespace JustModIt.Manager.Platform.Windows.Tests;

/// <summary>A SQLite database the test writes through the system's SQLite, as a launcher would.</summary>
internal sealed partial class TestDatabase : IDisposable
{
    private const string Sqlite = "winsqlite3";
    private const int OpenReadWrite = 0x2;
    private const int OpenCreate = 0x4;

    private readonly nint _handle;

    private TestDatabase(nint handle) => _handle = handle;

    public static TestDatabase Create(string path, string sql)
    {
        if (Open(path, out var handle, OpenReadWrite | OpenCreate, null) != 0)
        {
            _ = Close(handle);
            throw new IOException($"Could not create {path}.");
        }

        var database = new TestDatabase(handle);
        database.Execute(sql);
        return database;
    }

    public void Execute(string sql)
    {
        if (Exec(_handle, sql, 0, 0, 0) != 0)
        {
            throw new IOException($"Could not run {sql}.");
        }
    }

    public void Dispose() => _ = Close(_handle);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_open_v2", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Open(string fileName, out nint database, int flags, string? vfs);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_exec", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Exec(nint database, string sql, nint callback, nint argument, nint error);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_close_v2")]
    private static partial int Close(nint database);
}
