using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Creator.Planning;

namespace JustModIt.Manager.Creator.Building;

/// <summary>A version of the game with the plan of a package for it.</summary>
/// <param name="Game">The game as the library reports it right before the build.</param>
/// <param name="Plan">The package's plan over that version.</param>
public sealed record BuildVersion(InstalledGame Game, PackagePlan Plan);

public sealed record BuildRequest
{
    /// <summary>Where the package goes.</summary>
    public required string OutputPath { get; init; }

    /// <summary>The game as the library reports it right before the build, so the package names the build its patches were made against.</summary>
    public required InstalledGame Game { get; init; }

    /// <summary>The other versions the package serves, which <see cref="PackageGroups"/> found to take the same bytes.</summary>
    public IReadOnlyList<BuildVersion> AlsoFor { get; init; } = [];

    /// <summary>Every install the app keeps a record of; one on any of the games refuses the build.</summary>
    public IReadOnlyList<ExistingInstall> Installs { get; init; } = [];

    /// <summary>What the site takes as release files; what it does not say is not checked.</summary>
    public ReleaseFileRules Site { get; init; } = new();
}

/// <summary>A built package.</summary>
/// <param name="OutputPath">Full path of the built package.</param>
/// <param name="Kind">Whether it is a plain .zip or a manifest package.</param>
/// <param name="Manifest">Null for a plain .zip.</param>
/// <param name="Operations">What the package does, in order; each file of a plain .zip is a copy to its own name.</param>
/// <param name="PackageSize">In bytes.</param>
/// <param name="Findings">
/// Warnings only the build could find: the ones the author acknowledged, and why the site would not take the package.
/// </param>
public sealed record PackageBuildResult(
    string OutputPath,
    PackageKind Kind,
    PackageManifest? Manifest,
    ValueList<PackageOperation> Operations,
    long PackageSize,
    ValueList<PlanFinding> Findings);
