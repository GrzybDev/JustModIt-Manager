using System.Collections.Immutable;
using System.Text.Json.Serialization;
using JustModIt.Manager.Core.Collections;

namespace JustModIt.Manager.Core.Settings;

/// <summary>The player's choices for one game, stored only for games whose choices differ from these defaults.</summary>
public sealed record GamePreferences
{
    public static GamePreferences Default { get; } = new();

    /// <summary>Null follows <see cref="AppSettings.AutoInstallProjects"/>.</summary>
    public bool? AutoInstall { get; init; }

    /// <summary>Null follows <see cref="AppSettings.AutoUpdateProjects"/>.</summary>
    public bool? AutoUpdate { get; init; }

    /// <summary>The project to use when several teams made one for the game.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// The variant of a release from before add-ons that the player chose before add-ons existed, by its name; read from
    /// older settings and never written, it stands for the choice of <see cref="ProjectId"/>'s variants until the player
    /// chooses its add-ons.
    /// </summary>
    public string? VariantName { get; init; }

    /// <summary>
    /// The add-ons the player turned on or off, by project and add-on key, only where that differs from the author's
    /// default; the window learns what they lead to from the projects' plans instead.
    /// </summary>
    [JsonIgnore]
    public ValueDictionary<Guid, ValueDictionary<string, bool>> Features { get; init; }

    /// <summary>Hidden from the player's list of games and left alone by the automation.</summary>
    public bool IsHidden { get; init; }

    /// <summary>The player's add-on choices for one project.</summary>
    /// <param name="project">The project's id.</param>
    public IReadOnlyDictionary<string, bool> FeaturesOf(Guid project) =>
        Features.TryGetValue(project, out var chosen) ? chosen : new ValueDictionary<string, bool>();

    /// <summary>The preferences with one project's add-on choices replaced, a project without any dropped.</summary>
    /// <param name="project">The project's id.</param>
    /// <param name="chosen">The add-ons turned on or off, only where that differs from the author's default.</param>
    public GamePreferences WithFeatures(Guid project, IReadOnlyDictionary<string, bool> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        return this with
        {
            Features = chosen.Count == 0
                ? Features.Remove(project)
                : Features.SetItem(project, new ValueDictionary<string, bool>(chosen.ToImmutableDictionary(StringComparer.Ordinal))),
        };
    }
}
