namespace JustModIt.Manager.Localization;

/// <summary>The app's language: its culture, its order of titles and the formatting of its texts.</summary>
public static class Text
{
    private static volatile TextCatalog _catalog = TextCatalog.Base;

    /// <summary>Makes <paramref name="catalog"/> the app's texts and language; an app calls it once, at startup.</summary>
    /// <param name="catalog">The texts the app's build merged (<see cref="TextCatalog.Load"/>).</param>
    public static void Use(TextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    internal static string Lookup(string name) => _catalog[name];
}
