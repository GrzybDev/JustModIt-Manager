using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;

namespace MenadzerSpolszczen;

/// <summary>A game holds one project: installing another takes the place of the one there.</summary>
internal sealed class OneProjectPerGamePolicy : IInstallPolicy
{
    public InstallAdmission Admit(IReadOnlyList<InstallRecord> installs) => installs switch
    {
        [] => InstallAdmission.Alongside,
        // A test install has no kept release to be put back from if the replacement fails.
        [{ IsTest: false } installed] => InstallAdmission.Replacing(installed),
        _ => InstallAdmission.Refused(JobFailureReason.OtherProjectInstalled),
    };
}
