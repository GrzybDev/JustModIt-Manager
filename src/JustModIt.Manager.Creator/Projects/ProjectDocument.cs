using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Creator.Projects;

/// <summary>One package's work as it is stored; enums are lower-case words, the author's folders are relative when they can be.</summary>
internal record PartDocument
{
    public string? Name { get; init; }

    public string? Mode { get; init; }

    public string? AuthorFolder { get; init; }

    /// <summary>By the version's <see cref="GameKey"/> in its text form.</summary>
    public Dictionary<string, string>? VersionFolders { get; init; }

    public Dictionary<string, string>? Choices { get; init; }

    public List<ProjectStep>? ExtraSteps { get; init; }

    public List<ProjectStep>? Steps { get; init; }

    public List<RuntimeFileDocument>? RuntimeFiles { get; init; }
}

/// <summary>A project file as it is stored; formats 3 and 4 hold their only part, game and game folder at the top level.</summary>
internal sealed record ProjectDocument : PartDocument
{
    public int FormatVersion { get; init; }

    public Guid? Id { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public GameDocument? Game { get; init; }

    public string? GameFolder { get; init; }

    public List<VersionDocument>? Versions { get; init; }

    public List<PartDocument>? Parts { get; init; }

    public MetadataDocument? Metadata { get; init; }

    public string? OutputFolder { get; init; }

    public ProjectSite? Site { get; init; }

    public List<GameKey>? DeclinedVersions { get; init; }

    public List<ToolNotice>? ThirdParty { get; init; }

    public List<LibraryNotice>? Libraries { get; init; }

    public List<string>? Acknowledged { get; init; }
}

internal sealed record VersionDocument(GameDocument Game, string? GameFolder = null, List<FolderRename>? Folders = null);

internal sealed record GameDocument(string Store, string Id, string? Name = null);

internal sealed record MetadataDocument(string? Title = null, string? Version = null, List<string>? Authors = null);

internal sealed record RuntimeFileDocument(string Path, string Kind);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    AllowOutOfOrderMetadataProperties = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    Converters = [typeof(GameKeyJsonConverter)])]
[JsonSerializable(typeof(ProjectDocument))]
[JsonSerializable(typeof(BuildRecord))]
[JsonSerializable(typeof(GameKey))]
internal sealed partial class ProjectJsonContext : JsonSerializerContext;
