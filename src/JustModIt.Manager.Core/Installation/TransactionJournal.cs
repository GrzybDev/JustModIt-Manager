using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustModIt.Manager.Core.Installation;

internal enum TransactionKind
{
    Install,
    MoveBackups,
}

/// <summary>A write-ahead journal line. Paths are relative to the game folder its header names.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TransactionStarted), "started")]
[JsonDerivedType(typeof(DirectoryCreated), "directory")]
[JsonDerivedType(typeof(FileBackedUp), "backup")]
[JsonDerivedType(typeof(FileWasAbsent), "absent")]
[JsonDerivedType(typeof(FileWritten), "written")]
[JsonDerivedType(typeof(BackupRelocated), "relocated")]
internal abstract record JournalEntry;

/// <summary>The journal's first line, which names the operation and the folders it works on.</summary>
/// <param name="FormatVersion">The journal's format.</param>
/// <param name="Kind">What the operation does.</param>
/// <param name="RecordId">The install the operation creates or works on.</param>
/// <param name="GameDirectory">The canonical game folder, a full path.</param>
/// <param name="StagingDirectory">Files the operation writes before renaming them into place; all of them are its own.</param>
/// <param name="BackupDirectory">Where the originals are, or were before a move of the backups.</param>
/// <param name="GameVolume">The game folder's volume, to tell a deleted game from one on an unplugged drive.</param>
/// <param name="TargetBackup">Where a move of the backups takes them.</param>
internal sealed record TransactionStarted(
    int FormatVersion,
    TransactionKind Kind,
    Guid RecordId,
    string GameDirectory,
    string StagingDirectory,
    string BackupDirectory,
    string? GameVolume = null,
    BackupFolder? TargetBackup = null) : JournalEntry
{
    public const int CurrentFormatVersion = 1;
}

/// <summary>A folder about to be created, which undoing removes once it is empty.</summary>
/// <param name="Path">The folder's path.</param>
internal sealed record DirectoryCreated(string Path) : JournalEntry;

/// <summary>An original about to go into the backup folder.</summary>
/// <param name="Path">The original's path.</param>
/// <param name="Name">The backup's name in the backup folder.</param>
/// <param name="Original">The original's content.</param>
/// <param name="Moved">The original was moved into the backup folder; otherwise it was copied there and stayed in place.</param>
/// <param name="Attributes">Attributes such as read-only that come back with the original.</param>
internal sealed record FileBackedUp(string Path, string Name, FileSnapshot Original, bool Moved, FileAttributes? Attributes = null)
    : JournalEntry;

/// <summary>A path without a file before the operation writes one, which undoing removes.</summary>
/// <param name="Path">The file's path.</param>
internal sealed record FileWasAbsent(string Path) : JournalEntry;

/// <summary>Content the operation is about to put at a path, so undoing it never removes what someone else wrote later.</summary>
/// <param name="Path">Where the content goes.</param>
/// <param name="Content">The content's size and hash.</param>
internal sealed record FileWritten(string Path, FileSnapshot Content) : JournalEntry;

/// <summary>A backup file about to be moved or copied from the old backup folder to the new one.</summary>
/// <param name="Name">The backup's name.</param>
/// <param name="Moved">Whether it is moved rather than copied.</param>
internal sealed record BackupRelocated(string Name, bool Moved) : JournalEntry;


/// <summary>
/// JSON lines written ahead of the changes they describe, made durable per batch by <see cref="Commit"/> before anything
/// in the batch happens, and harmless when their change never happened, since every undo is idempotent.
/// </summary>
internal sealed class TransactionJournal : IDisposable
{
    private const int BufferSize = 1 << 16;

    private static readonly JsonWriterOptions LineOptions = new() { Indented = false, SkipValidation = false };

    private readonly FileStream _stream;
    private readonly List<JournalEntry> _entries;

    private TransactionJournal(FileStream stream, List<JournalEntry> entries)
    {
        _stream = stream;
        _entries = entries;
    }

