using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;

namespace JustModIt.Manager.Core.State;

/// <summary>What the last check of an install's files found.</summary>
/// <param name="At">When the files were checked.</param>
/// <param name="BuildId">The game's build when it was checked; a new build is checked again.</param>
/// <param name="Repair">Null when the files were as the install left them.</param>
internal sealed record Verification(DateTimeOffset At, string? BuildId, RepairReason? Repair)
{
    /// <summary>The most urgent thing wrong with the files; null when they are intact.</summary>
    /// <param name="result">What the check found for each file.</param>
    public static RepairReason? RepairOf(VerificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Files.Any(file => file.Integrity == FileIntegrity.Modified) ? RepairReason.FilesModified
            : result.Files.Any(file => file.Integrity == FileIntegrity.OriginalRestored) ? RepairReason.OriginalsRestored
            : result.Files.Any(file => file.Integrity == FileIntegrity.Missing) ? RepairReason.FilesMissing
            : null;
    }
}

/// <summary>
/// When an install's files are checked: on first sight, after the store changed the game's build or worked on it, once
/// a day and when the player opens the game, never while the store or a job works on the game.
/// </summary>
internal static class VerificationSchedule
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>A check dated further ahead than this comes from a clock that was wrong; it counts as not done.</summary>
    public static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromHours(1);

    /// <summary>When an install's files are due for a check.</summary>
    /// <param name="record">The install.</param>
    /// <param name="game">What its store reports; checks need the game idle in the install's folder.</param>
    /// <param name="hasJob">The game has a job, which holds every check back.</param>
    /// <param name="last">What the last check found; null before the first.</param>
    /// <param name="requested">The player opened the game, so it is checked whatever the last check found.</param>
    /// <param name="now">The current time.</param>
    /// <returns>When the install is due, possibly already; null while it must not be checked.</returns>
    public static DateTimeOffset? DueAt(InstallRecord record, InstalledGame? game, bool hasJob, Verification? last, bool requested,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (hasJob || record.IsTest || game is not { IsReady: true } || !PathComparison.AreSame(game.InstallDirectory, record.InstallDirectory))
        {
            return null;
        }

        var quiet = SafetyGate.QuietUntil(game.StoreActivityAt, now) ?? now;
        if (requested || last is null || last.BuildId != game.BuildId || last.At - now > ClockSkewTolerance)
        {
            return quiet;
        }

        var due = last.At + Interval;
        return due > quiet ? due : quiet;
    }
}
