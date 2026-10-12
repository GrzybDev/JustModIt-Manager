using JustModIt.Manager.Core.Games;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// Groups a release's ready files into variants as the site's page does and picks each variant's package for a game: one
/// made for its store version, else for its build's system, else a universal one.
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

    /// <summary>What a release offers the game, recommending the variant of the site's default file whenever it fits.</summary>
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
            return new ReleaseSelection { Links = links, Variants = [], Unavailable = SelectionUnavailability.NoPublicRelease };
        }

        var linkUuids = links.Select(link => link.Uuid).ToHashSet();
        var variants = GroupVariants(release.Files.Where(file => file.IsReady))
            .Select(variant => Choose(variant.Name, variant.Files, linkUuids, gameSystem))
            .ToList();
        var installable = variants.Where(variant => variant.IsInstallable).ToList();
        var recommended = installable.FirstOrDefault(variant => variant.Files.Any(file => file.Uuid == release.DefaultFile?.Uuid))
            ?? installable.FirstOrDefault(variant => variant.Package!.Format == PackageFormat.Zip)
            ?? installable.FirstOrDefault();
        return new ReleaseSelection
        {
            Release = release,
            Links = links,
            Variants = variants,
            Default = recommended,
            Unavailable = recommended is not null ? null
                : variants.Count == 0 ? SelectionUnavailability.NoReadyFiles
                : variants.Exists(variant => variant.Unavailable == SelectionUnavailability.UnsupportedFormat)
                    ? SelectionUnavailability.UnsupportedFormat
                : variants.Exists(variant => variant.Unavailable == SelectionUnavailability.OtherSystems)
                    ? SelectionUnavailability.OtherSystems
                : SelectionUnavailability.OtherStores,
        };
    }

    /// <summary>The names of a release's variants, of its ready files grouped as the site's page groups them.</summary>
    /// <param name="release">The release.</param>
    public static IReadOnlyList<string> VariantNames(Release release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return [.. GroupVariants(release.Files.Where(file => file.IsReady)).Select(variant => variant.Name).Distinct()];
    }

    // A file whose coverage overlaps one already in its name's variant starts a variant of its own: older releases reused
    // one name for what were really two choices.
    private static List<(string Name, List<ReleaseFile> Files)> GroupVariants(IEnumerable<ReleaseFile> files)
    {
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

    private static ReleaseVariant Choose(string name, List<ReleaseFile> files, HashSet<Guid> links, OperatingSystemKind system)
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
        return new ReleaseVariant
        {
            Name = name,
            Files = files,
            Package = package,
            Unavailable = package is not null ? null
                : best is not null ? SelectionUnavailability.UnsupportedFormat
                : forStore.Count > 0 ? SelectionUnavailability.OtherSystems
                : SelectionUnavailability.OtherStores,
        };
    }
}
