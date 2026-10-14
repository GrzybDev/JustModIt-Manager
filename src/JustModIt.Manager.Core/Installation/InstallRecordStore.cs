using System.Text.Encodings.Web;
using System.Text.Json;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Installation;

/// <summary>One folder per install in the app's data: the record, its file list and the journal of an unfinished operation.</summary>
/// <param name="Id">The install's or the operation's record id.</param>
/// <param name="Root">The folder.</param>
internal sealed record InstallFolder(Guid Id, string Root)
{
    public string RecordFile => Path.Join(Root, "record.json");

    public string FilesFile => Path.Join(Root, "files.json");

    public string JournalFile => Path.Join(Root, "journal.jsonl");

    public bool HasRecord => File.Exists(RecordFile);

    public bool HasJournal => File.Exists(JournalFile);
}

/// <summary>An install's record as it was read.</summary>
/// <param name="Folder">The install's folder.</param>
/// <param name="Record">Null when there is none or it cannot be read.</param>
/// <param name="IsUnreadable">A record exists but is damaged or was written by a newer app.</param>
/// <param name="InstallDirectory">The game folder an unreadable record still names; null when it names none.</param>
internal readonly record struct RecordLoad(InstallFolder Folder, InstallRecord? Record, bool IsUnreadable,
    string? InstallDirectory = null);

/// <summary>Reads and writes the folders of installs.</summary>
/// <param name="installsDirectory">The folder that holds one folder per install.</param>
/// <param name="time">Times the retries of atomic writes.</param>
internal sealed class InstallRecordStore(string installsDirectory, TimeProvider time)
{
    private const string FolderFormat = "N";
    private const long MaxFileLength = 1L << 30;

    // How InstallationJsonContext names InstallRecord.InstallDirectory.
    private const string InstallDirectoryProperty = "installDirectory";

    // People read these files, so letters stay letters instead of escapes.
    private static readonly JsonWriterOptions RecordWriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonWriterOptions FilesWriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The folder of an install, which may not exist yet.</summary>
    /// <param name="id">The install's record id.</param>
    public InstallFolder FolderOf(Guid id) => new(id, Path.Join(installsDirectory, id.ToString(FolderFormat)));

    /// <summary>The folders of every install on disk, leaving out folders not named after a record id.</summary>
    public IReadOnlyList<InstallFolder> Folders() =>
        Directory.Exists(installsDirectory)
            ? [.. Directory.EnumerateDirectories(installsDirectory)
                .Select(directory => Guid.TryParseExact(Path.GetFileName(directory), FolderFormat, out var id) ? FolderOf(id) : null)
                .OfType<InstallFolder>()]
            : [];

    /// <summary>Reads the record of every folder that has one.</summary>
    /// <param name="cancellationToken">Stops the reads.</param>
    public async Task<IReadOnlyList<RecordLoad>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var records = new List<RecordLoad>();
        foreach (var folder in Folders())
        {
            if (folder.HasRecord)
            {
                records.Add(await LoadAsync(folder, cancellationToken).ConfigureAwait(false));
            }
        }

