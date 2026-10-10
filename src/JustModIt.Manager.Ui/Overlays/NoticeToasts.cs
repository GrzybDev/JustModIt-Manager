using Avalonia.Media;
using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Theme;

namespace JustModIt.Manager.Ui.Overlays;

/// <summary>
/// The automation's notices as the window shows them while it is in front: a short title of what happened and the game
/// it happened to in the line below, with a way to the game when there is something to do there.
/// </summary>
public static class NoticeToasts
{
    /// <summary>The notification of a notice.</summary>
    /// <param name="notice">A typed notice of the automation.</param>
    /// <param name="showGame">Opens the game a notice is about.</param>
    public static ToastViewModel Create(NoticeView notice, Action<NoticeView> showGame)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(showGame);
        var subject = notice.ProjectName is { } project
            ? Text.Format(Strings.Toast_ProjectOfGame, ("project", project), ("game", notice.GameName ?? ""))
            : notice.GameName ?? "";
        string About(string pattern, params (string Name, object? Value)[] more) =>
            Text.Format(pattern, [("game", subject), .. more]);
        var version = Formats.Version(notice.ReleaseLabel ?? "");

        var (tone, icon, title, message, offersGame) = notice.Kind switch
        {
            NoticeKind.Installed => (ToastTone.Success, Icons.CircleCheck, Strings.Toast_InstalledTitle,
                notice.TeamName is { } team ? About(Strings.Toast_Installed, ("team", team)) : About(Strings.Toast_InstalledAnyTeam), false),
            NoticeKind.Updated => (ToastTone.Success, Icons.CircleCheck, Strings.Toast_UpdatedTitle,
                version.Length > 0 ? About(Strings.Toast_Updated, ("version", version)) : About(Strings.Toast_UpdatedLatest), false),
            NoticeKind.Repaired => (ToastTone.Success, Icons.CircleCheck, Strings.Toast_RepairedTitle, About(Strings.Toast_Repaired), false),
            NoticeKind.Available => (ToastTone.Info, Icons.Download, Strings.Toast_AvailableTitle,
                notice.OffersTargetLanguage ? About(Strings.Toast_AvailableOfficial)
                : notice.TeamName is { } team ? About(Strings.Toast_Available, ("team", team))
                : About(Strings.Toast_AvailableAnyTeam), true),
            NoticeKind.ChoiceNeeded => (ToastTone.Info, Icons.Users, Strings.Toast_ChoiceNeededTitle, About(Strings.Toast_ChoiceNeeded), true),
            NoticeKind.AvailableSummary => (ToastTone.Info, Icons.Info, Strings.NoticeAvailableSummaryTitle, Summary(notice), false),
            NoticeKind.UpdateAvailable => (ToastTone.Info, Icons.CircleArrowUp, Strings.Toast_UpdateAvailableTitle,
                version.Length > 0 ? About(Strings.Toast_UpdateAvailable, ("version", version)) : About(Strings.Toast_UpdateAvailableLatest), true),
            NoticeKind.RepairNeeded => (ToastTone.Warning, Icons.Wrench, Strings.Toast_RepairNeededTitle, About(Strings.Toast_RepairNeeded), true),
            NoticeKind.Forgotten => (ToastTone.Warning, Icons.TriangleAlert, Strings.Toast_ForgottenTitle,
                notice.KeptBackups ? About(Strings.Toast_ForgottenKeepingBackups) : About(Strings.Toast_Forgotten), false),
            NoticeKind.PreviousReleaseLost => (ToastTone.Danger, Icons.CircleX, Strings.Toast_PreviousReleaseLostTitle,
                About(Strings.Toast_PreviousReleaseLost), true),
            _ => (ToastTone.Danger, Icons.CircleX,
                Text.Format(Strings.Toast_FailedTitle, ("job", notice.Job?.ToString() ?? "")),
                notice.Failure?.Reason == JobFailureReason.ReleaseUnavailable ? About(Strings.Toast_ReleaseUnavailable) : About(Strings.Toast_Failed),
                true),
        };

        return new ToastViewModel(tone, icon, title, message)
        {
            ActionText = offersGame && notice.Game is not null ? Strings.Toast_ShowGame : null,
            Action = offersGame && notice.Game is not null ? () => showGame(notice) : null,
        };
    }

    private static string Summary(NoticeView notice)
    {
        (string Pattern, int Count)[] parts =
        [
            (Strings.NoticeAvailableSummary, notice.Games.Count(game => !notice.Updates.Contains(game) && !notice.Repairs.Contains(game))),
            (Strings.NoticeUpdatesSummary, notice.Updates.Count),
            (Strings.NoticeRepairsSummary, notice.Repairs.Count),
        ];
        return string.Join(" ", parts.Where(part => part.Count > 0).Select(part => Text.Format(part.Pattern, ("count", part.Count))));
    }
}
