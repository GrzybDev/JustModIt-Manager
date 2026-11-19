using System.Buffers;
using System.Text.Json;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Agent.Steam;

/// <summary>
/// The texts file the app writes into its Millennium plugin's folder: the achievement texts, the panel's words and the games
/// whose launch options to report.
/// </summary>
internal static class PluginTexts
{
    /// <summary>The file's content.</summary>
    /// <param name="demand">What Steam's interface is to get.</param>
    /// <param name="options">The app's name, version and link scheme, which the panel tells.</param>
    /// <param name="zone">The time zone the panel's dates are in.</param>
    public static byte[] Write(SteamUiDemand demand, AgentOptions options, TimeZoneInfo zone)
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
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return content.WrittenSpan.ToArray();
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
