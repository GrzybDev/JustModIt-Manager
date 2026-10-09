using System.Text.Json.Serialization;

namespace JustModIt.Manager.Creator.Projects;

/// <summary>A project file as it is stored; enums are lower-case words, the author's folder is relative when it can be.</summary>
internal sealed record ProjectDocument
{
    public int FormatVersion { get; init; }

    public string? Mode { get; init; }

    public GameDocument? Game { get; init; }

    public string? GameFolder { get; init; }

    public string? AuthorFolder { get; init; }

    public Dictionary<string, string>? Choices { get; init; }

    public List<ProjectStep>? ExtraSteps { get; init; }

    public List<ProjectStep>? Steps { get; init; }

    public MetadataDocument? Metadata { get; init; }

    public List<ToolNotice>? ThirdParty { get; init; }

    public List<string>? Acknowledged { get; init; }
}

internal sealed record GameDocument(string Store, string Id, string? Name);

internal sealed record MetadataDocument(string? Title, string? Version, List<string>? Authors);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    AllowOutOfOrderMetadataProperties = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectDocument))]
internal sealed partial class ProjectJsonContext : JsonSerializerContext;
