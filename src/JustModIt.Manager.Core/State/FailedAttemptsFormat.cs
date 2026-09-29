using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;

namespace JustModIt.Manager.Core.State;

/// <summary>The last failed job of each game, kept across restarts until a job on the game succeeds or the player dismisses it.</summary>
public sealed record FailedAttempts(ValueDictionary<GameKey, FailedAttempt> Games)
{
    public static FailedAttempts Empty { get; } = new(default(ValueDictionary<GameKey, FailedAttempt>));
}

/// <summary>The format of <c>state/failures.json</c>, which leaves out an entry it cannot read.</summary>
public sealed class FailedAttemptsFormat : IJsonDocumentFormat<FailedAttempts>
{
    private const string GamesProperty = "games";

    public static FailedAttemptsFormat Instance { get; } = new();

    public int CurrentVersion => 1;

    public IReadOnlySet<string> KnownProperties { get; } = FrozenSet.Create(StringComparer.Ordinal, GamesProperty);

    /// <summary>The failures' file.</summary>
    /// <param name="locations">The app's data folders.</param>
    public static string FileIn(AppDataLocations locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        return Path.Combine(locations.StateDirectory, "failures.json");
    }

    public FailedAttempts Read(JsonElement root, int version)
    {
        var entries = root.GetOrNull(GamesProperty, JsonValueKind.Array) is { } games
            ? games.EnumerateArray().Where(entry => entry.ValueKind == JsonValueKind.Object).Select(ReadEntry).OfType<(GameKey, FailedAttempt)>()
            : [];
        var builder = ImmutableDictionary.CreateBuilder<GameKey, FailedAttempt>();
        foreach (var (game, attempt) in entries)
        {
            builder[game] = attempt;
        }

        return new FailedAttempts(new ValueDictionary<GameKey, FailedAttempt>(builder.ToImmutable()));
    }

    public void Write(Utf8JsonWriter writer, FailedAttempts document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);
        writer.WriteStartArray(GamesProperty);
        foreach (var (game, attempt) in document.Games.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("game", game.ToString());
            writer.WriteString("kind", attempt.Kind.ToString());
            writer.WriteString("origin", attempt.Origin.ToString());
            writer.WriteString("at", attempt.At);
            writer.WriteBoolean("lostPrevious", attempt.LostPrevious);
            WriteGuid(writer, "releaseId", attempt.ReleaseId);
            WriteGuid(writer, "packageFileId", attempt.PackageFileId);
            if (attempt.Failure is { } failure)
            {
                writer.WriteStartObject("failure");
                WriteFailure(writer, failure);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static (GameKey, FailedAttempt)? ReadEntry(JsonElement entry)
    {
        if (!GameKey.TryParse(entry.GetTextOrNull("game"), out var game)
            || entry.GetEnumOrNull<JobKind>("kind") is not { } kind
            || entry.GetOrNull("at", JsonValueKind.String) is not { } time || !time.TryGetDateTimeOffset(out var at))
        {
            return null;
        }

        var failure = entry.GetOrNull("failure", JsonValueKind.Object) is { } failed && failed.GetEnumOrNull<JobFailureReason>("reason") is { } reason
            ? ReadFailure(failed, reason)
            : null;
        return (game, new FailedAttempt(kind, entry.GetEnumOrNull<JobOrigin>("origin") ?? JobOrigin.User, at, failure)
        {
            LostPrevious = entry.GetBooleanOrNull("lostPrevious") ?? false,
            ReleaseId = ReadGuid(entry, "releaseId"),
            PackageFileId = ReadGuid(entry, "packageFileId"),
        });
    }

    private static JobFailure ReadFailure(JsonElement failure, JobFailureReason reason) => new(reason)
    {
        Files = Texts(failure, "files"),
        Processes = Texts(failure, "processes"),
        GameState = failure.GetEnumOrNull<GameInstallState>("gameState"),
        StateReason = failure.GetEnumOrNull<GameStateReason>("stateReason"),
        Unavailability = failure.GetEnumOrNull<SelectionUnavailability>("unavailability"),
        CatalogueError = failure.GetEnumOrNull<CatalogueErrorReason>("catalogueError"),
        RequiredBytes = Number(failure, "requiredBytes"),
        AvailableBytes = Number(failure, "availableBytes"),
        Location = failure.GetTextOrNull("location"),
    };

    private static void WriteFailure(Utf8JsonWriter writer, JobFailure failure)
    {
        writer.WriteString("reason", failure.Reason.ToString());
        WriteTexts(writer, "files", failure.Files);
        WriteTexts(writer, "processes", failure.Processes);
        WriteEnum(writer, "gameState", failure.GameState);
        WriteEnum(writer, "stateReason", failure.StateReason);
        WriteEnum(writer, "unavailability", failure.Unavailability);
        WriteEnum(writer, "catalogueError", failure.CatalogueError);
        if (failure.RequiredBytes is { } required)
        {
            writer.WriteNumber("requiredBytes", required);
        }

        if (failure.AvailableBytes is { } available)
        {
            writer.WriteNumber("availableBytes", available);
        }

        if (failure.Location is { } location)
        {
            writer.WriteString("location", location);
        }
    }

    private static ValueList<string> Texts(JsonElement parent, string name) =>
        parent.GetOrNull(name, JsonValueKind.Array) is { } array
            ? [.. array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    private static long? Number(JsonElement parent, string name) =>
        parent.GetOrNull(name, JsonValueKind.Number) is { } number && number.TryGetInt64(out var value) ? value : null;

    private static Guid? ReadGuid(JsonElement parent, string name) => Guid.TryParse(parent.GetTextOrNull(name), out var id) ? id : null;

    private static void WriteTexts(Utf8JsonWriter writer, string name, ValueList<string> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WriteEnum<TEnum>(Utf8JsonWriter writer, string name, TEnum? value)
        where TEnum : struct, Enum
    {
        if (value is { } known)
        {
            writer.WriteString(name, known.ToString());
        }
    }

    private static void WriteGuid(Utf8JsonWriter writer, string name, Guid? value)
    {
        if (value is { } id)
        {
            writer.WriteString(name, id.ToString("D"));
        }
    }
}
