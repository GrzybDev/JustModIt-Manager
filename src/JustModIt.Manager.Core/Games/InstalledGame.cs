using JustModIt.Manager.Core.Collections;

namespace JustModIt.Manager.Core.Games;

/// <summary>A game a store or launcher installed on this machine, as the store's own files describe it.</summary>
public sealed record InstalledGame
{
    public required GameKey Key { get; init; }

    /// <summary>The store's name for the game; its id when the store names none.</summary>
    public required string Name { get; init; }

    /// <summary>The canonical folder of the game.</summary>
    public required string InstallDirectory { get; init; }

    /// <summary>The library the game was found in (<see cref="Library.LibraryLocation.Id"/>).</summary>
    public required string LibraryId { get; init; }

    /// <summary>The store's build of the installed files; text, because GOG build ids exceed 2^53.</summary>
    public string? BuildId { get; init; }

    public GameInstallState State { get; init; }

    public GameStateReason Reason { get; init; }

    /// <summary>When the store last wrote its records of the game, after which changing files waits for a quiet period.</summary>
    public DateTimeOffset? StoreActivityAt { get; init; }

    /// <summary>The operating system the installed build is made for.</summary>
    public OperatingSystemKind Platform { get; init; }

    /// <summary>
    /// Language tags (<c>pl</c>, <c>pt-BR</c>) that the store's files name for this install, each offered officially, though
    /// usually just the one the game was installed in, so a missing language may still be offered.
    /// </summary>
    public ValueList<string> Languages { get; init; }

    /// <summary>
    /// Kept from an earlier scan because the store's records could not be read this time; not trusted for writing.
    /// </summary>
    public bool IsStale { get; init; }

    public bool IsReady => State == GameInstallState.Installed && !IsStale;

    /// <summary>This game as it is kept while its library is offline, such as on an unplugged drive.</summary>
    public InstalledGame AsOffline() =>
        this with { State = GameInstallState.Offline, Reason = GameStateReason.LibraryOffline, IsStale = false };

    /// <summary>Whether <see cref="Languages"/> names <paramref name="language"/> (<c>pl</c>) in any region.</summary>
    /// <param name="language">An ISO 639-1 code without a region.</param>
    public bool HasLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return Languages.Any(tag => tag.Equals(language, StringComparison.OrdinalIgnoreCase)
            || (tag.StartsWith(language, StringComparison.OrdinalIgnoreCase) && tag.Length > language.Length
                && tag[language.Length] == '-'));
    }
}
