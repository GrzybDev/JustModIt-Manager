using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>What an install changed and where it came from, as its record keeps it, so it shows offline.</summary>
public sealed record InstallView
{
    public required Guid Id { get; init; }

    public required DateTimeOffset InstalledAt { get; init; }

    public required string InstallDirectory { get; init; }

    public string? GameBuildId { get; init; }

    public int FileCount { get; init; }

    public PackageKind PackageKind { get; init; }

    public string PackageFileName { get; init; } = "";

    public Guid? ProjectId { get; init; }

    public string? ProjectName { get; init; }

    public string? TeamName { get; init; }

    public Guid? ReleaseId { get; init; }

    public string? ReleaseLabel { get; init; }

    public int? ReleaseVersion { get; init; }

    public string? VariantName { get; init; }

    public Uri? CoverUrl { get; init; }

    /// <summary>A copy of the cover in the app's cache, shown offline.</summary>
    public string? CoverPath { get; init; }

    public BackupLocation BackupLocation { get; init; }

    public required string BackupDirectory { get; init; }

    /// <summary>A package the package creator installed to try it out; never updated or repaired.</summary>
    public bool IsTest { get; init; }

    public IReadOnlyList<ThirdPartyNoticeView> ThirdPartyNotices { get; init; } = [];

    public static InstallView From(InstallRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var source = record.Source;
        return new InstallView
        {
            Id = record.Id,
            InstalledAt = record.InstalledAt,
            InstallDirectory = record.InstallDirectory,
            GameBuildId = record.GameBuildId,
            FileCount = record.FileCount,
            PackageKind = record.Package.Kind,
            PackageFileName = record.Package.FileName,
            ProjectId = Guid.TryParse(source?.ProjectId, out var project) ? project : null,
            ProjectName = source?.ProjectName,
            TeamName = source?.TeamName,
            ReleaseId = Guid.TryParse(source?.ReleaseId, out var release) ? release : null,
            ReleaseLabel = source?.ReleaseLabel,
            ReleaseVersion = source?.ReleaseVersion,
            VariantName = source?.VariantName,
            CoverUrl = source?.CoverUrl,
            CoverPath = source?.CoverPath,
            BackupLocation = record.Backup.Location,
            BackupDirectory = record.Backup.Directory,
            IsTest = record.IsTest,
            ThirdPartyNotices = [.. record.Package.ThirdPartyNotices?.Select(ThirdPartyNoticeView.From) ?? []],
        };
    }
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
        ArgumentNullException.ThrowIfNull(notice);
        return new ThirdPartyNoticeView(notice.Name, notice.License, notice.SourceUrl);
    }
}

/// <summary>An operation a crash interrupted that could not be finished or undone yet; its game stays untouched.</summary>
/// <param name="RecordId">The install the operation was for.</param>
/// <param name="Outcome">Why it is still pending.</param>
/// <param name="GameDirectory">The game's folder; null when the journal is too damaged to tell.</param>
public sealed record RecoveryView(Guid RecordId, RecoveryOutcome Outcome, string? GameDirectory = null)
{
    /// <summary>Maps an operation that crash recovery left pending.</summary>
    /// <param name="operation">What recovery found.</param>
    public static RecoveryView From(RecoveredOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new RecoveryView(operation.RecordId, operation.Outcome, operation.GameDirectory);
    }
}

/// <summary>Backups of a game its store confirms is gone, which the player may remove.</summary>
/// <param name="RecordId">The forgotten install, as <see cref="Messages.RemoveLeftoverBackups"/> names it.</param>
/// <param name="Game">The game that is gone.</param>
/// <param name="GameName">The game's name as it was last seen.</param>
/// <param name="Directory">The folder holding the backups.</param>
/// <param name="Bytes">The backups' size.</param>
public sealed record LeftoverBackupView(Guid RecordId, GameKey Game, string GameName, string Directory, long Bytes)
{
    /// <summary>Maps backups that a forgotten install left.</summary>
    /// <param name="backup">The backups the engine found.</param>
    public static LeftoverBackupView From(LeftoverBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        return new LeftoverBackupView(backup.RecordId, backup.Game, backup.GameName, backup.Directory, backup.Bytes);
    }
}
