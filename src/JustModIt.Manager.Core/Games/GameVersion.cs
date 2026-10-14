using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Collections;

namespace JustModIt.Manager.Core.Games;

/// <summary>Which files of a game its store installed, as far as the store's records tell.</summary>
/// <param name="BuildId">The store's build; null when the store names none.</param>
/// <param name="Languages">The languages the store's records name for the install.</param>
/// <param name="Content">The installed content parts with their versions (Steam: depot@manifest); empty elsewhere.</param>
public sealed record GameVersion(
    string? BuildId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ValueList<string> Languages = default,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] ValueList<string> Content = default)
{
    /// <summary>
    /// The version of an original taken before the app recorded versions, which matches no version, itself included, so such an
    /// original never counts as fitting the game.
    /// </summary>
    public static GameVersion Unrecorded { get; } = new() { IsUnrecorded = true };

    /// <summary>No record told the version; see <see cref="Unrecorded"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsUnrecorded { get; init; }

    /// <summary>The version of the game's files its store reports now.</summary>
    /// <param name="game">The game as its store describes it.</param>
    public static GameVersion Of(InstalledGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return new GameVersion(game.BuildId, game.Languages, game.Content);
    }

    /// <summary>
    /// Whether both describe the same files: the same languages, and the same installed content where both list it, since a
    /// new build may change only parts the player does not have, otherwise the same build, unknown on both sides or neither;
    /// an unrecorded version matches none.
    /// </summary>
    /// <param name="other">The other version.</param>
    public bool Matches(GameVersion other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (IsUnrecorded || other.IsUnrecorded || !Languages.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(other.Languages))
        {
            return false;
        }

        return Content.Count > 0 && other.Content.Count > 0
            ? Content.ToHashSet(StringComparer.Ordinal).SetEquals(other.Content)
            : string.Equals(BuildId, other.BuildId, StringComparison.Ordinal);
    }
}
