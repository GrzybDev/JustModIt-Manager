using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;

namespace JustModIt.Manager.Core.Automation;

/// <summary>
/// Decides what the app does on its own for one game, the game's own choices overriding the app's settings; pure, since
/// the history says what was done already and nothing here starts anything.
/// </summary>
internal static class AutomationPolicy
{
    /// <summary>Decides what to do for a game and each install on it.</summary>
    /// <param name="game">The game as the state shows it.</param>
    /// <param name="settings">The app's settings, which the game's preferences override.</param>
    /// <param name="history">What the automation did already.</param>
    /// <param name="now">The current time, against which retries are held back.</param>
    /// <returns>
    /// One decision for a game with nothing installed, otherwise one per install, since only the player adds projects to
    /// a game or swaps them.
    /// </returns>
    public static IReadOnlyList<AutomationDecision> Decide(MyGame game, AppSettings settings, AutomationHistory history, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(history);
        if (!settings.OnboardingCompleted)
        {
            return [AutomationDecision.Skip(AutomationReason.OnboardingPending)];
        }

        // Hidden means left alone, updates and repairs of what is installed included.
        if (game.IsHidden)
        {
            return [AutomationDecision.Skip(AutomationReason.Hidden)];
        }

        if (game.Installs.Count == 0)
        {
            return [game.Status.State switch
            {
                MyGameState.NotInstalled => DecideInstall(game, settings.ResolveAutoInstall(game.Key), history, now),
                MyGameState.Busy => AutomationDecision.Skip(AutomationReason.Busy),
                _ => AutomationDecision.Skip(AutomationReason.GameNotReady),
            }];
        }

        return [.. game.Installs.Select(install => Decide(game, install, settings, history, now) with { Install = install })];
    }

    private static AutomationDecision Decide(MyGame game, MyInstall install, AppSettings settings, AutomationHistory history,
        DateTimeOffset now) => install.Status.State switch
    {
        MyGameState.UpdateAvailable when settings.ResolveAutoUpdate(game.Key) => AttemptUpdate(game, install, history, now),
        MyGameState.UpdateAvailable => install.Project?.Selection.Release is { } release
            ? Announce(game, release, AutomationReason.AutoUpdateOff, history)
            : AutomationDecision.Skip(AutomationReason.AutoUpdateOff),

        // A player who turned automatic updates off for a game is only told of its repairs too.
        MyGameState.NeedsRepair when settings.ReapplyAfterGameUpdate && game.Preferences.AutoUpdate != false =>
            AttemptRepair(game, install, history, now),
        MyGameState.NeedsRepair => history.WasRepairAnnounced(install.Record.Id, game.Game?.BuildId)
            ? AutomationDecision.Skip(AutomationReason.AlreadyAnnounced)
            : new AutomationDecision(AutomationAction.Announce, AutomationReason.ReapplyOff),
        MyGameState.Busy => AutomationDecision.Skip(AutomationReason.Busy),
        MyGameState.GameNotReady => AutomationDecision.Skip(AutomationReason.GameNotReady),
        MyGameState.Orphaned => AutomationDecision.Skip(AutomationReason.Orphaned),
        MyGameState.BackupsUnavailable => AutomationDecision.Skip(AutomationReason.BackupsUnavailable),
        MyGameState.TestInstalled => AutomationDecision.Skip(AutomationReason.TestInstall),
        _ => AutomationDecision.Skip(AutomationReason.UpToDate),
    };

    private static AutomationDecision DecideInstall(MyGame game, bool autoInstall, AutomationHistory history, DateTimeOffset now)
    {
        if (game.Project is not { Selection: { Release: { } release } selection })
        {
            return AnnounceChoice(game, history);
        }

        var (variant, _) = selection.Choose(game.Preferences.VariantName);
        // The player can still install it by hand; the app just does not push it on a game its store already offers in
        // the language.
        var reason = !autoInstall ? AutomationReason.AutoInstallOff
            : game.OffersTargetLanguage ? AutomationReason.TargetLanguageOffered
            : (AutomationReason?)null;
        if (reason is { } announced)
        {
            return Announce(game, release, announced, history);
        }

        var install = Attempt(game, AutomationAction.Install, release, history, now);
        return install.Action == AutomationAction.Install ? install with { Variant = variant } : install;
    }

    private static AutomationDecision Announce(MyGame game, Release release, AutomationReason reason, AutomationHistory history) =>
        history.WasAnnounced(game.Key, release.Uuid)
            ? AutomationDecision.Skip(AutomationReason.AlreadyAnnounced)
            : new AutomationDecision(AutomationAction.Announce, reason)
            {
                Release = release,
                ReleaseId = release.Uuid,
                Announces = [release.Uuid],
            };

    // The choice between teams is the player's alone; the automation only asks for it.
    private static AutomationDecision AnnounceChoice(MyGame game, AutomationHistory history)
    {
        ValueList<Guid> releases = [.. game.Projects.Select(offer => offer.Selection.Release?.Uuid).OfType<Guid>()];
        return releases.All(release => history.WasAnnounced(game.Key, release))
            ? AutomationDecision.Skip(AutomationReason.AlreadyAnnounced)
            : new AutomationDecision(AutomationAction.Announce, AutomationReason.NoProjectChosen) { Announces = releases };
    }

    // Updates and repairs keep the installed variant: another one would swap e.g. the player's voice-over for text only,
    // while the one installed may just not be ready yet in the new release.
    private static AutomationDecision AttemptUpdate(MyGame game, MyInstall install, AutomationHistory history, DateTimeOffset now) =>
        install is { Status.Update: { VariantFellBack: false, Variant: { } variant }, Project.Selection.Release: { } release }
            ? Attempt(game, AutomationAction.Update, release, history, now) is { Action: AutomationAction.Update } update
                ? update with { Variant = variant }
                : AutomationDecision.Skip(AutomationReason.RecentlyTried)
            : AutomationDecision.Skip(AutomationReason.VariantUnavailable);

    private static AutomationDecision AttemptRepair(MyGame game, MyInstall install, AutomationHistory history, DateTimeOffset now)
    {
        if (install.Project is { Selection: var selection } && install.Record.Source?.VariantName is { } installed
            && selection.FindInstallable(installed) is null)
        {
            return AutomationDecision.Skip(AutomationReason.VariantUnavailable);
        }

        // A repair installs the current release, so a newer one is a new try even when the installed one stopped fitting.
        var target = install.Project?.Selection.Release?.Uuid
            ?? install.Record.Source?.ReleaseUuid ?? Guid.Empty;
        return history.CanTry(game.Key, target, AutomationAction.Repair, game.Game?.BuildId, now)
            ? new AutomationDecision(AutomationAction.Repair) { ReleaseId = target, Release = install.Project?.Selection.Release }
            : AutomationDecision.Skip(AutomationReason.RecentlyTried);
    }

    private static AutomationDecision Attempt(MyGame game, AutomationAction action, Release release, AutomationHistory history,
        DateTimeOffset now) =>
        history.CanTry(game.Key, release.Uuid, action, game.Game?.BuildId, now)
            ? new AutomationDecision(action) { Release = release, ReleaseId = release.Uuid }
            : AutomationDecision.Skip(AutomationReason.RecentlyTried);
}
