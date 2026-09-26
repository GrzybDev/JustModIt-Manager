namespace JustModIt.Manager.Core.Games;

/// <summary>
/// A game as its store identifies it, e.g. a Steam AppID or a GOG product id, whose text form <c>steam:620</c> keys
/// settings and install records.
/// </summary>
public readonly record struct GameKey
{
    private const char Separator = ':';

    // Text, since GOG ids exceed 2^53 and must never pass through a floating-point number.
    private readonly string? _storeGameId;

    /// <summary>Creates the key of a game in a store.</summary>
    /// <param name="store">The store; never the default value.</param>
    /// <param name="storeGameId">The store's id for the game, as text.</param>
    public GameKey(StoreId store, string storeGameId)
    {
        if (store.Value.Length == 0)
        {
            throw new ArgumentException("A game key needs a store.", nameof(store));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(storeGameId);
        Store = store;
        _storeGameId = storeGameId;
    }

    public StoreId Store { get; }

    public string StoreGameId => _storeGameId ?? string.Empty;

    /// <summary>Reads a key from its text form, e.g. <c>steam:620</c>; false when the text is none.</summary>
    /// <param name="text">The text form; null reads as no key.</param>
    /// <param name="key">The key read; the default value when there is none.</param>
    public static bool TryParse(string? text, out GameKey key)
    {
        key = default;
        var separator = text?.IndexOf(Separator, StringComparison.Ordinal) ?? -1;
        if (separator < 0 || !StoreId.TryParse(text![..separator], out var store) || string.IsNullOrWhiteSpace(text[(separator + 1)..]))
        {
            return false;
        }

        key = new GameKey(store, text[(separator + 1)..]);
        return true;
    }

    public override string ToString() => $"{Store}{Separator}{StoreGameId}";
}
