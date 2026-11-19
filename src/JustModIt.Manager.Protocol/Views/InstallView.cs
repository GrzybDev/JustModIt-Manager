using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>
/// An install on a game with what its record keeps, so it shows offline; the files it changed stay in the record, so a
/// long list cannot swell every snapshot.
/// </summary>
public sealed record InstallView
{
    /// <summary>The id commands about the install address it by.</summary>
    public required Guid Id { get; init; }

    public required GameStatusView Status { get; init; }

    /// <inheritdoc cref="MyInstall.BackupsUnavailable"/>
    public bool BackupsUnavailable { get; init; }

    /// <inheritdoc cref="MyInstall.MissingOriginals"/>
    public IReadOnlyList<string> MissingOriginals { get; init; } = [];

    /// <inheritdoc cref="MyInstall.Project"/>
    public ProjectOfferView? Project { get; init; }

    /// <inheritdoc cref="MyInstall.LastFailure"/>
    public FailureView? LastFailure { get; init; }

    /// <inheritdoc cref="MyInstall.NeedsAttention"/>
    public bool NeedsAttention { get; init; }

    public required DateTimeOffset InstalledAt { get; init; }

    public required string InstallDirectory { get; init; }

    public string? GameBuildId { get; init; }

    public int FileCount { get; init; }

    public PackageKind PackageKind { get; init; }

    /// <summary>The file of the install's first package, its base.</summary>
    public string PackageFileName { get; init; } = "";

    /// <summary>The files of every package the install came from, in the order they installed.</summary>
    public IReadOnlyList<string> PackageFileNames { get; init; } = [];

    public Guid? ProjectId { get; init; }

    public string? ProjectName { get; init; }

    public string? TeamName { get; init; }

    public Guid? ReleaseId { get; init; }

    public string? ReleaseLabel { get; init; }

    public int? ReleaseVersion { get; init; }

    /// <summary>The variant of a release from before add-ons that the install has; null for none.</summary>
    public string? VariantName { get; init; }

    /// <summary>The add-ons the install has, named as they were when they were installed.</summary>
    public IReadOnlyList<InstalledFeature> Features { get; init; } = [];

    /// <summary>The add-ons the install went without, since they did not fit the game, with why.</summary>
    public IReadOnlyList<LeftOutFeature> LeftOut { get; init; } = [];

    public Uri? CoverUrl { get; init; }

    /// <summary>A copy of the cover in the app's cache, shown offline.</summary>
    public string? CoverPath { get; init; }

    public BackupLocation BackupLocation { get; init; }

    public required string BackupDirectory { get; init; }

    /// <summary>A package the package creator installed to try it out; never updated or repaired.</summary>
    public bool IsTest { get; init; }

    public IReadOnlyList<ThirdPartyNoticeView> ThirdPartyNotices { get; init; } = [];

    /// <summary>The game files the software of the install's packages changes while the game runs, as their manifests name them.</summary>
    public IReadOnlyList<string> RuntimeFiles { get; init; } = [];

    /// <inheritdoc cref="MyInstall.LaunchOptions"/>
    public LaunchOptionsView? LaunchOptions { get; init; }

    /// <summary>Maps an install with its status and record.</summary>
    /// <param name="install">The install as the engine's state holds it.</param>
    public static InstallView From(MyInstall install)
    {
        var record = install.Record;
        var source = record.Source;
        return new InstallView
        {
            Id = record.Id,
            Status = GameStatusView.From(install.Status),
            BackupsUnavailable = install.BackupsUnavailable,
            MissingOriginals = install.MissingOriginals,
            Project = install.Project is { } offer ? ProjectOfferView.From(offer, InstallChoices.InstalledReleaseOf(record)) : null,
            LastFailure = install.LastFailure is { } failure ? FailureView.From(failure) : null,
            NeedsAttention = install.NeedsAttention,
            InstalledAt = record.InstalledAt,
            InstallDirectory = record.InstallDirectory,
            GameBuildId = record.GameBuildId,
            FileCount = record.FileCount,
            PackageKind = record.Package.Kind,
            PackageFileName = record.Package.FileName,
            PackageFileNames = [.. Packages(record).Select(package => package.FileName)],
            ProjectId = source?.ProjectUuid,
            ProjectName = source?.ProjectName,
            TeamName = source?.TeamName,
            ReleaseId = source?.ReleaseUuid,
            ReleaseLabel = source?.ReleaseLabel,
            ReleaseVersion = source?.ReleaseVersion,
            VariantName = source?.VariantName,
            Features = InstallChoices.FeaturesOf(record),
            LeftOut = [.. record.LeftOutFeatures ?? []],
            CoverUrl = source?.CoverUrl,
            CoverPath = source?.CoverPath,
            BackupLocation = record.Backup.Location,
            BackupDirectory = record.Backup.Directory,
            IsTest = record.IsTest,
            ThirdPartyNotices = [.. Packages(record).SelectMany(package => package.ThirdPartyNotices ?? []).Select(ThirdPartyNoticeView.From)
                .Distinct()],
            RuntimeFiles = [.. Packages(record).SelectMany(package => package.RuntimeFiles ?? []).Distinct(PackagePath.Comparer)],
            LaunchOptions = install.LaunchOptions is { } options ? LaunchOptionsView.From(options) : null,
        };
    }

    // A record from before installs had parts names its one package.
    private static IEnumerable<InstalledPackage> Packages(InstallRecord record) =>
        record.Parts?.Select(part => part.Package) ?? [record.Package];
}

/// <summary>
/// A third-party component an installed package carries; the files it covers stay in the record, so a long list cannot
/// swell every snapshot.
/// </summary>
/// <param name="Name">The component's name.</param>
/// <param name="License">Its licence, as the package names it.</param>
/// <param name="SourceUrl">The address of its source.</param>
public sealed record ThirdPartyNoticeView(string Name, string License, string SourceUrl)
{
    /// <summary>Maps a notice of the installed package.</summary>
    /// <param name="notice">The notice as the install record keeps it.</param>
    public static ThirdPartyNoticeView From(ThirdPartyNotice notice)
    {
        return new ThirdPartyNoticeView(notice.Name, notice.License, notice.SourceUrl);
    }
}

/// <summary>The launch options an install's project needs, and whether Steam has them.</summary>
/// <param name="State">Whether Steam has them.</param>
/// <param name="Options">The options the project needs; empty when they were refused.</param>
/// <param name="Missing">Those of them the player's options in Steam lack, as last read; empty while unknown.</param>
public sealed record LaunchOptionsView(LaunchOptionsState State, IReadOnlyList<string> Options, IReadOnlyList<string> Missing)
{
    /// <summary>Maps what the install's project needs of the launch options.</summary>
    /// <param name="options">The launch options as the engine's state holds them.</param>
    public static LaunchOptionsView From(MyLaunchOptions options) => new(options.State, [.. options.Options], [.. options.Missing]);
}