    public TransactionStarted Header => (TransactionStarted)_entries[0];

    public IReadOnlyList<JournalEntry> Entries => _entries;

    /// <summary>Creates a journal and makes its header durable.</summary>
    /// <param name="path">The journal file; nothing may exist there yet.</param>
    /// <param name="header">The first line.</param>
    public static TransactionJournal Create(string path, TransactionStarted header)
    {
        var journal = new TransactionJournal(
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, BufferSize), []);
        try
        {
            journal.Add(header);
            journal.Commit();
            return journal;
        }
        catch
        {
            journal.Dispose();
            FileOperations.TryDeleteFile(path);
            throw;
        }
    }

    /// <summary>Reads every complete line of a journal.</summary>
    /// <param name="path">The journal file.</param>
    /// <exception cref="InvalidDataException">A complete line cannot be read, so the journal cannot be trusted.</exception>
    public static IReadOnlyList<JournalEntry> Read(string path) => Parse(path, int.MaxValue);

    /// <summary>Reads only the first line, so a journal damaged further on still tells which game it belongs to.</summary>
    /// <param name="path">The journal file.</param>
    /// <returns>Null when the journal never described a change.</returns>
    /// <exception cref="InvalidDataException">The header cannot be read or was written by a newer app.</exception>
    public static TransactionStarted? ReadHeader(string path) =>
        Parse(path, maxEntries: 1).FirstOrDefault() switch
        {
            null => null,
            TransactionStarted { FormatVersion: <= TransactionStarted.CurrentFormatVersion } header => header,
            _ => throw new InvalidDataException("The journal header is not one this version can read."),
        };

    /// <summary>Writes a line, which is durable only after the next <see cref="Commit"/>.</summary>
    /// <param name="entry">The change about to happen.</param>
    public void Add(JournalEntry entry)
    {
        using (var writer = new Utf8JsonWriter(_stream, LineOptions))
        {
            JsonSerializer.Serialize(writer, entry, InstallationJsonContext.Default.JournalEntry);
        }

        _stream.WriteByte((byte)'\n');
        _entries.Add(entry);
    }

    /// <summary>Makes every entry added so far durable; only then may the changes they describe happen.</summary>
    public void Commit() => _stream.Flush(flushToDisk: true);

    public void Dispose() => _stream.Dispose();

    private static List<JournalEntry> Parse(string path, int maxEntries)
    {
        byte[] content;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            content = new byte[stream.Length];
            stream.ReadExactly(content);
        }

        var entries = new List<JournalEntry>();
        var position = 0;
        int end;
        // A line without its newline was cut short by a crash; its change never started, because the newline is made
        // durable together with the line before any change.
        while (entries.Count < maxEntries && (end = Array.IndexOf(content, (byte)'\n', position)) >= 0)
        {
            var line = content.AsSpan(position, end - position);
            if (!line.IsEmpty)
            {
                try
                {
                    entries.Add(JsonSerializer.Deserialize(line, InstallationJsonContext.Default.JournalEntry)
                        ?? throw new InvalidDataException("Empty journal entry."));
                }
                catch (Exception exception) when (IsDamaged(exception) && end == content.Length - 1)
                {
                    // Power loss during a flush can persist the end of the last line without its start, which may even
                    // read as a nested object without an entry type. Its change never started either, so it is dropped
                    // like a line without its newline.
                    break;
                }
                catch (Exception exception) when (IsDamaged(exception))
                {
                    throw new InvalidDataException($"The journal line at byte {position} cannot be read.", exception);
                }
            }

            position = end + 1;
        }

        return entries;
    }

    private static bool IsDamaged(Exception exception) => exception is JsonException or NotSupportedException;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    AllowOutOfOrderMetadataProperties = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = [typeof(Games.GameKeyJsonConverter)])]
[JsonSerializable(typeof(JournalEntry))]
[JsonSerializable(typeof(InstallRecord))]
[JsonSerializable(typeof(InstalledFiles))]
internal sealed partial class InstallationJsonContext : JsonSerializerContext;
