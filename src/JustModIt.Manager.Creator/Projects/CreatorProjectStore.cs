using System.Collections.Immutable;
using System.Text.Encodings.Web;
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

    /// <summary>Creates the exception for a damaged project.</summary>
    public ProjectLoadException()
        : this(ProjectLoadError.Damaged)
    {
    }

    /// <summary>Creates the exception for a damaged project with a message for logs.</summary>
    /// <param name="message">Meant for logs, not for users.</param>
    public ProjectLoadException(string message)
        : this(message, null!)
    {
    }

    /// <summary>Creates the exception for a damaged project with a message for logs and the error behind it.</summary>
    /// <param name="message">Meant for logs, not for users.</param>
    /// <param name="innerException">The error behind it.</param>
    public ProjectLoadException(string message, Exception innerException)
        : base(message, innerException) =>
        Error = ProjectLoadError.Damaged;

    public ProjectLoadError Error { get; }
}

/// <summary>
/// Saves and opens project files, storing the author's folder relative to the project file when it lies inside the
/// project's folder, so a folder holding both can move to another drive or machine.
/// </summary>
public static class CreatorProjectStore
{
    public const int CurrentFormatVersion = 3;

    private const string FormatVersionProperty = "formatVersion";
    private const long MaxLength = 16 << 20;

    // People read these files, so letters stay letters instead of escapes.
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Writes the project file atomically, off the calling thread.</summary>
    /// <param name="project">The project to save.</param>
    /// <param name="path">The project file, replaced when it exists.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public static Task SaveAsync(CreatorProject project, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return Task.Run(() => AtomicFile.WriteAllBytesAsync(path, Serialize(project, directory), cancellationToken), cancellationToken);
    }

    /// <summary>Opens a project file of this or an older format, off the calling thread.</summary>
    /// <param name="path">The project file.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <exception cref="ProjectLoadException">The file cannot be read, is damaged or was written by a newer app.</exception>
    public static Task<CreatorProject> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(async () =>
        {
            byte[] content;
            try
            {
                content = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
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
    internal static byte[] Serialize(CreatorProject project, string projectDirectory)
    {
        var document = new ProjectDocument
        {
            FormatVersion = CurrentFormatVersion,
            Mode = Word(project.Mode),
            Game = project.Game is { } game ? new GameDocument(game.Key.Store.Value, game.Key.StoreGameId, game.Name) : null,
            GameFolder = project.GameFolder,
            AuthorFolder = RelativeWhenInside(projectDirectory, project.AuthorFolder),
            Choices = project.Choices.Count > 0 ? project.Choices.ToDictionary(choice => choice.Key, choice => Word(choice.Value)) : null,
            ExtraSteps = project.ExtraSteps.Count > 0 ? [.. project.ExtraSteps] : null,
            Steps = project.Steps.Count > 0 ? [.. project.Steps] : null,
            Metadata = new MetadataDocument(project.Metadata.Title, project.Metadata.Version, [.. project.Metadata.Authors]),
            ThirdParty = project.ThirdParty.Count > 0 ? [.. project.ThirdParty] : null,
            Acknowledged = project.Acknowledged.Count > 0 ? [.. project.Acknowledged] : null,
        };
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            JsonSerializer.Serialize(writer, document, ProjectJsonContext.Default.ProjectDocument);
        }

        return buffer.ToArray();
    }

    /// <summary>Reads a project file's content of any format this version knows.</summary>
    /// <param name="content">The file's bytes, with or without a byte order mark.</param>
    /// <param name="projectDirectory">The project file's folder, which a relative author's folder starts from.</param>
    /// <exception cref="ProjectLoadException">The content is damaged or was written by a newer app.</exception>
    internal static CreatorProject Deserialize(byte[] content, string projectDirectory)
    {
        // Editors like to add a byte order mark, which the parser refuses.
        ReadOnlySpan<byte> byteOrderMark = [0xEF, 0xBB, 0xBF];
        if (content.AsSpan().StartsWith(byteOrderMark))
        {
            content = content[byteOrderMark.Length..];
        }

        try
        {
            // The version is read on its own first, so a project from a newer app says "update" even when the rest of
            // it is unreadable to this one.
            using (var json = JsonDocument.Parse(content))
            {
                var version = json.RootElement.ValueKind == JsonValueKind.Object
                    && json.RootElement.TryGetProperty(FormatVersionProperty, out var stored) && stored.TryGetInt32(out var number)
                        ? number
                        : 0;
                switch (version)
                {
                    case > CurrentFormatVersion:
                        throw new ProjectLoadException(ProjectLoadError.NewerVersion);
                    case PrototypeProject.FormatVersion:
                        return PrototypeProject.Read(json.RootElement);
                    case < CurrentFormatVersion:
                        throw new ProjectLoadException(ProjectLoadError.Damaged);
                }
            }

            var document = JsonSerializer.Deserialize(content, ProjectJsonContext.Default.ProjectDocument)
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
        Mode = document.Mode is null ? CreatorMode.Simple : Parse<CreatorMode>(document.Mode),
        Game = document.Game is { } game ? new ProjectGame(new GameKey(new StoreId(game.Store), game.Id), game.Name ?? game.Id) : null,
        GameFolder = document.GameFolder ?? string.Empty,
        AuthorFolder = document.AuthorFolder switch
        {
            null or "" => string.Empty,
            var folder when Path.IsPathFullyQualified(folder) => folder,
            var folder => Path.GetFullPath(PackagePath.ToFullPath(projectDirectory, folder)),
        },
        Choices = new ValueDictionary<string, FileChoice>((document.Choices ?? [])
            .ToImmutableDictionary(choice => choice.Key, choice => Parse<FileChoice>(choice.Value), PackagePath.Comparer)),
        ExtraSteps = Items(document.ExtraSteps),
        Steps = Items(document.Steps),
        Metadata = new PackageMetadata
        {
            Title = document.Metadata?.Title ?? string.Empty,
            Version = document.Metadata?.Version ?? string.Empty,
            Authors = Items(document.Metadata?.Authors),
        },
        ThirdParty = Items(document.ThirdParty),
        Acknowledged = [.. Items(document.Acknowledged).Distinct(StringComparer.Ordinal)],
    };

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

    private static async Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0, FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            if (stream.Length > MaxLength)
            {
                throw new ProjectLoadException(ProjectLoadError.Damaged);
            }

            var content = new byte[stream.Length];
            await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
            return content;
        }
    }

    /// <summary>A member's name in lower case, as project files store it.</summary>
    /// <param name="value">The member.</param>
    internal static string Word<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        value.ToString().ToLowerInvariant();

    /// <summary>One member by its name in any letter case; numbers and combinations are refused.</summary>
    /// <param name="text">The stored name.</param>
    internal static TEnum Parse<TEnum>(string text)
        where TEnum : struct, Enum =>
        Enum.GetNames<TEnum>().FirstOrDefault(name => name.Equals(text, StringComparison.OrdinalIgnoreCase)) is { } name
            ? Enum.Parse<TEnum>(name)
            : throw new JsonException($"'{text}' is not a {typeof(TEnum).Name}.");
}
