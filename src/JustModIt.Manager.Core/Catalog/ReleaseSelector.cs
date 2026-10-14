using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Picks the package of a release's base and of each of its add-ons for a game: one made for its store version, else for
/// its build's system, else a universal one; a release from before add-ons has its files grouped into variants as the
/// site's page groups them, and they become one group of which the player picks one.
/// </summary>
public static class ReleaseSelector
{
    /// <summary>What the project's public release offers the game.</summary>
    /// <param name="project">The project as the index has it.</param>
    /// <param name="game">The game's store and store id.</param>
    /// <param name="gameSystem">The system the installed build is made for.</param>
    public static ReleaseSelection Select(IndexEntry project, GameKey game, OperatingSystemKind gameSystem)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Select(project.StorefrontLinks, project.Status, project.PublicRelease, game, gameSystem);
    }

    /// <summary>What the project's public release offers the game.</summary>
    /// <param name="project">The project's page.</param>
    /// <param name="game">The game's store and store id.</param>
    /// <param name="gameSystem">The system the installed build is made for.</param>
    public static ReleaseSelection Select(ProjectPage project, GameKey game, OperatingSystemKind gameSystem)
    {
        ArgumentNullException.ThrowIfNull(project);
        return Select(project.StorefrontLinks, project.Status, project.PublicRelease, game, gameSystem);
    }

    /// <summary>
    /// The names of what a release lets the player choose: its add-ons with a finished file, or the variants of a release
    /// from before add-ons, as the site's page groups them.
    /// </summary>
    /// <param name="release">The release.</param>
    /// <returns>The names, and whether they are variants; none when there is nothing to choose.</returns>
    public static (IReadOnlyList<string> Names, bool AreVariants) FeatureNames(Release release)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Features is { Count: > 0 } features)
        {
            return ([.. features.Where(feature => feature.Files.Any(file => file.IsReady)).Select(feature => feature.Name)], false);
        }

        var variants = Variants(release.Files.Where(file => file.IsReady));
        return variants.Count > 1 ? ([.. variants.Select(variant => variant.Name).Distinct()], true) : ([], false);
    }

    /// <summary>What a release offers the game.</summary>
    /// <param name="projectLinks">Every storefront link of the project.</param>
    /// <param name="status">The project's status, which may hold releases back; null when unknown.</param>
    /// <param name="release">The public release; null when there is none.</param>
    /// <param name="game">The game's store and store id.</param>
    /// <param name="gameSystem">The system of the installed build, which Proton for one makes differ from the host's.</param>
    private static ReleaseSelection Select(IReadOnlyList<StorefrontLink> projectLinks, ProjectStatus? status, Release? release,
        GameKey game, OperatingSystemKind gameSystem)
    {
        ArgumentNullException.ThrowIfNull(projectLinks);
        var links = projectLinks.Where(link => link.GameKey == game).OrderBy(link => link.Position).ToList();
        // A backend that holds a release back already leaves it out for anonymous visitors; this covers one that does not.
        if (release is null || status?.AllowsPublicReleases == false)
        {
            return new ReleaseSelection { Links = links, Unavailable = SelectionUnavailability.NoPublicRelease };
        }

        var linkUuids = links.Select(link => link.Uuid).ToHashSet();
        var baseFiles = release.Files.Where(file => file.IsReady).ToList();
        if (release.Features is not { Count: > 0 } && Variants(baseFiles) is { Count: > 1 } variants)
        {
            return Legacy(release, links, variants, linkUuids, gameSystem);
        }

        var (package, unavailable) = baseFiles.Count == 0 ? (null, SelectionUnavailability.NoReadyFiles) : Choose(baseFiles, linkUuids, gameSystem);
        return new ReleaseSelection
        {
            Release = release,
            Links = links,
            Base = package,
            Features = package is null ? [] : Features(release.Features ?? [], linkUuids, gameSystem),
            Unavailable = unavailable,
        };
    }

    // An add-on without a package for the game is not offered for it; a group keeps its first member's spelling, and one
    // with a single listed member is an add-on on its own.
    private static List<FeatureOffer> Features(IReadOnlyList<ReleaseFeature> features, HashSet<Guid> links, OperatingSystemKind system)
    {
        var groups = features.Where(feature => feature.Group.Trim().Length > 0)
            .GroupBy(feature => feature.Group.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToDictionary(group => group.Key, group => group.First().Group.Trim(), StringComparer.OrdinalIgnoreCase);
        var offers = new List<FeatureOffer>();
        foreach (var feature in features)
        {
            var files = feature.Files.Where(file => file.IsReady).ToList();
            if (Choose(files, links, system).Package is { } package)
            {
                offers.Add(new FeatureOffer
                {
                    Key = feature.Key,
                    Name = feature.Name,
                    Description = feature.Description,
                    Group = groups.GetValueOrDefault(feature.Group.Trim()),
                    IsDefault = feature.IsDefault,
                    Package = package,
                    Files = files,
                });
            }
        }

        return offers;
    }

    // The variants that fit become one group, recommending the variant of the site's default file whenever it fits; a
    // variant's name is its key, which an install from before add-ons recorded as its choice.
    private static ReleaseSelection Legacy(Release release, List<StorefrontLink> links, List<(string Name, List<ReleaseFile> Files)> variants,
        HashSet<Guid> linkUuids, OperatingSystemKind system)
    {
        var chosen = variants.Select(variant => (variant.Name, variant.Files, Pick: Choose(variant.Files, linkUuids, system))).ToList();
        var installable = chosen.Where(variant => variant.Pick.Package is not null).ToList();
        var recommended = installable.FindIndex(variant => variant.Files.Exists(file => file.Uuid == release.DefaultFile?.Uuid));
        recommended = recommended >= 0 ? recommended : installable.FindIndex(variant => variant.Pick.Package!.Format == PackageFormat.Zip);
        recommended = recommended >= 0 ? recommended : 0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        return new ReleaseSelection
        {
            Release = release,
            Links = links,
            IsLegacy = true,
            Features = [.. installable.Select((variant, index) => new FeatureOffer
            {
                Key = UniqueKey(variant.Name, keys),
                Name = variant.Name,
                Description = variant.Files.Select(file => file.Description).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "",
                Group = "",
                IsDefault = index == recommended,
                Package = variant.Pick.Package!,
                Files = variant.Files,
            })],
            Unavailable = installable.Count > 0 ? null
                : chosen.Exists(variant => variant.Pick.Unavailable == SelectionUnavailability.UnsupportedFormat)
                    ? SelectionUnavailability.UnsupportedFormat
                : chosen.Exists(variant => variant.Pick.Unavailable == SelectionUnavailability.OtherSystems)
                    ? SelectionUnavailability.OtherSystems
                : SelectionUnavailability.OtherStores,
        };
    }

    // Older releases may give two variants one name; the first keeps it, so an install that recorded the name keeps its own.
    private static string UniqueKey(string name, HashSet<string> taken)
    {
        var key = name;
        for (var number = 2; !taken.Add(key); number++)
        {
            key = $"{name} ({number})";
        }

        return key;
    }

    /// <summary>A release's files grouped into variants as the site's page groups the files of a release from before add-ons.</summary>
    /// <param name="files">The release's ready files.</param>
    internal static List<(string Name, List<ReleaseFile> Files)> Variants(IEnumerable<ReleaseFile> files)
    {
        // A file whose coverage overlaps one already in its name's variant starts a variant of its own: older releases
        // reused one name for what were really two choices.
        var variants = new List<(string Name, List<ReleaseFile> Files)>();
        foreach (var file in files)
        {
            var home = variants.FindIndex(variant =>
                variant.Name == file.Name && !variant.Files.Exists(other => CoverageOverlaps(file, other)));
            if (home < 0)
            {
                variants.Add((file.Name, [file]));
            }
            else
            {
                variants[home].Files.Add(file);
            }
        }

        return variants;
    }

    private static bool CoverageOverlaps(ReleaseFile first, ReleaseFile second) =>
        Meets(first.StorefrontLinkUuids, second.StorefrontLinkUuids) && Meets(first.OperatingSystems, second.OperatingSystems);

    // An empty list covers everything on its axis, yet a universal file next to a pinned one is how a variant says "this
    // store's own build, and the universal one for everybody else", so only two empty lists meet.
    private static bool Meets<T>(IReadOnlyList<T> first, IReadOnlyList<T> second) =>
        first.Count == 0 || second.Count == 0 ? first.Count == 0 && second.Count == 0 : first.Any(second.Contains);

    private static (ReleaseFile? Package, SelectionUnavailability? Unavailable) Choose(List<ReleaseFile> files, HashSet<Guid> links,
        OperatingSystemKind system)
    {
        var forStore = files.Where(file => file.StorefrontLinkUuids.Count == 0 || file.StorefrontLinkUuids.Any(links.Contains)).ToList();
        // A file that the best one outranks is meant for other store versions or systems, so it never stands in for a
        // best one the app cannot install.
        var best = forStore
            .Where(file => file.OperatingSystems.Count == 0
                || (system != OperatingSystemKind.Unknown && file.OperatingSystems.Contains(system)))
            .OrderByDescending(file => file.StorefrontLinkUuids.Count > 0)
            .ThenByDescending(file => file.OperatingSystems.Count > 0)
            .FirstOrDefault();
        var package = best?.Format is not null ? best : null;
        return (package, package is not null ? null
            : best is not null ? SelectionUnavailability.UnsupportedFormat
            : forStore.Count > 0 ? SelectionUnavailability.OtherSystems
            : SelectionUnavailability.OtherStores);
    }
}
