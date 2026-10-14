using JustModIt.Manager.Core.Collections;

namespace JustModIt.Manager.Core.Catalog;

/// <summary>An add-on an install has, named as it was when it was installed.</summary>
/// <param name="Key">The add-on's key.</param>
/// <param name="Name">Its name, shown offline.</param>
public sealed record InstalledFeature(string Key, string Name);

/// <summary>The release and packages an install used, as its record keeps them.</summary>
/// <param name="ReleaseUuid">The installed release.</param>
/// <param name="ReleaseVersion">The release's number within the project; 0 when the site gave none.</param>
/// <param name="PublishedAt">When the release went public; null when the site did not say.</param>
/// <param name="Files">The release files the install's packages are.</param>
public sealed record InstalledRelease(Guid ReleaseUuid, int ReleaseVersion, DateTimeOffset? PublishedAt, IReadOnlyList<Guid> Files)
{
    /// <summary>The kept downloads the packages came from, when a package is a copy of another release's file.</summary>
    public IReadOnlyList<Guid> Downloads { get; init; } = [];

    /// <summary>The add-ons the install has.</summary>
    public IReadOnlyList<InstalledFeature> Features { get; init; } = [];

    /// <summary>The variant of a release from before add-ons the install has; null for none.</summary>
    public string? Variant { get; init; }

    /// <summary>
    /// The install came from a release that offered several variants that fit the game; null when its record does not say,
    /// which an install from before add-ons never does.
    /// </summary>
    public bool? HadVariantChoice { get; init; }

    /// <summary>The install was made before add-ons, or from the variants of a release from before them.</summary>
    public bool PredatesFeatures { get; init; }

    /// <summary>Whether the install's packages already hold a release file, itself or the file it copies.</summary>
    /// <param name="file">A file of a release's plan.</param>
    public bool Holds(ReleaseFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Has(file.Uuid) || (file.CopiedFrom is { } original && Has(original));
    }

    private bool Has(Guid file) => Files.Contains(file) || Downloads.Contains(file);
}

public enum UpdateState
{
    UpToDate,

    /// <summary>A newer release, or a changed set of packages in the same release, fits the game.</summary>
    Available,

    /// <summary>The site made an older release public again, which is never offered as an update; reinstalling is up to the player.</summary>
    RolledBack,

    /// <summary>The current release has nothing for the game's store version or system, so it is no update for this player.</summary>
    NotForThisGame,

    /// <summary>
    /// Nothing is public any more, since the release was withdrawn or held back; the install stays and can still be removed.
    /// </summary>
    Withdrawn,
}

public enum UpdateKind
{
    NewRelease,

    /// <summary>The team replaced a package within the same release, or changed which add-ons it installs by default.</summary>
    ReplacedPackage,
}

/// <summary>Whether an install has an update, and what to install for it.</summary>
/// <param name="State">Whether an update fits the game, or why there is none.</param>
/// <param name="Kind">What kind of update is available; null when none is.</param>
/// <param name="Plan">What the update installs; null when none is available.</param>
public sealed record UpdateCheck(UpdateState State, UpdateKind? Kind = null, ReleasePlan? Plan = null)
{
    /// <summary>The add-ons the install has that the update goes without, though the player did not turn them off.</summary>
    public ValueList<InstalledFeature> Drops { get; init; }

    /// <summary>
    /// Only the player may install what the release offers now: it goes without an add-on the install has, the install
    /// predates add-ons and the player has not chosen any, or the variant it has is gone.
    /// </summary>
    public bool WaitsForPlayer { get; init; }

    /// <summary>The bytes of the plan's packages that the install does not hold already.</summary>
    public long Size { get; init; }
}

public static class ReleaseUpdates
{
    /// <summary>Whether the project's public release, as the player's choices take it, is an update of what is installed.</summary>
    /// <param name="installed">The release and packages the install's record keeps.</param>
    /// <param name="selection">What the project offers the game now.</param>
    /// <param name="choices">The player's choices and the add-ons the install went without, which the plan follows.</param>
    public static UpdateCheck Check(InstalledRelease installed, ReleaseSelection selection, FeatureChoices choices)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(choices);
        if (selection.Release is not { } release)
        {
            return new UpdateCheck(UpdateState.Withdrawn);
        }

