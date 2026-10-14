using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;

namespace JustModIt.Manager.Core.State;

/// <summary>
/// The last failed job of each install and of each game, kept across restarts until a job on it succeeds or the player
/// dismisses it.
/// </summary>
/// <param name="Failures">The last failed job, by the install or the game it is kept on.</param>
public sealed record FailedAttempts(ValueDictionary<FailureKey, FailedAttempt> Failures)
{
    public static FailedAttempts Empty { get; } = new(default(ValueDictionary<FailureKey, FailedAttempt>));
}

/// <summary>The format of <c>state/failures.json</c>, which leaves out an entry it cannot read.</summary>
public sealed class FailedAttemptsFormat : IJsonDocumentFormat<FailedAttempts>
{
    private const string GamesProperty = "games";

    public static FailedAttemptsFormat Instance { get; } = new();

    public int CurrentVersion => 1;

    public IReadOnlySet<string> KnownProperties { get; } = FrozenSet.Create(StringComparer.Ordinal, GamesProperty);

    public FailedAttempts Read(JsonElement root, int version)
    {
        var entries = root.GetItems(GamesProperty).Select(ReadEntry).OfType<(FailureKey, FailedAttempt)>();
        var builder = ImmutableDictionary.CreateBuilder<FailureKey, FailedAttempt>();
        foreach (var (key, attempt) in entries)
        {
            builder[key] = attempt;
        }

        return new FailedAttempts(new ValueDictionary<FailureKey, FailedAttempt>(builder.ToImmutable()));
    }

    public void Write(Utf8JsonWriter writer, FailedAttempts document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);
        writer.WriteStartArray(GamesProperty);
        foreach (var (key, attempt) in document.Failures.OrderBy(pair => pair.Key.Game.ToString(), StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Install))
        {
            writer.WriteStartObject();
            writer.WriteString("game", key.Game.ToString());
            writer.WriteIfSet("install", key.Install);
            writer.WriteString("kind", attempt.Kind.ToString());
            writer.WriteString("origin", attempt.Origin.ToString());
            writer.WriteString("at", attempt.At);
            writer.WriteBoolean("lostPrevious", attempt.LostPrevious);
            writer.WriteIfSet("releaseId", attempt.ReleaseId);
            writer.WriteStringsIfAny("packageFileIds", [.. attempt.PackageFileIds.Select(file => file.ToString("D"))]);
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

    private static (FailureKey, FailedAttempt)? ReadEntry(JsonElement entry)
    {
        if (!GameKey.TryParse(entry.GetTextOrNull("game"), out var game)
            || entry.GetEnumOrNull<JobKind>("kind") is not { } kind
            || entry.GetDateTimeOffsetOrNull("at") is not { } at)
        {
            return null;
        }

        var failure = entry.GetOrNull("failure", JsonValueKind.Object) is { } failed && failed.GetEnumOrNull<JobFailureReason>("reason") is { } reason
            ? ReadFailure(failed, reason)
            : null;
        return (new FailureKey(game, entry.GetGuidOrNull("install")), new FailedAttempt(kind,
            entry.GetEnumOrNull<JobOrigin>("origin") ?? JobOrigin.User, at, failure)
        {
            LostPrevious = entry.GetBooleanOrNull("lostPrevious") ?? false,
            ReleaseId = entry.GetGuidOrNull("releaseId"),
            PackageFileIds = PackageFileIdsOf(entry),
        });
    }

    // Earlier versions kept a single package.
    private static ValueList<Guid> PackageFileIdsOf(JsonElement entry) =>
        entry.GetGuidOrNull("packageFileId") is { } single
            ? [single]
            : [.. entry.GetStrings("packageFileIds").Select(id => Guid.TryParse(id, out var file) ? file : (Guid?)null).OfType<Guid>()];

    private static JobFailure ReadFailure(JsonElement failure, JobFailureReason reason) => new(reason)
    {
        Files = [.. failure.GetStrings("files")],
        Processes = [.. failure.GetStrings("processes")],
        GameState = failure.GetEnumOrNull<GameInstallState>("gameState"),
        StateReason = failure.GetEnumOrNull<GameStateReason>("stateReason"),
        Unavailability = failure.GetEnumOrNull<SelectionUnavailability>("unavailability"),
        CatalogueError = failure.GetEnumOrNull<CatalogueErrorReason>("catalogueError"),
        RequiredBytes = failure.GetInt64OrNull("requiredBytes"),
        AvailableBytes = failure.GetInt64OrNull("availableBytes"),
        Location = failure.GetTextOrNull("location"),
        OtherApp = failure.GetTextOrNull("otherApp"),
    };

    private static void WriteFailure(Utf8JsonWriter writer, JobFailure failure)
    {
        writer.WriteString("reason", failure.Reason.ToString());
        writer.WriteStringsIfAny("files", failure.Files);
        writer.WriteStringsIfAny("processes", failure.Processes);
        writer.WriteEnumIfSet("gameState", failure.GameState);
        writer.WriteEnumIfSet("stateReason", failure.StateReason);
        writer.WriteEnumIfSet("unavailability", failure.Unavailability);
        writer.WriteEnumIfSet("catalogueError", failure.CatalogueError);
        writer.WriteIfSet("requiredBytes", failure.RequiredBytes);
        writer.WriteIfSet("availableBytes", failure.AvailableBytes);
        writer.WriteIfSet("location", failure.Location);
        writer.WriteIfSet("otherApp", failure.OtherApp);
    }
}
