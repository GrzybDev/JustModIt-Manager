using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>Reads SQLite databases through the SQLite that Windows ships (<c>winsqlite3.dll</c>), so the app carries none.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsSqliteReader : ISqliteReader
{
    private const string Sqlite = "winsqlite3";
    private const int Ok = 0;
    private const int RowReady = 100;
    private const int Done = 101;
    private const int OpenReadOnly = 0x1;
    private const int NullType = 5;

    // Owners write in short transactions; a database still locked after this is read again at the next scan.
    private const int BusyTimeoutMilliseconds = 2000;

    public static WindowsSqliteReader Instance { get; } = new();

    public IReadOnlyList<string?[]>? Query(string path, string sql)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(sql);
        if (!File.Exists(path))
        {
            return null;
        }

        int opened;
        nint database;
        try
        {
            opened = Open(path, out database, OpenReadOnly, null);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        try
        {
            Check(opened);
            Check(BusyTimeout(database, BusyTimeoutMilliseconds));
            Check(Prepare(database, sql, -1, out var statement, 0));
            try
            {
                return ReadRows(statement);
            }
            finally
            {
                _ = FinalizeStatement(statement);
            }
        }
        finally
        {
            _ = CloseDatabase(database);
        }
    }

    private static List<string?[]> ReadRows(nint statement)
    {
        var rows = new List<string?[]>();
        int result;
        while ((result = Step(statement)) == RowReady)
        {
            var row = new string?[ColumnCount(statement)];
            for (var column = 0; column < row.Length; column++)
            {
                row[column] = ColumnType(statement, column) == NullType ? null : ColumnText(statement, column);
            }

            rows.Add(row);
        }

        Check(result == Done ? Ok : result);
        return rows;
    }

    // The text must be taken before its length, which SQLite counts for the encoding asked for last.
    private static string ColumnText(nint statement, int column)
    {
        var text = ColumnText16(statement, column);
        return Marshal.PtrToStringUni(text, ColumnBytes16(statement, column) / sizeof(char));
    }

    private static void Check(int result)
    {
        if (result != Ok)
        {
            throw new IOException($"SQLite failed with code {result}.");
        }
    }

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_open_v2", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int Open(string fileName, out nint database, int flags, string? vfs);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_busy_timeout")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int BusyTimeout(nint database, int milliseconds);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_prepare_v2", StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int Prepare(nint database, string sql, int length, out nint statement, nint tail);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_step")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int Step(nint statement);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_column_count")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ColumnCount(nint statement);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_column_type")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ColumnType(nint statement, int column);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_column_text16")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint ColumnText16(nint statement, int column);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_column_bytes16")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ColumnBytes16(nint statement, int column);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_finalize")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int FinalizeStatement(nint statement);

    [LibraryImport(Sqlite, EntryPoint = "sqlite3_close_v2")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CloseDatabase(nint database);
}
