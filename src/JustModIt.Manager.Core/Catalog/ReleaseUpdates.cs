namespace JustModIt.Manager.Core.Catalog;

/// <summary>The release and package an install used, as its record keeps them.</summary>
/// <param name="ReleaseUuid">The installed release.</param>
/// <param name="ReleaseVersion">The release's number within the project; 0 when the site gave none.</param>
/// <param name="PublishedAt">When the release went public; null when the site did not say.</param>
/// <param name="FileUuid">The installed package.</param>
/// <param name="VariantName">The variant the player chose, so an update keeps it; null when the install does not say.</param>
public sealed record InstalledRelease(Guid ReleaseUuid, int ReleaseVersion, DateTimeOffset? PublishedAt, Guid FileUuid,
    string? VariantName);

public enum UpdateState
{
    UpToDate,

    /// <summary>A newer release, or a corrected package in the same release, fits the game.</summary>
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

    /// <summary>The team replaced the installed package within the same release.</summary>
    ReplacedPackage,
}

/// <summary>Whether an install has an update, and what to install for it.</summary>
/// <param name="State">Whether an update fits the game, or why there is none.</param>
/// <param name="Kind">What kind of update is available; null when none is.</param>
/// <param name="Variant">What to install for an update; the remembered variant unless <paramref name="VariantFellBack"/>.</param>
/// <param name="VariantFellBack">The remembered variant is gone, so the recommended one stands in.</param>
public sealed record UpdateCheck(UpdateState State, UpdateKind? Kind = null, ReleaseVariant? Variant = null,
    bool VariantFellBack = false);

public static class ReleaseUpdates
{
    /// <summary>Whether the project's public release is an update of what is installed.</summary>
    /// <param name="installed">The release and package the install's record keeps.</param>
    /// <param name="selection">What the project offers the game now.</param>
    public static UpdateCheck Check(InstalledRelease installed, ReleaseSelection selection)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Release is not { } release)
        {
            return new UpdateCheck(UpdateState.Withdrawn);
        }

        var sameRelease = release.Uuid == installed.ReleaseUuid;
        if (sameRelease && release.Files.Any(file => file.Uuid == installed.FileUuid && file.IsReady))
        {
            return new UpdateCheck(UpdateState.UpToDate);
        }

        if (!sameRelease && IsOlder(release, installed))
        {
            return new UpdateCheck(UpdateState.RolledBack);
        }

        var (variant, fellBack) = selection.Choose(installed.VariantName);
        return variant is null
            ? new UpdateCheck(UpdateState.NotForThisGame)
            : new UpdateCheck(UpdateState.Available, sameRelease ? UpdateKind.ReplacedPackage : UpdateKind.NewRelease, variant, fellBack);
    }

    // Release numbers only grow within a project; publication dates decide for a backend that numbers nothing.
    private static bool IsOlder(Release release, InstalledRelease installed) =>
        release.Version > 0 && installed.ReleaseVersion > 0
            ? release.Version < installed.ReleaseVersion
            : release.PublishedAt < installed.PublishedAt;
}
