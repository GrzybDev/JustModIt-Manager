using System.Text.Json;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// A project page with the JSON it was read from, which the cache keeps rather than the parsed page, so a field this
/// version of the app does not know survives for a newer one.
/// </summary>
/// <param name="Page">The page as this version of the app reads it.</param>
/// <param name="Json">The UTF-8 JSON exactly as the site sent it.</param>
public sealed record ProjectPageDocument(ProjectPage Page, ReadOnlyMemory<byte> Json)
{
    /// <summary>Reads a project page, keeping its JSON.</summary>
    /// <param name="json">The UTF-8 JSON as the site sent it.</param>
    /// <exception cref="JsonException">The JSON is not a project page.</exception>
    public static ProjectPageDocument Parse(ReadOnlyMemory<byte> json) =>
        new(JsonSerializer.Deserialize(json.Span, CatalogueJsonContext.Default.ProjectPage)
            ?? throw new JsonException("The project page is null."), json);
}
