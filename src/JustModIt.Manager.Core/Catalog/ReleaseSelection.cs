using System.Collections.Frozen;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>Why nothing of a project can be installed on a game.</summary>
public enum SelectionUnavailability
{
    /// <summary>No release is public, or the project's status holds its releases back.</summary>
    NoPublicRelease,

    /// <summary>The public release has no finished file.</summary>
    NoReadyFiles,

    /// <summary>The package made for the game's store version and system comes in a format the app does not install.</summary>
    UnsupportedFormat,

    /// <summary>The packages are made for other store versions of the game.</summary>
    OtherStores,

    /// <summary>The packages for this store version are made for other systems than the game build's.</summary>
    OtherSystems,
}

/// <summary>
/// An optional part of a release with the package that fits the game, or a variant of a release from before add-ons, which
/// the player picks like one add-on of a group.
/// </summary>
public sealed record FeatureOffer
{
    /// <summary>The add-on's key, which stays when it is renamed; a variant's name for a release from before add-ons.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary>Plain text; empty for none.</summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// The caption of the choice that two or more listed add-ons make, of which exactly one is on, spelt as its first member
    /// spells it; null for an add-on on its own, and empty for the variants of a release from before add-ons, which the app
    /// names itself.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>The add-on comes with the release unless the player says otherwise; for a group, its member to recommend.</summary>
    public bool IsDefault { get; init; }

    /// <summary>The package that fits the game.</summary>
    public required ReleaseFile Package { get; init; }

    /// <summary>Every ready file of the add-on or variant, the package among them.</summary>
    public IReadOnlyList<ReleaseFile> Files { get; init; } = [];
}

/// <summary>One package of an install's plan.</summary>
/// <param name="FeatureKey">The add-on it installs, by key; null for the base and for the variant of a release from before add-ons.</param>
/// <param name="FeatureName">The add-on's name; null for the base.</param>
/// <param name="File">The release file to install.</param>
/// <param name="IsOptional">The install goes on without it when its own check fails on the game, as an add-on's does.</param>
public sealed record PlanPart(string? FeatureKey, string? FeatureName, ReleaseFile File, bool IsOptional);

/// <summary>What one install of a release installs, the base first and then the add-ons in the release's order.</summary>
/// <param name="Parts">The packages in the order they install, so a later one's file replaces an earlier one's.</param>
/// <param name="FeatureKeys">The add-ons that are on, by key.</param>
/// <param name="Variant">The variant a release from before add-ons installs, by its key; null for any other release.</param>
public sealed record ReleasePlan(IReadOnlyList<PlanPart> Parts, IReadOnlyList<string> FeatureKeys, string? Variant)
{
    /// <summary>The bytes of every package, as far as the site gives their sizes.</summary>
    public long Size => Parts.Sum(part => part.File.Size ?? 0);
}

/// <summary>
/// The player's choices and an install's record, which the plan of an install follows over the author's defaults.
/// </summary>
public sealed record FeatureChoices
{
    public static FeatureChoices None { get; } = new();

    /// <summary>The add-ons the player turned on or off, by key, only where that differs from the author's default.</summary>
    public IReadOnlyDictionary<string, bool> Chosen { get; init; } = FrozenDictionary<string, bool>.Empty;

    /// <summary>The variant of a release from before add-ons that the player chose before add-ons existed; null for none.</summary>
    public string? ChosenVariant { get; init; }

    /// <summary>The add-ons the install has, by key; empty for a new install.</summary>
    public IReadOnlyCollection<string> Installed { get; init; } = [];

    /// <summary>The variant of a release from before add-ons that the install has; null for none.</summary>
    public string? InstalledVariant { get; init; }

    /// <summary>
    /// The add-ons the install went without since they did not fit the game, by key; they stay off on the same release and
    /// game build, so nothing tries them again in a loop.
    /// </summary>
    public IReadOnlyCollection<string> LeftOut { get; init; } = [];

    /// <summary>The release <see cref="LeftOut"/> holds for; null when nothing was left out on this game build.</summary>
    public Guid? LeftOutRelease { get; init; }
}

/// <summary>What a project offers one game: the base and the add-ons that fit it.</summary>
public sealed record ReleaseSelection
{
    /// <summary>The public release; null when there is none to offer.</summary>
    public Release? Release { get; init; }

    /// <summary>The project's storefront links to the game; empty when the project no longer links it.</summary>
    public required IReadOnlyList<StorefrontLink> Links { get; init; }

    /// <summary>The base's package for the game; null when none fits, and for a release from before add-ons.</summary>
    public ReleaseFile? Base { get; init; }

    /// <summary>The add-ons that fit the game, or the variants of a release from before add-ons, in the release's order.</summary>
    public IReadOnlyList<FeatureOffer> Features { get; init; } = [];

