using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.Jobs;

/// <summary>
/// What an install of a project takes from the player's choices and from the install's record, which the state, the
/// jobs and the window's view all plan with, so they never disagree.
/// </summary>
public static class InstallChoices
{
    /// <summary>The choices a plan of a project on a game follows.</summary>
    /// <param name="preferences">The player's choices for the game.</param>
    /// <param name="project">The project's id.</param>
    /// <param name="record">The project's install on the game; null for a new install.</param>
    /// <param name="gameBuild">The game's build now, against which the add-ons the install went without hold.</param>
    public static FeatureChoices For(GamePreferences preferences, Guid project, InstallRecord? record, string? gameBuild)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var chosen = preferences.FeaturesOf(project);
        // A variant chosen before add-ons existed was the game's choice, which only its project's variants follow.
        var variant = chosen.Count == 0 && (preferences.ProjectId is null || preferences.ProjectId == project) ? preferences.VariantName : null;
        var leftOut = record is { LeftOutFeatures: { Count: > 0 } features } && record.GameBuildId == gameBuild ? features : null;
        return new FeatureChoices
        {
            Chosen = chosen,
            ChosenVariant = variant,
            Installed = record is null ? [] : [.. FeaturesOf(record).Select(feature => feature.Key)],
            InstalledVariant = record?.Source?.VariantName,
            LeftOut = leftOut is null ? [] : [.. leftOut.Select(feature => feature.Key)],
            LeftOutRelease = leftOut is null ? null : record!.Source?.ReleaseUuid,
        };
    }

    /// <summary>The release and packages an install came from; null when its record does not name them.</summary>
    /// <param name="record">The install's record.</param>
    public static InstalledRelease? InstalledReleaseOf(InstallRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Source is not { ReleaseUuid: { } release } source)
        {
            return null;
        }

        var files = record.Parts is { } parts
            ? parts.Select(part => Guid.TryParse(part.ReleaseFileId, out var file) ? file : (Guid?)null).OfType<Guid>().ToList()
            : source.ReleaseFileUuid is { } single ? [single] : [];
        if (files.Count == 0)
        {
            return null;
        }

        return new InstalledRelease(release, source.ReleaseVersion ?? 0, source.ReleasePublishedAt, files)
        {
            Downloads = [.. record.Parts?.Select(part => Guid.TryParse(part.CachedFileId, out var cached) ? cached : (Guid?)null).OfType<Guid>() ?? []],
            Features = FeaturesOf(record),
            Variant = source.VariantName,
            HadVariantChoice = source.HadVariantChoice,
            PredatesFeatures = record.Parts is null || source.VariantName is not null,
        };
    }

    /// <summary>The add-ons an install has, in the order they installed.</summary>
    /// <param name="record">The install's record.</param>
    public static IReadOnlyList<InstalledFeature> FeaturesOf(InstallRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return [.. (record.Parts ?? []).Where(part => part.FeatureKey is not null)
            .Select(part => new InstalledFeature(part.FeatureKey!, part.FeatureName ?? part.FeatureKey!))];
    }
}
