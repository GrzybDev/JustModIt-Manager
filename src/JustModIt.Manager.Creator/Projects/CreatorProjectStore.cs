using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.Creator.Projects;

public enum ProjectLoadError
{
    /// <summary>The file cannot be read, e.g. it is gone or locked.</summary>
    Unreadable,

    /// <summary>The file is no project, or it is broken.</summary>
    Damaged,

    /// <summary>A newer version of the app wrote the project.</summary>
    NewerVersion,
}

public sealed class ProjectLoadException : Exception
{
    /// <summary>Creates the exception for a project that cannot be opened.</summary>
    /// <param name="error">Why it cannot be opened.</param>
    /// <param name="innerException">The error behind it; null for none.</param>
    public ProjectLoadException(ProjectLoadError error, Exception? innerException = null)
        : base($"The project cannot be opened: {error}.", innerException) =>
        Error = error;

    public ProjectLoadError Error { get; }
}

/// <summary>
/// Saves and opens project files, storing the author's folder relative to the project file when it lies inside the
/// project's folder, so a folder holding both can move to another drive or machine.
/// </summary>
public static class CreatorProjectStore
{
    public const int CurrentFormatVersion = 5;

    // Formats 3 and 4 hold one part at the top level, where format 5's document inherits a part's members, so all three read alike.
    private const int FirstDocumentVersion = 3;

    private const string FormatVersionProperty = "formatVersion";
    private const long MaxLength = 16 << 20;

