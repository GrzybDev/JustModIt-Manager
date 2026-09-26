using System.Text.Json;

namespace JustModIt.Manager.Core.IO;

/// <summary>Maps a document to the top-level properties of a JSON object stored by <see cref="JsonDocumentStore{T}"/>.</summary>
public interface IJsonDocumentFormat<T>
{
    /// <summary>The schema version the format writes.</summary>
    int CurrentVersion { get; }

    /// <summary>Every top-level property the format reads or writes; the store keeps all others as they were.</summary>
    IReadOnlySet<string> KnownProperties { get; }

    /// <summary>
    /// Reads the document leniently: a missing or invalid value falls back to its default instead of failing the whole
    /// document.
    /// </summary>
    /// <param name="root">The stored JSON object.</param>
    /// <param name="version">The stored schema version; 0 for documents written before versioning.</param>
    T Read(JsonElement root, int version);

    /// <summary>Writes the known properties into the object the store has already opened.</summary>
    /// <param name="writer">The writer, inside the opened object.</param>
    /// <param name="document">The document to write.</param>
    void Write(Utf8JsonWriter writer, T document);
}