    /// <summary>
    /// A release from before add-ons whose files make several variants, read as one group of which the player picks one.
    /// </summary>
    public bool IsLegacy { get; init; }

    /// <summary>Why nothing can be installed; null when something can.</summary>
    public SelectionUnavailability? Unavailable { get; init; }

    public bool IsInstallable => Unavailable is null;

    /// <summary>
    /// What an install takes: the base; each add-on on its own as the player turned it, else as its author says; one member
    /// of each group, the one the player chose, else the installed one, else the recommended one, else the first.
    /// </summary>
    /// <param name="choices">The player's choices and the install's record.</param>
    /// <returns>Null when nothing can be installed.</returns>
    public ReleasePlan? Plan(FeatureChoices choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (!IsInstallable)
        {
            return null;
        }

        var leftOut = choices.LeftOutRelease == Release?.Uuid ? choices.LeftOut.ToHashSet(StringComparer.Ordinal) : [];
        var on = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in Features.Where(feature => feature.Group is null && !leftOut.Contains(feature.Key)))
        {
            if (choices.Chosen.TryGetValue(feature.Key, out var chosen) ? chosen : feature.IsDefault)
            {
                on.Add(feature.Key);
            }
        }

        foreach (var members in Groups())
        {
            var fitting = members.Where(member => !leftOut.Contains(member.Key)).ToList();
            var member = fitting.FirstOrDefault(member => choices.Chosen.TryGetValue(member.Key, out var chosen) && chosen)
                ?? (IsLegacy ? fitting.FirstOrDefault(member => member.Key == choices.ChosenVariant) : null)
                ?? fitting.FirstOrDefault(member => IsLegacy ? member.Key == choices.InstalledVariant : choices.Installed.Contains(member.Key))
                ?? fitting.FirstOrDefault(member => member.IsDefault)
                ?? fitting.FirstOrDefault();
            if (member is not null)
            {
                on.Add(member.Key);
            }
        }

        var parts = new List<PlanPart>();
        if (Base is { } package)
        {
            parts.Add(new PlanPart(null, null, package, IsOptional: false));
        }

        // A release from before add-ons has no base, so its variant is what the install is for.
        parts.AddRange(Features.Where(feature => on.Contains(feature.Key)).Select(feature =>
            new PlanPart(IsLegacy ? null : feature.Key, feature.Name, feature.Package, IsOptional: !IsLegacy)));
        return new ReleasePlan(parts, IsLegacy ? [] : [.. Features.Where(feature => on.Contains(feature.Key)).Select(feature => feature.Key)],
            IsLegacy ? Features.FirstOrDefault(feature => on.Contains(feature.Key))?.Key : null);
    }

    /// <summary>
    /// The player's choices with some changed, keeping only what differs from what the plan takes without them: an add-on
    /// set back to its author's default is forgotten, and of a group only a member that would not be taken anyway is kept.
    /// </summary>
    /// <param name="chosen">The player's earlier choices for the project.</param>
    /// <param name="changes">The add-ons turned on or off by key; null forgets the choice.</param>
    /// <param name="installed">The add-ons and the variant the install has, which a group falls back to; empty for none.</param>
    public IReadOnlyDictionary<string, bool> Remember(IReadOnlyDictionary<string, bool> chosen, IReadOnlyDictionary<string, bool?> changes,
        IReadOnlyCollection<string> installed)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(installed);
        var result = new Dictionary<string, bool>(chosen, StringComparer.Ordinal);
        foreach (var (key, value) in changes)
        {
            var feature = Features.FirstOrDefault(feature => feature.Key == key);
            if (value is not { } on || feature is null)
            {
                result.Remove(key);
                continue;
            }

            if (feature.Group is null)
            {
                if (on == feature.IsDefault)
                {
                    result.Remove(key);
                }
                else
                {
                    result[key] = on;
                }

                continue;
            }

            // A member of a group is never turned off on its own, only by choosing another one.
            if (!on)
            {
                continue;
            }

            var members = Features.Where(member => member.Group == feature.Group).ToList();
            foreach (var member in members)
            {
                result.Remove(member.Key);
            }

            var taken = members.FirstOrDefault(member => installed.Contains(member.Key))
                ?? members.FirstOrDefault(member => member.IsDefault) ?? members[0];
            if (taken.Key != key)
            {
                result[key] = true;
            }
        }

        return result;
    }

    private IEnumerable<IReadOnlyList<FeatureOffer>> Groups() =>
        Features.Where(feature => feature.Group is not null)
            .GroupBy(feature => feature.Group!, StringComparer.Ordinal)
            .Select(group => (IReadOnlyList<FeatureOffer>)[.. group]);
}
