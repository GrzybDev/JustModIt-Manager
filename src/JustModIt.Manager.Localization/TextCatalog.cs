using System.Collections;
using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace JustModIt.Manager.Localization;

/// <summary>The texts of one language, which <see cref="Strings"/> reads once <see cref="Text.Use"/> makes them current.</summary>
public sealed class TextCatalog
{
    /// <summary>The name under which an app's build embeds its merged texts (<c>Texts.targets</c>).</summary>
    public const string ResourceName = "JustModIt.Manager.Texts.resources";

    private const string BaseResourceName = "JustModIt.Manager.Localization.Resources.Strings.resources";

    private static readonly Lazy<TextCatalog> LazyBase = new(() => Read(string.Empty, typeof(TextCatalog).Assembly, BaseResourceName));

    private readonly FrozenDictionary<string, string> _texts;

    /// <summary>Creates the catalog of one language.</summary>
    /// <param name="language">An IETF language tag; empty means English.</param>
    /// <param name="texts">The texts by key; a missing one falls back to the English base.</param>
    public TextCatalog(string language, IEnumerable<KeyValuePair<string, string>> texts)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(texts);
        Culture = CultureInfo.GetCultureInfo(language.Length == 0 ? "en" : language);
        _texts = texts.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The platform's English texts, which every other catalog falls back to.</summary>
    public static TextCatalog Base => LazyBase.Value;

    /// <summary>The culture that numbers, dates and the order of titles follow.</summary>
    public CultureInfo Culture { get; }

    internal IReadOnlyDictionary<string, string> Texts => _texts;

    /// <summary>The text of a key, or the English base's when this catalog lacks it.</summary>
    /// <param name="name">The key in <c>Strings.resx</c>.</param>
    public string this[string name] => _texts.TryGetValue(name, out var text) ? text : Base._texts[name];

    /// <summary>Reads the texts that an app's build merged into its assembly.</summary>
    /// <param name="language">The app's UI language, an IETF language tag; empty means English.</param>
    /// <param name="assembly">The app's own assembly.</param>
    public static TextCatalog Load(string language, Assembly assembly) => Read(language, assembly, ResourceName);

    private static TextCatalog Read(string language, Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new MissingManifestResourceException($"{assembly.FullName} has no {resourceName}.");
        using var reader = new ResourceReader(stream);
        var texts = new List<KeyValuePair<string, string>>();
        foreach (DictionaryEntry entry in reader)
        {
            texts.Add(new((string)entry.Key, (string)entry.Value!));
        }

        return new TextCatalog(language, texts);
    }
}