        return records;
    }

    /// <summary>Reads an install's record.</summary>
    /// <param name="folder">The install's folder.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public static async Task<RecordLoad> LoadAsync(InstallFolder folder, CancellationToken cancellationToken)
    {
        byte[] content;
        try
        {
            content = await ReadAsync(folder.RecordFile, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return new RecordLoad(folder, null, IsUnreadable: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new RecordLoad(folder, null, IsUnreadable: true);
        }

        try
        {
            if (JsonSerializer.Deserialize(content, InstallationJsonContext.Default.InstallRecord) is { } record
                && record.Id == folder.Id && record.FormatVersion <= InstallRecord.CurrentFormatVersion)
            {
                return new RecordLoad(folder, record, IsUnreadable: false);
            }
        }
        catch (JsonException)
        {
        }

        return new RecordLoad(folder, null, IsUnreadable: true, InstallDirectoryOf(content));
    }

    /// <summary>
    /// Whether an install other than <paramref name="except"/>, or an unfinished operation, is on this very game folder,
    /// counting one that cannot be read, since it may be; one on a game inside it or around it has a folder of its own.
    /// </summary>
    /// <param name="gameDirectory">The game's folder.</param>
    /// <param name="except">The install or operation that does not count.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    public async Task<bool> HasOtherOnAsync(string gameDirectory, Guid except, CancellationToken cancellationToken)
    {
        foreach (var folder in Folders().Where(folder => folder.Id != except))
        {
            if (IsPendingOn(folder, gameDirectory, PathComparison.AreSame))
            {
                return true;
            }

            if (folder.HasRecord && await LoadAsync(folder, cancellationToken).ConfigureAwait(false) is var load
                && (load.IsUnreadable || (load.Record is { } record && PathComparison.AreSame(record.InstallDirectory, gameDirectory))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether an interrupted operation on the game folder, or on a game inside it or around it, waits for recovery,
    /// counting a journal that cannot be read at all, since it may be about this game.
    /// </summary>
    /// <param name="folder">The folder of the install or operation.</param>
    /// <param name="gameDirectory">The game's folder.</param>
    public static bool IsPendingOn(InstallFolder folder, string gameDirectory) =>
        IsPendingOn(folder, gameDirectory, PathComparison.Overlaps);

    private static bool IsPendingOn(InstallFolder folder, string gameDirectory, Func<string, string, bool> matches)
    {
        try
        {
            return folder.HasJournal && TransactionJournal.ReadHeader(folder.JournalFile) is { } header
                && matches(header.GameDirectory, gameDirectory);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return true;
        }
    }

    /// <summary>Reads the files an install touched.</summary>
    /// <param name="folder">The install's folder.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <exception cref="InvalidDataException">The file list is missing, damaged or was written by a newer app.</exception>
    public static async Task<InstalledFiles> LoadFilesAsync(InstallFolder folder, CancellationToken cancellationToken)
    {
        try
        {
            var files = JsonSerializer.Deserialize(await ReadAsync(folder.FilesFile, cancellationToken).ConfigureAwait(false),
                InstallationJsonContext.Default.InstalledFiles);
            return files is not null && files.FormatVersion <= InstalledFiles.CurrentFormatVersion
                ? files
                : throw new InvalidDataException($"The file list of install {folder.Id} is not one this version can read.");
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            throw new InvalidDataException($"The file list of install {folder.Id} cannot be read.", exception);
        }
    }

    /// <summary>
    /// Writes the file list first and the record last, since the record is the commit point of an install, each in the
    /// oldest format that holds it, so older versions still undo what they can read.
    /// </summary>
    /// <param name="record">The install's record.</param>
    /// <param name="files">The files the install touched.</param>
    /// <param name="cancellationToken">Stops the writes.</param>
    public async Task SaveAsync(InstallRecord record, InstalledFiles files, CancellationToken cancellationToken)
    {
        var folder = FolderOf(record.Id);
        var list = files with { FormatVersion = files.RequiredFormatVersion };
        await AtomicFile.WriteJsonAsync(folder.FilesFile, FilesWriterOptions,
            writer => JsonSerializer.Serialize(writer, list, InstallationJsonContext.Default.InstalledFiles), cancellationToken, time)
            .ConfigureAwait(false);
        await SaveRecordAsync(record, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces an install's record in one step.</summary>
    /// <param name="record">The install's record.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    public Task SaveRecordAsync(InstallRecord record, CancellationToken cancellationToken) =>
        AtomicFile.WriteJsonAsync(FolderOf(record.Id).RecordFile, RecordWriterOptions,
            writer => JsonSerializer.Serialize(writer, record, InstallationJsonContext.Default.InstallRecord), cancellationToken, time);

    /// <summary>Removes the files the app keeps for an install, and the folder when nothing else is in it.</summary>
    /// <param name="folder">The install's folder.</param>
    public static void DeleteFolder(InstallFolder folder)
    {
        FileOperations.DeleteFile(folder.RecordFile);
        FileOperations.DeleteFile(folder.FilesFile);
        FileOperations.DeleteFile(folder.JournalFile);
        FileOperations.DeleteDirectoryIfEmpty(folder.Root);
    }

    private static Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken) =>
        SharedFileRead.ReadAllBytesAsync(path, MaxFileLength, cancellationToken);

    // A record this version cannot read still names its game folder, unless it is damaged beyond that.
    private static string? InstallDirectoryOf(byte[] content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.GetFullPathOrNull(InstallDirectoryProperty)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
