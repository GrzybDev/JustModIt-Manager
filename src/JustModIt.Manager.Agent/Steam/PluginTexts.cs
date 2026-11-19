using System.Buffers;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>A change of a game's launch options that the plugin makes the next time it reads the texts file.</summary>
/// <param name="Id">Grows with every change, so the plugin makes each once, in order, and reports how far it got.</param>
/// <param name="Game">The Steam game.</param>
/// <param name="Add">The options to add where they are missing.</param>
/// <param name="Remove">The options to take out where they are present.</param>
internal sealed record PendingLaunchOptions(long Id, GameKey Game, ValueList<string> Add, ValueList<string> Remove);

/// <summary>
/// The texts file the app writes into its Millennium plugin's folder: the achievement texts, the panel's words, the games
/// whose launch options to report and the changes of them to make.
/// </summary>
internal static class PluginTexts
{
    /// <summary>The file's content.</summary>
    /// <param name="demand">What Steam's interface is to get.</param>
    /// <param name="changes">The changes of launch options the plugin has not made yet.</param>
    /// <param name="options">The app's name, version and link scheme, which the panel tells.</param>
    /// <param name="zone">The time zone the panel's dates are in.</param>
    public static byte[] Write(SteamUiDemand demand, IEnumerable<PendingLaunchOptions> changes, AgentOptions options, TimeZoneInfo zone)
    {
        var content = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(content, AtomicFile.ReadableJson))
        {
            writer.WriteStartObject();
            writer.WriteString("language", demand.Language);
            writer.WritePropertyName("texts");
            writer.WriteRawValue(demand.Texts.Span, skipInputValidation: true);
            WritePanel(writer, demand, options, zone);
            writer.WriteStartObject("launchOptions");
            WriteArray(writer, "games", demand.LaunchOptionsGames.Select(game => game.StoreGameId));
            writer.WriteStartArray("changes");
            foreach (var change in changes)
            {
                writer.WriteStartObject();
                writer.WriteNumber("id", change.Id);
                writer.WriteString("app", change.Game.StoreGameId);
                WriteArray(writer, "add", change.Add);
                WriteArray(writer, "remove", change.Remove);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return content.WrittenSpan.ToArray();
    }

    /// <summary>The changes of launch options a texts file lists, so those not made yet outlive the agent.</summary>
    /// <param name="content">The file's content.</param>
    public static IReadOnlyList<PendingLaunchOptions> ReadChanges(byte[] content)
    {
        try
        {
            using var file = JsonDocument.Parse(content);
            return file.RootElement.GetOrNull("launchOptions", JsonValueKind.Object)?.GetItems("changes")
                .Select(change => change.GetInt64OrNull("id") is { } id && change.GetTextOrNull("app") is { Length: > 0 } app
                    ? new PendingLaunchOptions(id, new GameKey(StoreId.Steam, app), [.. change.GetStrings("add")], [.. change.GetStrings("remove")])
                    : null)
                .OfType<PendingLaunchOptions>().ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void WriteArray(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void WritePanel(Utf8JsonWriter writer, SteamUiDemand demand, AgentOptions options, TimeZoneInfo zone)
    {
        var product = ("product", (object)options.ProductName);
        var games = Text.Format(Strings.GameCount, ("count", demand.Games));
        writer.WriteStartObject("panel");
        writer.WriteString("title", Text.Format(Strings.SteamPlugin_Name, product));
        writer.WriteString("subtitle", Text.Format(Strings.SteamPlugin_InstalledBy, product, ("version", options.Version)));

        writer.WriteStartObject("texts");
        writer.WriteString("label", Strings.SteamPlugin_TextsLabel);
        writer.WriteString("value", demand.Games == 0 ? Strings.SteamPlugin_TextsNone
            : demand.UpdatedAt is { } updated ? Text.Format(Strings.SteamPlugin_TextsValue, ("games", games), ("date", Formats.Date(updated, zone)))
            : games);
        writer.WriteString("working", Strings.SteamPlugin_Working);
        writer.WriteString("blocked", Strings.SteamPlugin_Blocked);
        writer.WriteEndObject();

        writer.WriteStartObject("language");
        writer.WriteString("label", Strings.SteamPlugin_LanguageLabel);
        writer.WriteString("matches", Strings.SteamPlugin_LanguageMatches);
        writer.WriteString("matchesStatus", Strings.SteamPlugin_LanguageMatchesStatus);
        writer.WriteString("other", Strings.SteamPlugin_LanguageOther);
        writer.WriteString("otherStatus", Strings.SteamPlugin_LanguageOtherStatus);
        writer.WriteEndObject();

        writer.WriteStartObject("manage");
        writer.WriteString("label", Strings.SteamPlugin_ManageLabel);
        writer.WriteString("value", Text.Format(Strings.SteamPlugin_ManageValue, product));
        writer.WriteString("open", Text.Format(Strings.SteamPlugin_Open, product));
        writer.WriteString("link", new AppLinks(options.Identity.Slug).For(new LibraryTarget()));
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}