    /// <summary>Writes the project file atomically, off the calling thread.</summary>
    /// <param name="project">The project to save.</param>
    /// <param name="path">The project file, replaced when it exists.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public static Task SaveAsync(CreatorProject project, string path, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return Task.Run(() => AtomicFile.WriteAllBytesAsync(path, Serialize(project, directory), cancellationToken), cancellationToken);
    }

    /// <summary>Opens a project file of this or an older format, off the calling thread.</summary>
    /// <param name="path">The project file.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <exception cref="ProjectLoadException">The file cannot be read, is damaged or was written by a newer app.</exception>
    public static Task<CreatorProject> LoadAsync(string path, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            byte[] content;
            try
            {
                content = await SharedFileRead.ReadAllBytesAsync(path, MaxLength, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                throw new ProjectLoadException(ProjectLoadError.Damaged, exception);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new ProjectLoadException(ProjectLoadError.Unreadable, exception);
            }

            return Deserialize(content, Path.GetDirectoryName(Path.GetFullPath(path))!);
        }, cancellationToken);
    }

    /// <summary>The content of the project's file.</summary>
    /// <param name="project">The project to store.</param>
    /// <param name="projectDirectory">The project file's folder, which a relative author's folder starts from.</param>
    private static byte[] Serialize(CreatorProject project, string projectDirectory)
    {
        var document = new ProjectDocument
        {
            FormatVersion = CurrentFormatVersion,
            Id = project.Id,
            Name = project.Name.Length > 0 ? project.Name : null,
            CreatedAt = project.CreatedAt,
            Versions = project.Versions.Count > 0
                ? [.. project.Versions.Select(version => new VersionDocument(
                    new GameDocument(version.Game.Key.Store.Value, version.Game.Key.StoreGameId, version.Game.Name),
                    version.GameFolder,
                    version.Folders.Count > 0 ? [.. version.Folders] : null))]
                : null,
            // Sorted, since an immutable dictionary enumerates by a string hash that differs per process, and one project must
            // always give the same bytes.
            Parts =
            [
                .. project.Parts.Select(part => new PartDocument
                {
                    Name = part.Name.Length > 0 ? part.Name : null,
                    Mode = Word(part.Mode),
                    AuthorFolder = RelativeWhenInside(projectDirectory, part.AuthorFolder),
                    VersionFolders = part.VersionFolders.Count > 0
                        ? part.VersionFolders.OrderBy(folder => folder.Key.ToString(), StringComparer.Ordinal)
                            .ToDictionary(folder => folder.Key.ToString(), folder => RelativeWhenInside(projectDirectory, folder.Value))
                        : null,
                    Choices = part.Choices.Count > 0
                        ? part.Choices.OrderBy(choice => choice.Key, StringComparer.Ordinal)
                            .ToDictionary(choice => choice.Key, choice => Word(choice.Value))
                        : null,
                    ExtraSteps = part.ExtraSteps.Count > 0 ? [.. part.ExtraSteps] : null,
                    Steps = part.Steps.Count > 0 ? [.. part.Steps] : null,
                    RuntimeFiles = part.RuntimeFiles.Count > 0
                        ? [.. part.RuntimeFiles.Select(file => new RuntimeFileDocument(file.Path, Word(file.Kind), file.Only.Count > 0 ? [.. file.Only] : null))]
                        : null,
                }),
            ],
            Metadata = new MetadataDocument(project.Metadata.Title, project.Metadata.Version, [.. project.Metadata.Authors]),
            OutputFolder = project.OutputFolder.Length > 0 ? project.OutputFolder : null,
            Site = project.Site,
            DeclinedVersions = project.DeclinedVersions.Count > 0 ? [.. project.DeclinedVersions] : null,
            ThirdParty = project.ToolNotices.Count > 0 ? [.. project.ToolNotices] : null,
            Libraries = project.LibraryNotices.Count > 0 ? [.. project.LibraryNotices] : null,
            Acknowledged = project.Acknowledged.Count > 0 ? [.. project.Acknowledged] : null,
        };
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, AtomicFile.ReadableJson with { NewLine = "\n" }))
        {
            JsonSerializer.Serialize(writer, document, ProjectJsonContext.Default.ProjectDocument);
        }

        return buffer.ToArray();
    }

    /// <summary>Reads a project file's content of any format this version knows.</summary>
    /// <param name="content">The file's bytes, with or without a byte order mark.</param>
    /// <param name="projectDirectory">The project file's folder, which a relative author's folder starts from.</param>
    /// <exception cref="ProjectLoadException">The content is damaged or was written by a newer app.</exception>
    private static CreatorProject Deserialize(byte[] content, string projectDirectory)
    {
        try
        {
            using var json = JsonDocument.Parse(Utf8Json.WithoutByteOrderMark(content.AsMemory()));
            var root = json.RootElement;
            // The version is read on its own first, so a project from a newer app says "update" even when the rest of
            // it is unreadable to this one.
            var version = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(FormatVersionProperty, out var stored) && stored.TryGetInt32(out var number)
                    ? number
                    : 0;
            switch (version)
            {
                case > CurrentFormatVersion:
                    throw new ProjectLoadException(ProjectLoadError.NewerVersion);
                case PrototypeProject.FormatVersion:
                    return PrototypeProject.Read(root);
                case < FirstDocumentVersion:
                    throw new ProjectLoadException(ProjectLoadError.Damaged);
            }

            var document = root.Deserialize(ProjectJsonContext.Default.ProjectDocument)
                ?? throw new ProjectLoadException(ProjectLoadError.Damaged);
            return ToProject(document, projectDirectory);
        }
        // A step without its type fails as an abstract type, which the serializer does not support.
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException
            or InvalidOperationException)
        {
            throw new ProjectLoadException(ProjectLoadError.Damaged, exception);
        }
    }

    private static CreatorProject ToProject(ProjectDocument document, string projectDirectory) => new()
    {
        Id = document.Id ?? default,
        Name = document.Name ?? string.Empty,
        CreatedAt = document.CreatedAt ?? default,
        Versions =
        [
            .. Items(document.Versions ?? (document.Game is { } game ? [new VersionDocument(game, document.GameFolder)] : []))
                .Select(version => new ProjectVersion(new ProjectGame(new GameKey(new StoreId(version.Game.Store), version.Game.Id),
                    version.Game.Name ?? version.Game.Id))
                {
                    GameFolder = version.GameFolder ?? string.Empty,
                    Folders = Items(version.Folders),
                }),
        ],
        // Nothing could index the base of a project without parts.
        Parts = document.Parts is { Count: 0 }
            ? throw new JsonException("A project needs a part.")
            : [.. Items(document.Parts ?? [document]).Select(part => new ProjectPart
            {
                Name = part.Name ?? string.Empty,
                Mode = part.Mode is null ? CreatorMode.Simple : Member<CreatorMode>(part.Mode),
                AuthorFolder = FullFolder(projectDirectory, part.AuthorFolder),
                VersionFolders = new ValueDictionary<GameKey, string>((part.VersionFolders ?? []).ToImmutableDictionary(
                    folder => GameKey.TryParse(folder.Key, out var key) ? key : throw new JsonException($"'{folder.Key}' is not a game."),
                    folder => FullFolder(projectDirectory, folder.Value))),
                Choices = new ValueDictionary<string, FileChoice>((part.Choices ?? [])
                    .ToImmutableDictionary(choice => choice.Key, choice => Member<FileChoice>(choice.Value), PackagePath.Comparer)),
                ExtraSteps = Items(part.ExtraSteps),
                Steps = Items(part.Steps),
                RuntimeFiles = [.. Items(part.RuntimeFiles).Select(file => new RuntimeFileChoice(file.Path, Member<RuntimeFileKind>(file.Kind)) { Only = [.. file.Only ?? []] })],
            })],
        Metadata = new PackageMetadata
        {
            Title = document.Metadata?.Title ?? string.Empty,
            Version = document.Metadata?.Version ?? string.Empty,
            Authors = Items(document.Metadata?.Authors),
        },
        OutputFolder = document.OutputFolder ?? string.Empty,
        Site = document.Site,
        DeclinedVersions = [.. document.DeclinedVersions ?? []],
        ToolNotices = Items(document.ThirdParty),
        LibraryNotices = Items(document.Libraries),
        Acknowledged = [.. Items(document.Acknowledged).Distinct(StringComparer.Ordinal)],
    };

    private static string FullFolder(string projectDirectory, string? folder) => folder switch
    {
        null or "" => string.Empty,
        _ when Path.IsPathFullyQualified(folder) => folder,
        _ => Path.GetFullPath(PackagePath.ToFullPath(projectDirectory, folder)),
    };

    private static TEnum Member<TEnum>(string text)
        where TEnum : struct, Enum =>
        LenientJson.EnumByName<TEnum>(text) ?? throw new JsonException($"'{text}' is not a {typeof(TEnum).Name}.");

    // The serializer checks the nullability of properties but not of list items.
    private static ValueList<T> Items<T>(List<T>? items)
        where T : class =>
        items?.Any(item => item is null) == true ? throw new JsonException("A list holds null.") : [.. items ?? []];

    // A relative path is kept only for a folder inside the project's folder, so moving the project file alone never
    // points it at a different folder.
    private static string RelativeWhenInside(string projectDirectory, string folder) =>
        folder.Length > 0 && PathComparison.IsWithin(projectDirectory, folder)
            ? PackagePath.FromFullPath(projectDirectory, Path.GetFullPath(folder))
            : folder;

    /// <summary>A member's name in lower case, as project files store it.</summary>
    /// <param name="value">The member.</param>
    private static string Word<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        value.ToString().ToLowerInvariant();
}
