using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;

namespace JustModIt.Manager.Core.Automation;

/// <summary>
/// Decides what the app does on its own for one game, the game's own choices overriding the app's settings; pure, since
/// the history says what was done already and nothing here starts anything.
/// </summary>
public static class AutomationPolicy
{
    public static AutomationDecision Decide(MyGame game, AppSettings settings, AutomationHistory history, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(history);
        if (!settings.OnboardingCompleted)
        {
            return AutomationDecision.Skip(AutomationReason.OnboardingPending);
        }

        // Hidden means left alone, updates and repairs of what is installed included.
        if (game.IsHidden)
        {
            return AutomationDecision.Skip(AutomationReason.Hidden);
        }

        return game.Status.State switch
        {
            MyGameState.NotInstalled => DecideInstall(game, settings.ResolveAutoInstall(game.Key), history, now),
            MyGameState.UpdateAvailable => settings.ResolveAutoUpdate(game.Key)
                ? AttemptUpdate(game, history, now)
                : AutomationDecision.Skip(AutomationReason.AutoUpdateOff),

            // A player who turned automatic updates off for a game wants it left alone, repairs included.
            MyGameState.NeedsRepair => settings.ReapplyAfterGameUpdate && game.Preferences.AutoUpdate != false
                ? AttemptRepair(game, history, now)
                : AutomationDecision.Skip(AutomationReason.ReapplyOff),
            MyGameState.Busy => AutomationDecision.Skip(AutomationReason.Busy),
            MyGameState.GameNotReady => AutomationDecision.Skip(AutomationReason.GameNotReady),
            MyGameState.Orphaned => AutomationDecision.Skip(AutomationReason.Orphaned),
            MyGameState.Unavailable => AutomationDecision.Skip(AutomationReason.Unavailable),
            MyGameState.BackupsUnavailable => AutomationDecision.Skip(AutomationReason.BackupsUnavailable),
            MyGameState.TestInstalled => AutomationDecision.Skip(AutomationReason.TestInstall),
            _ => AutomationDecision.Skip(AutomationReason.UpToDate),
        };
    }

    private static AutomationDecision DecideInstall(MyGame game, bool autoInstall, AutomationHistory history, DateTimeOffset now)
    {
        if (game.Project is not { Selection: { Release: { } release } selection })
        {
            return AutomationDecision.Skip(game.Project is null ? AutomationReason.NoProjectChosen : AutomationReason.Unavailable);
        }

        var (variant, _) = selection.Choose(game.Preferences.VariantName);
        if (variant is null)
        {
            return AutomationDecision.Skip(AutomationReason.Unavailable);
        }

        // The player can still install it by hand; the app just does not push it on a game its store already offers in
        // the language.
        var reason = !autoInstall ? AutomationReason.AutoInstallOff
            : game.OffersTargetLanguage ? AutomationReason.TargetLanguageOffered
            : (AutomationReason?)null;
        if (reason is not null)
        {
            return history.WasAnnounced(game.Key, release.Uuid)
                ? AutomationDecision.Skip(AutomationReason.AlreadyAnnounced)
                : new AutomationDecision(AutomationAction.Announce, reason) { Release = release, ReleaseId = release.Uuid, Variant = variant };
        }

        var install = Attempt(game, AutomationAction.Install, release, history, now);
        return install.Action == AutomationAction.Install ? install with { Variant = variant } : install;
    }

    // Updates and repairs keep the installed variant: another one would swap e.g. the player's voice-over for text only,
    // while the one installed may just not be ready yet in the new release.
    private static AutomationDecision AttemptUpdate(MyGame game, AutomationHistory history, DateTimeOffset now) =>
        game is { Status.Update: { VariantFellBack: false, Variant: { } variant }, Project.Selection.Release: { } release }
            ? Attempt(game, AutomationAction.Update, release, history, now) is { Action: AutomationAction.Update } update
                ? update with { Variant = variant }
                : AutomationDecision.Skip(AutomationReason.RecentlyTried)
            : AutomationDecision.Skip(AutomationReason.VariantUnavailable);

    private static AutomationDecision AttemptRepair(MyGame game, AutomationHistory history, DateTimeOffset now)
    {
        if (game.Project is { Selection: var selection } && game.Installed?.Source?.VariantName is { } installed
            && selection.FindInstallable(installed) is null)
        {
            return AutomationDecision.Skip(AutomationReason.VariantUnavailable);
        }

        // A repair installs the current release, so a newer one is a new try even when the installed one stopped fitting.
        var target = game.Project?.Selection.Release?.Uuid
            ?? (Guid.TryParse(game.Installed?.Source?.ReleaseId, out var installedRelease) ? installedRelease : Guid.Empty);
        return history.CanTry(game.Key, target, AutomationAction.Repair, game.Game?.BuildId, now)
            ? new AutomationDecision(AutomationAction.Repair) { ReleaseId = target, Release = game.Project?.Selection.Release }
            : AutomationDecision.Skip(AutomationReason.RecentlyTried);
    }

    private static AutomationDecision Attempt(MyGame game, AutomationAction action, Release release, AutomationHistory history,
        DateTimeOffset now) =>
        history.CanTry(game.Key, release.Uuid, action, game.Game?.BuildId, now)
            ? new AutomationDecision(action) { Release = release, ReleaseId = release.Uuid }
            : AutomationDecision.Skip(AutomationReason.RecentlyTried);
}
