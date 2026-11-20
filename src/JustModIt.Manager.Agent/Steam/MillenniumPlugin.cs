using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// The app's plugin among Millennium's, and Millennium's list of the plugins it loads, which Millennium keeps in memory while
/// Steam runs and writes back, so both change only while Steam is closed.
/// </summary>
/// <param name="millennium">Where Millennium keeps its plugins and its settings.</param>
/// <param name="name">The plugin's name, the app's own, so two apps or a development build each keep a plugin of their own.</param>
internal sealed class MillenniumPlugin(MillenniumFolders millennium, string name)
{
    public const string TextsFileName = "texts.json";
    public const string StatusFileName = "status.json";

    private const string ManifestFileName = "plugin.json";

    public string Name => name;

    public string Directory { get; } = Path.Join(millennium.Plugins, name);

    private string ConfigFile => millennium.ConfigFile;

    /// <summary>The plugin's files as this version of the app writes them, by their path in the plugin's folder.</summary>
    /// <param name="name">The plugin's name.</param>
    /// <param name="version">The app's version, which the plugin carries.</param>
    /// <param name="title">The plugin's name as Millennium shows it.</param>
    /// <param name="description">What the plugin does, as Millennium shows it.</param>
    public static IReadOnlyDictionary<string, byte[]> Files(string name, string version, string title, string description)
    {
        var manifest = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(manifest, AtomicFile.ReadableJson))
        {
            writer.WriteStartObject();
            writer.WriteString("name", name);
            writer.WriteString("common_name", title);
            writer.WriteString("description", description);
            writer.WriteString("version", version);
            writer.WriteString("backendType", "lua");
            writer.WriteEndObject();
        }

        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [ManifestFileName] = manifest.WrittenSpan.ToArray(),
            [Path.Join("backend", "main.lua")] = Encoding.UTF8.GetBytes(SteamScripts.PluginBackend(name)),
            [Path.Join(".millennium", "Dist", "index.js")] = Encoding.UTF8.GetBytes(SteamScripts.PluginFrontend(name)),
        };
    }

    /// <summary>The version of the plugin's files; null when the plugin is not there.</summary>
    public string? InstalledVersion()
    {
        using var manifest = ReadJson(ManifestFileName);
        return manifest is null ? null : manifest.RootElement.GetTextOrNull("version") ?? "";
    }

    /// <summary>Whether Millennium's list of the plugins it loads names the plugin.</summary>
    public bool IsEnabled()
    {
        try
        {
            return EnabledList(ReadConfig(), create: false)?.Any(IsThisPlugin) == true;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether the plugin's files are these.</summary>
    /// <param name="files">The files as <see cref="Files"/> gives them.</param>
    public bool IsCurrent(IReadOnlyDictionary<string, byte[]> files) =>
        files.All(file => ReadBytes(file.Key) is { } bytes && bytes.AsSpan().SequenceEqual(file.Value));

    /// <summary>
    /// Writes the plugin's files, enabling a plugin that was not there, so one the player turned off in Millennium stays
    /// off; only while Steam is closed.
    /// </summary>
    /// <param name="files">The files as <see cref="Files"/> gives them.</param>
    /// <param name="cancellationToken">Stops between files.</param>
    /// <exception cref="InvalidDataException">Millennium's settings cannot be read or are not as the app knows them.</exception>
    public async Task InstallAsync(IReadOnlyDictionary<string, byte[]> files, CancellationToken cancellationToken)
    {
        // Enabled before its files go in, so a stop halfway never leaves a plugin that looks turned off by the player.
        if (InstalledVersion() is null)
        {
            await EnableAsync(true, cancellationToken).ConfigureAwait(false);
        }

        foreach (var (path, content) in files)
        {
            await AtomicFile.WriteAllBytesAsync(Path.Join(Directory, path), content, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes the plugin's folder and takes the plugin off Millennium's list, the folder first, since a name on the list
    /// without its folder loads nothing.
    /// </summary>
    /// <param name="cancellationToken">Stops the change of Millennium's settings.</param>
    /// <exception cref="InvalidDataException">Millennium's settings cannot be read or are not as the app knows them.</exception>
    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }

        // Also settings an uninstalled Millennium left behind, which an install later reads again.
        if (File.Exists(ConfigFile))
        {
            await EnableAsync(false, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Replaces the texts file the plugin reads.</summary>
    /// <param name="content">The file's content.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task WriteTextsAsync(byte[] content, CancellationToken cancellationToken) =>
        AtomicFile.WriteAllBytesAsync(Path.Join(Directory, TextsFileName), content, cancellationToken);

    /// <summary>The texts file as it is; null when there is none.</summary>
    public byte[]? ReadTexts() => ReadBytes(TextsFileName);

    /// <summary>What the plugin reported last; null when it reported nothing or the report cannot be read now.</summary>
    public JsonDocument? ReadStatus() => ReadJson(StatusFileName);

    private async Task EnableAsync(bool enabled, CancellationToken cancellationToken)
    {
        var config = ReadConfig();
        var list = EnabledList(config, create: enabled);
        var present = list?.Where(IsThisPlugin).ToList() ?? [];
        if ((present.Count > 0) == enabled)
        {
            return;
        }

        if (enabled)
        {
            list!.Add((JsonNode?)JsonValue.Create(name));
        }

        present.ForEach(item => list!.Remove(item));
        await AtomicFile.WriteJsonAsync(ConfigFile, AtomicFile.ReadableJson, writer => config.WriteTo(writer), cancellationToken)
            .ConfigureAwait(false);
    }

    // Every other setting stays as Millennium wrote it.
    private JsonObject ReadConfig()
    {
        try
        {
            return JsonNode.Parse(File.ReadAllBytes(ConfigFile)) as JsonObject ?? throw new InvalidDataException("Millennium's settings are not an object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Millennium's settings cannot be read.", exception);
        }
    }

    private static JsonArray? EnabledList(JsonObject config, bool create) =>
        Child(config, "plugins", create, () => new JsonObject()) is { } plugins
            ? Child(plugins, "enabledPlugins", create, () => new JsonArray())
            : null;

    // A value of another shape belongs to a Millennium the app does not know, whose other plugins it must not turn off.
    private static T? Child<T>(JsonObject parent, string name, bool create, Func<T> made) where T : JsonNode => parent[name] switch
    {
        T found => found,
        null when create => (T)(parent[name] = made()),
        null => null,
        _ => throw new InvalidDataException($"Millennium's settings hold {name} of another shape."),
    };

    private bool IsThisPlugin(JsonNode? item) => item?.GetValueKind() == JsonValueKind.String && item.ToString() == name;

    private byte[]? ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(Path.Join(Directory, path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The plugin writes its report in place, so a read may meet half of it; the next change brings the whole.
    private JsonDocument? ReadJson(string path)
    {
        try
        {
            return ReadBytes(path) is { } bytes ? JsonDocument.Parse(bytes) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
