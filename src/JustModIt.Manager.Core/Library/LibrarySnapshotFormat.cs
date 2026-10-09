using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Library;

/// <summary>
/// The last library snapshot, kept so the window can show the games at once after the app starts; an entry that cannot
/// be read is left out, since the first scan replaces everything anyway.
/// </summary>
public sealed class LibrarySnapshotFormat : IJsonDocumentFormat<LibrarySnapshot>
{
    private const string ScannedAt = "scannedAt";
    private const string Stores = "stores";
    private const string Games = "games";
    private const string Launch = "launch";

    public static LibrarySnapshotFormat Instance { get; } = new();

    public int CurrentVersion => 2;

    public IReadOnlySet<string> KnownProperties { get; } = FrozenSet.Create(StringComparer.Ordinal, ScannedAt, Stores, Games);

    /// <summary>Gives the snapshot's file in the app's state folder.</summary>
    /// <param name="locations">The app's data folders.</param>
    public static string FileIn(AppDataLocations locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        return Path.Combine(locations.StateDirectory, "library.json");
    }

    public LibrarySnapshot Read(JsonElement root, int version) => new()
    {
        ScannedAt = root.TryGetProperty(ScannedAt, out var scannedAt) && scannedAt.ValueKind == JsonValueKind.String
            && scannedAt.TryGetDateTimeOffset(out var time)
                ? time
                : null,
        Stores = [.. Items(root, Stores).Select(ReadStore).OfType<StoreStatus>()],
        Games = new ValueDictionary<GameKey, InstalledGame>(Items(root, Games).Select(ReadGame).OfType<InstalledGame>()
            .DistinctBy(game => game.Key).ToImmutableDictionary(game => game.Key)),
        IsCached = true,
    };

    public void Write(Utf8JsonWriter writer, LibrarySnapshot document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);
        if (document.ScannedAt is { } scannedAt)
        {
            writer.WriteString(ScannedAt, scannedAt);
        }

        writer.WriteStartArray(Stores);
        foreach (var store in document.Stores)
        {
            writer.WriteStartObject();
            writer.WriteString("store", store.Store.Value);
            writer.WriteBoolean("isPresent", store.IsPresent);
            writer.WriteBoolean("scanFailed", store.ScanFailed);
            writer.WriteBoolean("isComplete", store.IsComplete);
            writer.WriteStartArray("libraries");
            foreach (var library in store.Libraries)
            {
                writer.WriteStartObject();
                writer.WriteString("id", library.Id);
                writer.WriteString("source", library.Source);
                if (library.Path is not null)
                {
                    writer.WriteString("path", library.Path);
                }

                writer.WriteString("state", library.State.ToString());
                writer.WriteBoolean("isManual", library.IsManual);
                writer.WriteNumber("unreadableEntries", library.UnreadableEntries);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray(Games);
        // Sorted, so the file changes only where a game changed.
        foreach (var game in document.Games.Values.OrderBy(game => game.Key.ToString(), StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("key", game.Key.ToString());
            writer.WriteString("name", game.Name);
            writer.WriteString("installDirectory", game.InstallDirectory);
            writer.WriteString("libraryId", game.LibraryId);
            if (game.BuildId is not null)
            {
                writer.WriteString("buildId", game.BuildId);
            }

            writer.WriteString("state", game.State.ToString());
            writer.WriteString("reason", game.Reason.ToString());
            if (game.StoreActivityAt is { } activity)
            {
                writer.WriteString("storeActivityAt", activity);
            }

            writer.WriteString("platform", game.Platform.ToString());
            writer.WriteStartArray("languages");
            foreach (var language in game.Languages)
            {
                writer.WriteStringValue(language);
            }

            writer.WriteEndArray();
            WriteLaunch(writer, game.Launch);
            writer.WriteBoolean("isStale", game.IsStale);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteLaunch(Utf8JsonWriter writer, GameLaunch? launch)
    {
        if (launch is null)
        {
            return;
        }

        writer.WriteStartObject(Launch);
        if (launch is LaunchLink link)
        {
            writer.WriteString("link", link.Uri);
        }
        else if (launch is LaunchProgram program)
        {
            writer.WriteString("executable", program.Executable);
            writer.WriteString("arguments", program.Arguments);
            writer.WriteString("workingDirectory", program.WorkingDirectory);
        }

        writer.WriteEndObject();
    }

    private static GameLaunch? ReadLaunch(JsonElement game) =>
        game.GetOrNull(Launch, JsonValueKind.Object) is not { } launch ? null
        : launch.GetTextOrNull("link") is { } link ? LaunchLink.TryCreate(link)
        : LaunchProgram.TryCreate(launch.GetTextOrNull("executable"), launch.GetTextOrNull("arguments"),
            launch.GetTextOrNull("workingDirectory"));

    private static StoreStatus? ReadStore(JsonElement store) =>
        StoreId.TryParse(store.GetTextOrNull("store"), out var id)
            ? new StoreStatus(
                id,
                store.GetBooleanOrNull("isPresent") ?? false,
                store.GetBooleanOrNull("scanFailed") ?? false,
                store.GetBooleanOrNull("isComplete") ?? true,
                [.. Items(store, "libraries").Select(ReadLibrary).OfType<LibraryLocation>()])
            : null;

    private static LibraryLocation? ReadLibrary(JsonElement library) =>
        library.GetTextOrNull("id") is { } id && library.GetTextOrNull("source") is { } source
            ? new LibraryLocation(
                id,
                source,
                library.GetTextOrNull("path"),
                library.GetEnumOrNull<LibraryState>("state") ?? LibraryState.Online,
                library.GetBooleanOrNull("isManual") ?? false,
                library.GetOrNull("unreadableEntries", JsonValueKind.Number) is { } count && count.TryGetInt32(out var entries) ? entries : 0)
            : null;

    private static InstalledGame? ReadGame(JsonElement game)
    {
        if (!GameKey.TryParse(game.GetTextOrNull("key"), out var key)
            || game.GetTextOrNull("name") is not { } name
            || game.GetFullPathOrNull("installDirectory") is not { } installDirectory
            || game.GetTextOrNull("libraryId") is not { } libraryId)
        {
            return null;
        }

        return new InstalledGame
        {
            Key = key,
            Name = name,
            InstallDirectory = installDirectory,
            LibraryId = libraryId,
            BuildId = game.GetTextOrNull("buildId"),
            // A state that cannot be read must not look installed.
            State = game.GetEnumOrNull<GameInstallState>("state") ?? GameInstallState.Unknown,
            Reason = game.GetEnumOrNull<GameStateReason>("reason") ?? GameStateReason.None,
            StoreActivityAt = game.TryGetProperty("storeActivityAt", out var activity) && activity.ValueKind == JsonValueKind.String
                && activity.TryGetDateTimeOffset(out var time)
                    ? time
                    : null,
            Platform = game.GetEnumOrNull<OperatingSystemKind>("platform") ?? OperatingSystemKind.Unknown,
            Languages = [.. Items(game, "languages", JsonValueKind.String).Select(language => language.GetString()!)],
            Launch = ReadLaunch(game),
            IsStale = game.GetBooleanOrNull("isStale") ?? false,
        };
    }

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name, JsonValueKind kind = JsonValueKind.Object) =>
        parent.GetOrNull(name, JsonValueKind.Array) is { } array ? array.EnumerateArray().Where(item => item.ValueKind == kind) : [];
}
