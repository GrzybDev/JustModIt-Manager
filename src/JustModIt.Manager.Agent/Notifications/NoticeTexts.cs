using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Agent.Notifications;

/// <summary>A typed notice in the app's words, for a notification from the tray.</summary>
public static class NoticeTexts
{
    /// <summary>The title and message of a notice, in the app's language.</summary>
    /// <param name="notice">A typed notice of the automation.</param>
    public static (string Title, string Message) For(AutomationNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var game = notice.GameName ?? notice.Game?.ToString() ?? "";
        var job = ("job", (object?)notice.Job?.ToString() ?? "");
        string Title(string pattern) => Text.Format(pattern, ("game", game), job);
        string InstallTitle(string pattern, string projectPattern) => notice.ProjectName is { } project
            ? Text.Format(projectPattern, ("project", project), ("game", game), job)
            : Title(pattern);
        string Versioned(string pattern, string latest) =>
            Formats.Version(notice.ReleaseLabel ?? "") is { Length: > 0 } version ? Text.Format(pattern, ("version", version)) : latest;
        return notice.Kind switch
        {
            NoticeKind.Available => (Title(Strings.NoticeAvailableTitle),
                notice.OffersTargetLanguage ? Strings.NoticeAvailableOfficial
                : notice.TeamName is { } team ? Text.Format(Strings.NoticeAvailable, ("team", team))
                : Strings.NoticeAvailableAnyTeam),
            NoticeKind.ChoiceNeeded => (Title(Strings.NoticeChoiceNeededTitle), Strings.NoticeChoiceNeeded),
            NoticeKind.AvailableSummary => (Strings.NoticeAvailableSummaryTitle,
                NoticeSummary.Format(notice.Games, notice.Updates, notice.Repairs)),
            NoticeKind.UpdateAvailable => (InstallTitle(Strings.NoticeUpdateAvailableTitle, Strings.NoticeUpdateAvailableProjectTitle),
                Versioned(Strings.NoticeUpdateAvailable, Strings.NoticeUpdateAvailableLatest)),
            NoticeKind.RepairNeeded => (InstallTitle(Strings.NoticeRepairNeededTitle, Strings.NoticeRepairNeededProjectTitle),
                Strings.NoticeRepairNeeded),
            NoticeKind.Installed => (Title(Strings.NoticeInstalledTitle), Strings.NoticeInstalled),
            NoticeKind.Updated => (InstallTitle(Strings.NoticeUpdatedTitle, Strings.NoticeUpdatedProjectTitle),
                Versioned(Strings.NoticeUpdated, Strings.NoticeUpdatedLatest)),
            NoticeKind.Repaired => (InstallTitle(Strings.NoticeRepairedTitle, Strings.NoticeRepairedProjectTitle), Strings.NoticeRepaired),
            NoticeKind.PreviousReleaseLost => (
                InstallTitle(Strings.NoticePreviousReleaseLostTitle, Strings.NoticePreviousReleaseLostProjectTitle),
                notice.ProjectName is null ? Strings.NoticePreviousReleaseLost : Strings.NoticePreviousReleaseLostProject),
            NoticeKind.Forgotten => (InstallTitle(Strings.NoticeForgottenTitle, Strings.NoticeForgottenProjectTitle),
                notice.KeptBackups ? Strings.NoticeForgottenKeepingBackups : Strings.NoticeForgotten),
            NoticeKind.StoreCheckAdvised => (InstallTitle(Strings.NoticeStoreCheckAdvisedTitle, Strings.NoticeStoreCheckAdvisedProjectTitle),
                Text.Format(Strings.NoticeStoreCheckAdvised, ("reason", KeptReasons.CaseOf(notice.Kept)))),
            _ => (InstallTitle(Strings.NoticeFailedTitle, Strings.NoticeFailedProjectTitle),
                notice.Failure?.Reason == JobFailureReason.ReleaseUnavailable ? Strings.NoticeReleaseUnavailable : Strings.NoticeFailed),
        };
    }
}
