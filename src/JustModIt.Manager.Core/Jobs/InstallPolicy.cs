using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Jobs;

/// <summary>Whether a game holds the installs of several projects at once, which each app decides for itself.</summary>
public interface IInstallPolicy
{
    /// <summary>What a new install on a game does with the installs already there.</summary>
    /// <param name="installs">The installs in the game's folder, test installs included.</param>
    InstallAdmission Admit(IReadOnlyList<InstallRecord> installs);
}

/// <summary>What a new install does with the installs already on its game.</summary>
public sealed record InstallAdmission
{
    /// <summary>It goes next to them; the engine still refuses files another install owns.</summary>
    public static InstallAdmission Alongside { get; } = new();

    /// <summary>
    /// The install it takes the place of the way an update does: the new package is downloaded and checked against the
    /// game as the uninstall will leave it, and the replaced release is put back if anything fails.
    /// </summary>
    public InstallRecord? Replaces { get; private init; }

    /// <summary>Why it may not go ahead.</summary>
    public JobFailureReason? Refusal { get; private init; }

    /// <summary>A new install that takes the place of an install, the way an update does.</summary>
    /// <param name="record">The install to replace.</param>
    public static InstallAdmission Replacing(InstallRecord record) => new() { Replaces = record };

    /// <summary>A new install that may not go ahead.</summary>
    /// <param name="reason">Why it may not.</param>
    public static InstallAdmission Refused(JobFailureReason reason) => new() { Refusal = reason };

    // Installs in another folder are left out, since a replacement uninstalls and installs in one folder.
    internal static InstallAdmission For(IInstallPolicy policy, InstalledGame game, IEnumerable<InstallRecord> records) =>
        policy.Admit([.. records.Where(record =>
            record.Game == game.Key && PathComparison.AreSame(record.InstallDirectory, game.InstallDirectory))]);
}

/// <summary>The platform's rule: projects share a game as long as their files do not overlap.</summary>
public sealed class SideBySideInstallPolicy : IInstallPolicy
{
    public static SideBySideInstallPolicy Instance { get; } = new();

    public InstallAdmission Admit(IReadOnlyList<InstallRecord> installs) => InstallAdmission.Alongside;
}