        choices = choices with { Installed = [.. installed.Features.Select(feature => feature.Key)], InstalledVariant = installed.Variant };

        var plan = selection.Plan(choices);
        var sameRelease = release.Uuid == installed.ReleaseUuid;
        if (sameRelease && installed.Files.All(file => IsReady(release, file)) && (plan is null || TakesInstalled(plan, installed)))
        {
            return new UpdateCheck(UpdateState.UpToDate);
        }

        if (!sameRelease && IsOlder(release, installed))
        {
            return new UpdateCheck(UpdateState.RolledBack);
        }

        if (plan is null)
        {
            return new UpdateCheck(UpdateState.NotForThisGame);
        }

        ValueList<InstalledFeature> drops = [.. installed.Features.Where(feature => !plan.FeatureKeys.Contains(feature.Key)
            && !TurnedOff(selection, choices, feature.Key))];
        return new UpdateCheck(UpdateState.Available, sameRelease ? UpdateKind.ReplacedPackage : UpdateKind.NewRelease, plan)
        {
            Drops = drops,
            WaitsForPlayer = drops.Count > 0 || GainsFeatures(installed, selection, plan, choices)
                || LostVariant(installed, selection, plan, choices),
            Size = plan.Parts.Where(part => !installed.Holds(part.File)).Sum(part => part.File.Size ?? 0),
        };
    }

    // A backend that does not say which add-ons a release has is checked by the base's file alone.
    private static bool IsReady(Release release, Guid file) =>
        release.Files.Concat(release.Features?.SelectMany(feature => feature.Files) ?? []).Any(entry => entry.Uuid == file && entry.IsReady);

    private static bool TakesInstalled(ReleasePlan plan, InstalledRelease installed) =>
        plan.Variant is { } variant
            ? variant == installed.Variant
            : plan.FeatureKeys.Order(StringComparer.Ordinal)
                .SequenceEqual(installed.Features.Select(feature => feature.Key).Order(StringComparer.Ordinal));

    // The player turned the add-on off, or chose another member of its group.
    private static bool TurnedOff(ReleaseSelection selection, FeatureChoices choices, string key) =>
        choices.Chosen.TryGetValue(key, out var on) ? !on
        : selection.Features.FirstOrDefault(feature => feature.Key == key)?.Group is { } group
            && selection.Features.Any(member => member.Group == group && choices.Chosen.TryGetValue(member.Key, out var chosen) && chosen);

    // An install from before add-ons is told once of what the release now offers, and never changed without the player.
    private static bool GainsFeatures(InstalledRelease installed, ReleaseSelection selection, ReleasePlan plan, FeatureChoices choices) =>
        installed.PredatesFeatures && !selection.IsLegacy && plan.FeatureKeys.Count > 0 && choices.Chosen.Count == 0;

    // A variant the player had a choice of is never swapped for another without the player, e.g. text and voice for text only.
    private static bool LostVariant(InstalledRelease installed, ReleaseSelection selection, ReleasePlan plan, FeatureChoices choices)
    {
        if (installed.HadVariantChoice == false || installed.Variant is not { } variant)
        {
            return false;
        }

        if (!selection.IsLegacy)
        {
            return selection.Base?.Name != variant;
        }

        var chosen = plan.Variant is { } taken && (choices.Chosen.ContainsKey(taken) || choices.ChosenVariant == taken);
        return !chosen && selection.Features.All(feature => feature.Key != variant);
    }

    // Release numbers only grow within a project; publication dates decide for a backend that numbers nothing.
    private static bool IsOlder(Release release, InstalledRelease installed) =>
        release.Version > 0 && installed.ReleaseVersion > 0
            ? release.Version < installed.ReleaseVersion
            : release.FirstPublishedAt < installed.PublishedAt;
}
