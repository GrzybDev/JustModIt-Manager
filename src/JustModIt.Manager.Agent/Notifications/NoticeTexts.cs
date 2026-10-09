using JustModIt.Manager.Core.Automation;
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
        string Title(string pattern) => Text.Format(pattern, ("game", game));
        return notice.Kind switch
        {
            NoticeKind.Available => (Title(Strings.NoticeAvailableTitle),
                notice.OffersTargetLanguage ? Strings.NoticeAvailableOfficial
                : notice.TeamName is { } team ? Text.Format(Strings.NoticeAvailable, ("team", team))
                : Strings.NoticeAvailableAnyTeam),
            NoticeKind.ChoiceNeeded => (Title(Strings.NoticeChoiceNeededTitle), Strings.NoticeChoiceNeeded),
            NoticeKind.AvailableSummary => (Strings.NoticeAvailableSummaryTitle, Summary(notice)),
            NoticeKind.UpdateAvailable => (Title(Strings.NoticeUpdateAvailableTitle),
                notice.ReleaseLabel is { Length: > 0 } label
                    ? Text.Format(Strings.NoticeUpdateAvailable, ("release", label))
                    : Strings.NoticeUpdateAvailableLatest),
            NoticeKind.RepairNeeded => (Title(Strings.NoticeRepairNeededTitle), Strings.NoticeRepairNeeded),
            NoticeKind.Installed => (Title(Strings.NoticeInstalledTitle), Strings.NoticeInstalled),
            NoticeKind.Updated => (Title(Strings.NoticeUpdatedTitle),
                notice.ReleaseLabel is { Length: > 0 } release ? Text.Format(Strings.NoticeUpdated, ("release", release)) : Strings.NoticeUpdatedLatest),
            NoticeKind.Repaired => (Title(Strings.NoticeRepairedTitle), Strings.NoticeRepaired),
            NoticeKind.PreviousReleaseLost => (Title(Strings.NoticePreviousReleaseLostTitle), Strings.NoticePreviousReleaseLost),
            NoticeKind.Forgotten => (Title(Strings.NoticeForgottenTitle),
                notice.KeptBackups ? Strings.NoticeForgottenKeepingBackups : Strings.NoticeForgotten),
            _ => (Text.Format(Strings.NoticeFailedTitle, ("job", notice.Job?.ToString() ?? ""), ("game", game)),
                notice.Failure?.Reason == JobFailureReason.ReleaseUnavailable ? Strings.NoticeReleaseUnavailable : Strings.NoticeFailed),
        };
    }

    private static string Summary(AutomationNotice notice)
    {
        (string Pattern, int Count)[] parts =
        [
            (Strings.NoticeAvailableSummary, notice.Games.Count - notice.Updates.Count - notice.Repairs.Count),
            (Strings.NoticeUpdatesSummary, notice.Updates.Count),
            (Strings.NoticeRepairsSummary, notice.Repairs.Count),
        ];
        return string.Join(" ", parts.Where(part => part.Count > 0).Select(part => Text.Format(part.Pattern, ("count", part.Count))));
    }
}
