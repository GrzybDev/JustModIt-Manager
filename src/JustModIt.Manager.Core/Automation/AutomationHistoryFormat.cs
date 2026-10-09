using System.Collections.Frozen;
using System.Text.Json;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;

namespace JustModIt.Manager.Core.Automation;

/// <summary>The format of <c>state/automation.json</c>, which leaves out an entry it cannot read.</summary>
public sealed class AutomationHistoryFormat : IJsonDocumentFormat<AutomationHistory>
{
    private const string AttemptsProperty = "attempts";
    private const string AnnouncementsProperty = "announcements";
    private const string RepairAnnouncementsProperty = "repairAnnouncements";
    private const string BaselineProperty = "baselineAt";

    public static AutomationHistoryFormat Instance { get; } = new();

    public int CurrentVersion => 1;

    public IReadOnlySet<string> KnownProperties { get; } =
        FrozenSet.Create(StringComparer.Ordinal, AttemptsProperty, AnnouncementsProperty, RepairAnnouncementsProperty, BaselineProperty);

    /// <summary>The history's file.</summary>
    /// <param name="locations">The app's data folders.</param>
    public static string FileIn(AppDataLocations locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        return Path.Combine(locations.StateDirectory, "automation.json");
    }

    public AutomationHistory Read(JsonElement root, int version) => new(
        [.. Items(root, AttemptsProperty).Select(ReadAttempt).OfType<KeyValuePair<AttemptKey, AttemptEntry>>()
            .DistinctBy(attempt => attempt.Key)],
        Items(root, AnnouncementsProperty).Select(ReadAnnouncement).OfType<Announcement>(),
        Items(root, RepairAnnouncementsProperty).Select(ReadRepairAnnouncement).OfType<RepairAnnouncement>(),
        Time(root, BaselineProperty));

    public void Write(Utf8JsonWriter writer, AutomationHistory document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);
        if (document.BaselineAt is { } baseline)
        {
            writer.WriteString(BaselineProperty, baseline);
        }

        writer.WriteStartArray(AttemptsProperty);
        foreach (var (key, attempt) in document.Attempts.OrderBy(pair => pair.Key.Game.ToString(), StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Release).ThenBy(pair => pair.Key.Action))
        {
            writer.WriteStartObject();
            writer.WriteString("game", key.Game.ToString());
            writer.WriteString("release", key.Release.ToString("D"));
            writer.WriteString("action", key.Action.ToString());
            writer.WriteString("at", attempt.At);
            writer.WriteNumber("failures", attempt.Failures);
            if (attempt.Reason is { } reason)
            {
                writer.WriteString("reason", reason.ToString());
            }

            if (attempt.Build is { } build)
            {
                writer.WriteString("build", build);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray(AnnouncementsProperty);
        foreach (var announcement in document.Announcements.OrderBy(item => item.Game.ToString(), StringComparer.Ordinal)
            .ThenBy(item => item.Release))
        {
            writer.WriteStartObject();
            writer.WriteString("game", announcement.Game.ToString());
            writer.WriteString("release", announcement.Release.ToString("D"));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray(RepairAnnouncementsProperty);
        foreach (var announcement in document.RepairAnnouncements.OrderBy(item => item.Install)
            .ThenBy(item => item.Build, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("install", announcement.Install.ToString("D"));
            if (announcement.Build is { } build)
            {
                writer.WriteString("build", build);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static KeyValuePair<AttemptKey, AttemptEntry>? ReadAttempt(JsonElement attempt) =>
        GameKey.TryParse(attempt.GetTextOrNull("game"), out var game)
        && Guid.TryParse(attempt.GetTextOrNull("release"), out var release)
        && attempt.GetEnumOrNull<AutomationAction>("action") is { } action
        && Time(attempt, "at") is { } at
            ? new KeyValuePair<AttemptKey, AttemptEntry>(new AttemptKey(game, release, action), new AttemptEntry(at,
                attempt.GetOrNull("failures", JsonValueKind.Number) is { } count && count.TryGetInt32(out var failures) ? Math.Max(failures, 0) : 0,
                attempt.GetEnumOrNull<JobFailureReason>("reason"),
                attempt.GetTextOrNull("build")))
            : null;

    private static Announcement? ReadAnnouncement(JsonElement announcement) =>
        GameKey.TryParse(announcement.GetTextOrNull("game"), out var game) && Guid.TryParse(announcement.GetTextOrNull("release"), out var release)
            ? new Announcement(game, release)
            : null;

    private static RepairAnnouncement? ReadRepairAnnouncement(JsonElement announcement) =>
        Guid.TryParse(announcement.GetTextOrNull("install"), out var install)
            ? new RepairAnnouncement(install, announcement.GetTextOrNull("build"))
            : null;

    private static DateTimeOffset? Time(JsonElement parent, string name) =>
        parent.GetOrNull(name, JsonValueKind.String) is { } text && text.TryGetDateTimeOffset(out var time) ? time : null;

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        parent.GetOrNull(name, JsonValueKind.Array) is { } array ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object) : [];
}
