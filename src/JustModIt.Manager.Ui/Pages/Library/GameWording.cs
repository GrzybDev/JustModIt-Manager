using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Ui.Pages.Library;

/// <summary>Words the player's games and their installs in the window's texts, from the typed states the agent sends.</summary>
internal static class GameWording
{
    public static string StoreName(StoreId store) => store.Value switch
    {
        "steam" => Strings.Library_StoreSteam,
        "gog" => Strings.Library_StoreGog,
        "epic" => Strings.Library_StoreEpic,
        _ => store.Value,
    };

    /// <summary>A release's version as the site names it, or its number; empty when neither says anything.</summary>
    public static string Version(string? label, int? number)
    {
        var version = label is { Length: > 0 } ? Formats.Version(label) : "";
        return version.Length > 0 ? version : number is > 0 ? Formats.Number(number.Value) : "";
    }

    public static string Version(ReleaseView? release) => release is null ? "" : Version(release.Name, release.Version);

    public static string Version(InstallView install) => Version(install.ReleaseLabel, install.ReleaseVersion);

    /// <summary>A short version label such as "v2"; empty for no version.</summary>
    public static string ShortVersion(string version) =>
        version.Length == 0 ? "" : Text.Format(Strings.Library_ShortVersion, ("version", version));

    /// <summary>What the store does with a game that is not ready, e.g. that it updates it.</summary>
    public static string GameState(GameInstallState state, GameStateReason reason, StoreId store) =>
        Text.Format(Strings.Library_GameState, ("state", state.ToString()), ("reason", reason.ToString()), ("store", StoreName(store)));

    /// <summary>The release's age and date, as the site tells when a release came out.</summary>
    public static string Released(ReleaseView release, TimeProvider clock) =>
        release.Date is { } date
            ? Text.Format(release.DateKind == ReleaseDateKind.Released ? Strings.Library_Released : Strings.Library_ReleaseChanged,
                ("age", Formats.CalendarAge(date, clock)), ("date", Formats.LongDate(date, clock.LocalTimeZone)))
            : "";

    /// <summary>Why a job failed, as one sentence.</summary>
    /// <param name="failure">The failure; null for a job that was cancelled.</param>
    /// <param name="store">The game's store.</param>
    /// <param name="game">The game's name.</param>
    /// <param name="volumes">The system's drives and shares, which name the drive of the failure's place.</param>
    public static string FailureReason(JobFailureView? failure, StoreId store, string game, IReadOnlyList<NamedVolume> volumes)
    {
        if (failure is null)
        {
            return Strings.GamePage_FailureCancelled;
        }

        var drive = Drive(failure.Location ?? "", volumes);
        var state = failure.GameState is { } gameState ? GameState(gameState, failure.StateReason ?? GameStateReason.None, store) : "";
        return Text.Format(Strings.GamePage_FailureReason,
            ("reason", failure.Reason.ToString()),
            ("store", StoreName(store)),
            ("storeId", store.Value),
            ("game", game),
            ("state", state),
            ("processes", string.Join(", ", failure.Processes)),
            ("file", failure.Files.Count > 0 ? failure.Files[0] : ""),
            ("files", failure.Files.Count),
            ("volume", drive.Kind),
            ("drive", drive.Name),
            ("location", failure.Location ?? ""),
            ("required", failure.RequiredBytes is { } required ? Formats.FileSize(required) : ""),
            ("available", failure.AvailableBytes is { } available ? Formats.FileSize(available) : ""),
            ("app", failure.OtherApp ?? ""),
            ("unavailability", failure.Unavailability?.ToString() ?? ""));
    }

    /// <summary>
    /// The volume a path lies on: the deepest one the agent lists that holds it, else the path's root as the system that wrote
    /// the path names it, such as a Windows drive.
    /// </summary>
    /// <param name="path">A full path; any other, such as a project's folder from another system, lies on no listed volume.</param>
    /// <param name="volumes">The system's drives and shares from the agent's state.</param>
    public static NamedVolume Volume(string path, IReadOnlyList<NamedVolume> volumes)
    {
        NamedVolume? found = null;
        // A folder written on another system, such as a project's from Windows on Linux, lies on no drive here.
        if (Path.IsPathFullyQualified(path))
        {
            foreach (var volume in volumes)
            {
                if (PathComparison.IsWithin(volume.MountPoint, path) && volume.MountPoint.Length >= (found?.MountPoint.Length ?? 0))
                {
                    found = volume;
                }
            }
        }

        return found ?? new NamedVolume(CreatorProjectStore.RootOf(path));
    }

    /// <summary>
    /// How the drive texts name the drive a path lies on: <c>home</c> for the disk with the home folder, else <c>other</c>
    /// with the drive's label, its mount point or its letter.
    /// </summary>
    /// <param name="path">A full path; an empty one names no drive.</param>
    /// <param name="volumes">The system's drives and shares from the agent's state.</param>
    public static (string Kind, string Name) Drive(string path, IReadOnlyList<NamedVolume> volumes)
    {
        var volume = Volume(path, volumes);
        // A drive letter reads without its separator, as in "E:", and Linux's root has nothing else to read.
        var point = volume.MountPoint.TrimEnd('\\', '/') is { Length: > 0 } trimmed ? trimmed : volume.MountPoint;
        return (volume.HoldsHome ? "home" : "other", volume.Label ?? point);
    }

    /// <summary>What a job is doing now, e.g. "Downloading".</summary>
    public static string Stage(JobView job) => Text.Format(Strings.Library_JobStage, ("stage", job.Stage.ToString()), ("kind", job.Kind.ToString()));

    /// <summary>The share of a job done, rounded down; null while the job cannot tell.</summary>
    public static string? Percent(JobView job) => job.Fraction is { } fraction ? Formats.Percent((int)Math.Floor(fraction * 100)) : null;

    /// <summary>Parts of a detail line joined by the separator the texts use, e.g. a stage, its bytes and the time left.</summary>
    public static string Join(IEnumerable<string> parts) =>
        parts.Where(part => part.Length > 0).Aggregate("", (line, part) =>
            line.Length == 0 ? part : Text.Format(Strings.Library_Joined, ("first", line), ("second", part)));
}
