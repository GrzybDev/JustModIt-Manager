using JustModIt.Manager.Core.Automation;
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
            NoticeKind.AvailableSummary => (Strings.NoticeAvailableSummaryTitle,
                Text.Format(Strings.NoticeAvailableSummary, ("count", notice.Games.Count))),
            NoticeKind.Installed => (Title(Strings.NoticeInstalledTitle), Strings.NoticeInstalled),
            NoticeKind.Updated => (Title(Strings.NoticeUpdatedTitle),
                notice.ReleaseLabel is { Length: > 0 } release ? Text.Format(Strings.NoticeUpdated, ("release", release)) : Strings.NoticeUpdatedLatest),
            NoticeKind.Repaired => (Title(Strings.NoticeRepairedTitle), Strings.NoticeRepaired),
            NoticeKind.PreviousReleaseLost => (Title(Strings.NoticePreviousReleaseLostTitle), Strings.NoticePreviousReleaseLost),
            _ => (Text.Format(Strings.NoticeFailedTitle, ("job", notice.Job?.ToString() ?? ""), ("game", game)), Strings.NoticeFailed),
        };
    }
}
