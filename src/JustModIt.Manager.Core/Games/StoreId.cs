namespace JustModIt.Manager.Core.Games;

/// <summary>
/// A storefront, as an open set rather than an enum, because the catalogue already names stores the app cannot detect
/// yet.
/// </summary>
public readonly record struct StoreId
{
    private readonly string? _value;

    /// <summary>Creates a store id from its text.</summary>
    /// <param name="value">Letters, digits and hyphens; stored in lower case.</param>
    public StoreId(string value)
    {
        if (!TryNormalize(value, out var normalized))
        {
            throw new ArgumentException($"'{value}' is not a valid store id.", nameof(value));
        }

        _value = normalized;
    }

    public static StoreId Steam { get; } = new("steam");

    public static StoreId Gog { get; } = new("gog");

    /// <summary>The Epic Games Store, whose games are known by the catalog namespace of the base game.</summary>
    public static StoreId Epic { get; } = new("epic");

    public string Value => _value ?? string.Empty;

    /// <summary>Reads a store id; false when the text is none.</summary>
    /// <param name="text">Letters, digits and hyphens in any case; null reads as no id.</param>
    /// <param name="store">The id read; the default value when there is none.</param>
    public static bool TryParse(string? text, out StoreId store)
    {
        store = TryNormalize(text, out var normalized) ? new StoreId(normalized) : default;
        return store.Value.Length > 0;
    }

    public override string ToString() => Value;

    private static bool TryNormalize(string? text, out string normalized)
    {
        normalized = text?.ToLowerInvariant() ?? string.Empty;
        return normalized.Length > 0 && normalized.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }
}
